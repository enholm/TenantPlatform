using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementManagementData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementManagementData
{
    internal static async Task<AgreementReferenceState> References(TenantPlatformDbContext db, Guid account, CancellationToken ct) => new(
        await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
        await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
        new() {
            Indices = await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new AgreementOptionDto(x.Id, x.Name, null, true)).ToListAsync(ct),
            Counterparties = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new AgreementOptionDto(x.Id, x.Name, null, true)).ToListAsync(ct),
            Members = await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account && x.User.IsActive).OrderBy(x => x.User.FirstName).Select(x => new AgreementOptionDto(x.UserId, x.User.FirstName + " " + x.User.LastName, null, true)).ToListAsync(ct),
            Buildings = await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new AgreementOptionDto(x.Id, x.Name, null, true)).ToListAsync(ct),
            Units = await db.Units.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new AgreementOptionDto(x.Id, x.Name, x.BuildingId, true)).ToListAsync(ct) });
    public async Task<AgreementReferenceState> ReadReferencesAsync(Guid account, CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); return await References(db, account, ct); }
    public async Task<Agreement?> ReadAgreementAsync(Guid account, Guid agreement, CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); return await db.Agreements.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct); }
    public async Task<IAgreementManagementTransaction> BeginAsync(Guid account, Guid? agreement, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            if (agreement.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {account} AND \"Id\" = {agreement.Value} FOR UPDATE", ct);
            var a = agreement.HasValue ? await db.Agreements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct) ?? throw new UnauthorizedAccessException() : null;
            var state = new AgreementManagementState { Agreement = a, References = await References(db, account, ct),
                HasActivatedLines = a is not null && await db.AgreementLines.AnyAsync(x => x.AccountId == account && x.AgreementId == a.Id && x.ActivatedUtc != null, ct),
                LineVersions = a is null ? [] : await db.AgreementLineVersions.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == a.Id).ToListAsync(ct),
                Proposals = a is null ? [] : await db.AgreementAdjustmentProposalRecords.Where(x => x.AccountId == account && x.AgreementId == a.Id && x.Status == AgreementProposalStatus.Pending).ToListAsync(ct),
                Selections = a is null ? [] : await db.AgreementIndexSelections.Where(x => x.AccountId == account && x.AgreementId == a.Id).ToListAsync(ct),
                Grants = a is null ? [] : await db.AgreementAccess.Where(x => x.AccountId == account && x.AgreementId == a.Id).ToListAsync(ct),
                Deadlines = a is null ? [] : await db.AgreementDeadlines.Where(x => x.AccountId == account && x.AgreementId == a.Id).ToListAsync(ct) };
            return new Transaction(db, tx, account, state);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, AgreementManagementState state) : IAgreementManagementTransaction
    {
        private readonly bool creating = state.Agreement is null;
        private readonly Dictionary<Guid, AgreementAccess> grants = state.Grants.ToDictionary(x => x.Id);
        private readonly HashSet<Guid> selections = state.Selections.Select(x => x.Id).ToHashSet();
        public AgreementManagementState State => state;
        public Task SynchronizeAsync(DateTimeOffset now, Guid user, CancellationToken ct)
        {
            var a = state.Agreement ?? throw new InvalidOperationException();
            if (creating && db.Entry(a).State == EntityState.Detached) db.Agreements.Add(a);
            return AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, now, user, ct);
        }
        public async Task CommitAsync(CancellationToken ct)
        {
            var a = state.Agreement ?? throw new InvalidOperationException(); if (a.AccountId != account) throw new UnauthorizedAccessException();
            if (creating && db.Entry(a).State == EntityState.Detached) db.Agreements.Add(a);
            db.AgreementAccess.RemoveRange(grants.Values.Where(x => !state.Grants.Any(g => g.Id == x.Id)));
            db.AgreementAccess.AddRange(state.Grants.Where(x => !grants.ContainsKey(x.Id)));
            db.AgreementIndexSelections.AddRange(state.Selections.Where(x => !selections.Contains(x.Id)));
            db.AgreementNoticeHistory.AddRange(state.NoticeHistory); db.AgreementDeadlineHistory.AddRange(state.History);
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
