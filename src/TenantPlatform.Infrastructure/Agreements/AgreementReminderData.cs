using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementReminderData(IDbContextFactory<TenantPlatformDbContext> factory, TimeProvider clock, ILogger<AgreementReminderData>? logger = null) : IAgreementReminderData
{
    public async Task<List<Guid>> ReadActiveAccountsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Accounts.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct); }
    public async Task<bool> AccountActiveAsync(Guid account, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Accounts.AnyAsync(x => x.Id == account && x.IsActive, ct); }
    public Task EnsureDeadlinesAsync(Guid account, CancellationToken ct) => new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(account, ct);
    public async Task<List<Guid>> ReadPlanningCandidatesAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from d in db.AgreementDeadlines.AsNoTracking() join a in db.Agreements.AsNoTracking() on d.AgreementId equals a.Id
            where d.AccountId == account && a.AccountId == account && d.State == AgreementDeadlineState.Current && d.Status != AgreementFollowupStatus.Completed select d.Id).ToListAsync(ct);
    }
    public async Task ExpireReservationsAsync(Guid account, DateTimeOffset now, string result, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.AgreementReminders.Where(x => x.AccountId == account && x.Status == AgreementReminderStatus.Processing && x.ReservedUntilUtc < now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AgreementReminderStatus.Failed).SetProperty(x => x.NextAttemptUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.ResultKey, result).SetProperty(x => x.ReservationId, (Guid?)null), ct);
    }
    private static IQueryable<AgreementReminder> Ready(TenantPlatformDbContext db, Guid account, DateTimeOffset now, int attempts) => db.AgreementReminders.Where(x =>
        x.AccountId == account && (x.Status == AgreementReminderStatus.Pending || x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null) && x.Attempts < attempts &&
        x.ScheduledUtc <= now && (x.NextAttemptUtc == null || x.NextAttemptUtc <= now));
    public async Task<List<Guid>> ReadReadyAsync(Guid account, DateTimeOffset now, int attempts, int limit, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await Ready(db, account, now, attempts).OrderBy(x => x.ScheduledUtc).Take(limit).Select(x => x.Id).ToListAsync(ct); }
    public async Task<bool> ClaimAsync(Guid account, Guid id, Guid reservation, DateTimeOffset now, DateTimeOffset expires, int attempts, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Ready(db, account, now, attempts).Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AgreementReminderStatus.Processing)
            .SetProperty(x => x.ReservationId, reservation).SetProperty(x => x.ReservedUntilUtc, expires).SetProperty(x => x.LastAttemptUtc, now).SetProperty(x => x.Attempts, x => x.Attempts + 1), ct) > 0;
    }
    public async Task<IAgreementReminderTransaction> BeginPlanningAsync(Guid account, Guid deadline, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct); var key = account.ToString("N") + ":" + deadline.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            var rows = await db.AgreementReminders.Where(x => x.AccountId == account && x.DeadlineId == deadline).ToListAsync(ct);
            return new Transaction(db, tx, await Load(db, account, deadline, rows, false, ct));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public async Task<IAgreementReminderTransaction> BeginReservedAsync(Guid account, Guid id, Guid reservation, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var row = await db.AgreementReminders.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id && x.ReservationId == reservation && x.Status == AgreementReminderStatus.Processing, ct);
            var state = row is null ? new AgreementReminderWork(false, null, null, null, [], null, null) : await Load(db, account, row.DeadlineId, [row], true, ct);
            return new Transaction(db, null, state);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private static async Task<AgreementReminderWork> Load(TenantPlatformDbContext db, Guid account, Guid deadline, List<AgreementReminder> rows, bool recipient, CancellationToken ct)
    {
        var active = await db.Accounts.AnyAsync(x => x.Id == account && x.IsActive, ct);
        var settings = await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account, ct);
        var d = await db.AgreementDeadlines.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == deadline, ct);
        var a = d is null ? null : await db.Agreements.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == d.AgreementId, ct);
        var userId = d?.AssignedUserId ?? a?.OwnerUserId;
        var facts = !recipient || a is null ? null : await AgreementFollowupAccess.Facts(db, account, a).SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var user = facts is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == facts.UserId, ct);
        return new(active, a, d, settings, rows, user, facts);
    }
    public void ReportFailure(Guid account, Guid? id, string errorType, bool transport)
    {
        if (transport) logger?.LogWarning("Agreement reminder {Id} transport failed with {ErrorType}.", id, errorType);
        else logger?.LogError("Agreement reminders for account {AccountId}, item {Id}, failed with {ErrorType}.", account, id, errorType);
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction? tx, AgreementReminderWork state) : IAgreementReminderTransaction
    {
        private readonly HashSet<Guid> ids = state.Rows.Select(x => x.Id).ToHashSet();
        public AgreementReminderWork State => state;
        public async Task CommitAsync(CancellationToken ct)
        {
            db.AgreementReminders.AddRange(state.Rows.Where(x => !ids.Contains(x.Id)));
            await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        }
        public async Task<bool> TryCommitPlanAsync(CancellationToken ct)
        {
            try { await CommitAsync(ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return false; }
        }
        public async ValueTask DisposeAsync() { try { if (tx is not null) await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
