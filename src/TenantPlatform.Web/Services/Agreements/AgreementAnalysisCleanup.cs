using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public class AgreementAnalysisCleanup(IDbContextFactory<TenantPlatformDbContext> factory,
    IAgreementDocumentStorage storage, TimeProvider clock)
{
    public async Task CleanDraftAsync(Guid accountId, Guid draftId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var draft = await db.AgreementAnalyses.FromSqlInterpolated($"SELECT * FROM agreement_analyses WHERE \"AccountId\" = {accountId} AND \"Id\" = {draftId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (draft is null || draft.State == AgreementAnalysisState.Approved || draft.LeaseUntilUtc > clock.GetUtcNow()) return;
        await db.Entry(draft).Collection(x => x.Files).LoadAsync(ct);
        var discard = draft.State == AgreementAnalysisState.Discarded || draft.ExpiresUtc <= clock.GetUtcNow();
        foreach (var file in draft.Files.Where(x => discard || x.PendingDelete).ToList())
        {
            if (file.AgreementDocumentId.HasValue) throw new InvalidOperationException("Cannot discard an archived document.");
            // Hold the draft lock while deleting: approval cannot race cleanup. A failed delete retains
            // its database record for a future retry, including after a process restart.
            await storage.DiscardUncommittedAsync(file.StorageKey);
            db.AgreementAnalysisFiles.Remove(file);
        }
        if (discard) db.AgreementAnalyses.Remove(draft);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var drafts = await db.AgreementAnalyses.AsNoTracking().Where(x => x.State != AgreementAnalysisState.Approved &&
            (x.State == AgreementAnalysisState.Discarded || x.ExpiresUtc <= now || x.Files.Any(f => f.PendingDelete)))
            .Select(x => new { x.AccountId, x.Id }).ToListAsync(ct);
        foreach (var draft in drafts) await CleanDraftAsync(draft.AccountId, draft.Id, ct);
    }
    public async Task CleanOrphansAsync(string rootPath, CancellationToken ct)
    {
        if (!Directory.Exists(rootPath)) return;
        // Crash between file creation and metadata commit. Uploads are bounded to five minutes;
        // only files at least two days old are considered. No document endpoint deletes archived files.
        foreach (var path in Directory.EnumerateFiles(rootPath))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var key = name.EndsWith(".partial", StringComparison.Ordinal) ? name[..^8] : name;
            if (!Guid.TryParseExact(key, "N", out _) || File.GetLastWriteTimeUtc(path) >= clock.GetUtcNow().AddDays(-2).UtcDateTime) continue;
            await using var db = await factory.CreateDbContextAsync(ct);
            // Deliberately global: this internal storage reconciliation must protect ALL tenants' files.
            if (!await db.AgreementDocuments.AnyAsync(x => x.StorageKey == key, ct) &&
                !await db.AgreementAnalysisFiles.AnyAsync(x => x.StorageKey == key, ct)) File.Delete(path);
        }
    }
}
public class AgreementAnalysisCleanupWorker(IServiceScopeFactory scopes, IOptions<AgreementDocumentStorageOptions> options,
    ILogger<AgreementAnalysisCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<AgreementAnalysisCleanup>();
                await cleanup.RunAsync(stoppingToken);
                await cleanup.CleanOrphansAsync(options.Value.RootPath, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Agreement analysis cleanup failed; will retry."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
