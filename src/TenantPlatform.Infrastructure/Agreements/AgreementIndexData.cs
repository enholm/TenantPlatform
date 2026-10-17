using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementIndexData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementIndexData
{
    public async Task<AgreementIndexState> ReadAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return new(await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct), await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct), []);
    }
    public async Task<IAgreementIndexTransaction> BeginAsync(Guid account, bool includeProposals, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var indices = await db.AgreementIndexRecords.Where(x => x.AccountId == account).ToListAsync(ct);
            var values = await db.AgreementIndexValueRecords.Where(x => x.AccountId == account).ToListAsync(ct);
            var proposals = includeProposals ? await db.AgreementAdjustmentProposalRecords.Where(x => x.AccountId == account && x.Status == AgreementProposalStatus.Pending).ToListAsync(ct) : [];
            return new Transaction(db, tx, new(indices, values, proposals));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, AgreementIndexState state) : IAgreementIndexTransaction
    {
        private readonly HashSet<Guid> indices = state.Indices.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> values = state.Values.Select(x => x.Id).ToHashSet();
        public AgreementIndexState State => state;
        public async Task CommitAsync(CancellationToken ct)
        {
            db.AgreementIndexRecords.AddRange(state.Indices.Where(x => !indices.Contains(x.Id)));
            db.AgreementIndexValueRecords.AddRange(state.Values.Where(x => !values.Contains(x.Id)));
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
