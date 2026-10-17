using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementDocumentData(IDbContextFactory<TenantPlatformDbContext> factory, IAgreementDocumentStorage storage, ILogger<AgreementDocumentData> logger) : IAgreementDocumentData
{
    public async Task<Guid?> ReadRevisionAsync(Guid account, Guid agreement, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Agreements.AsNoTracking().Where(x => x.AccountId == account && x.Id == agreement).Select(x => (Guid?)x.Revision).SingleOrDefaultAsync(ct);
    }
    public async Task<AgreementDocument?> ReadDocumentAsync(Guid account, Guid document, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == document, ct);
    }
    public async Task CleanupUncommittedAsync(Guid account, string key)
    {
        // Ambiguous commits retain the binary unless absence of metadata can be proved.
        try
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            if (!await db.AgreementDocuments.AnyAsync(x => x.AccountId == account && x.StorageKey == key)) await storage.DiscardUncommittedAsync(key);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not clean up uncommitted agreement file {StorageKey}.", key); }
    }
    public async Task<IAgreementDocumentTransaction> BeginAsync(Guid account, Guid agreement, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {account} AND \"Id\" = {agreement} FOR UPDATE", ct);
            var a = await db.Agreements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct) ?? throw new UnauthorizedAccessException();
            return new Transaction(db, tx, a);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Agreement agreement) : IAgreementDocumentTransaction
    {
        public Agreement Agreement => agreement;
        public async Task CommitAsync(AgreementDocument document, CancellationToken ct)
        {
            if (document.AccountId != agreement.AccountId || document.AgreementId != agreement.Id) throw new UnauthorizedAccessException();
            db.AgreementDocuments.Add(document);
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
