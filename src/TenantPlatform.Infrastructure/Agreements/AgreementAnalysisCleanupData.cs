using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementAnalysisCleanupData(IDbContextFactory<TenantPlatformDbContext> factory, string? rootPath = null) : IAgreementAnalysisCleanupData
{
    public async Task<List<AnalysisCleanupTarget>> ReadCandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementAnalyses.AsNoTracking().Where(x => x.State != AgreementAnalysisState.Approved && (x.State == AgreementAnalysisState.Discarded || x.ExpiresUtc <= now || x.Files.Any(f => f.AccountId == x.AccountId && f.PendingDelete)))
            .Select(x => new AnalysisCleanupTarget(x.AccountId, x.Id)).ToListAsync(ct);
    }
    public async Task<IAnalysisCleanupTransaction> BeginAsync(Guid account, Guid draft, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            var row = await db.AgreementAnalyses.FromSqlInterpolated($"SELECT * FROM agreement_analyses WHERE \"AccountId\" = {account} AND \"Id\" = {draft} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (row is not null) row.Files = await db.AgreementAnalysisFiles.Where(x => x.AccountId == account && x.AnalysisId == draft).ToListAsync(ct);
            return new Transaction(db, tx, row);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public Task<List<string>> ReadOrphanCandidatesAsync(DateTimeOffset olderThan, CancellationToken ct)
    {
        var keys = new List<string>();
        if (rootPath is null || !Directory.Exists(rootPath)) return Task.FromResult(keys);
        foreach (var path in Directory.EnumerateFiles(rootPath))
        {
            ct.ThrowIfCancellationRequested(); var name = Path.GetFileName(path); var key = name.EndsWith(".partial", StringComparison.Ordinal) ? name[..^8] : name;
            if (Guid.TryParseExact(key, "N", out _) && File.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime) keys.Add(name);
        }
        return Task.FromResult(keys);
    }
    private static string StorageKey(string candidate) => candidate.EndsWith(".partial", StringComparison.Ordinal) ? candidate[..^8] : candidate;
    public async Task<bool> IsReferencedAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var storageKey = StorageKey(key);
        return await db.AgreementDocuments.AnyAsync(x => x.StorageKey == storageKey, ct) || await db.AgreementAnalysisFiles.AnyAsync(x => x.StorageKey == storageKey, ct);
    }
    public Task DeleteOrphanAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rootPath is null || Path.GetFileName(key) != key || !Guid.TryParseExact(StorageKey(key), "N", out _)) throw new InvalidOperationException("Invalid orphan key.");
        File.Delete(Path.Combine(rootPath, key)); return Task.CompletedTask;
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, AgreementAnalysis? draft) : IAnalysisCleanupTransaction
    {
        private readonly List<AgreementAnalysisFile> files = draft?.Files.ToList() ?? [];
        public AgreementAnalysis? Draft => draft;
        public async Task CommitAsync(bool removeDraft, CancellationToken ct)
        {
            if (draft is null) return;
            db.AgreementAnalysisFiles.RemoveRange(files.Where(x => !draft.Files.Contains(x)));
            if (removeDraft) db.AgreementAnalyses.Remove(draft);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
