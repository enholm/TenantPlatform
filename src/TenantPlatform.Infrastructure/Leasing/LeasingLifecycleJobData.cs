using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingLifecycleJobData(IDbContextFactory<TenantPlatformDbContext> factory, ILogger<LeasingLifecycleJobData>? logger = null) : ILeasingLifecycleJobData
{
    public async Task<List<Guid>> ReadActiveAccountsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Accounts.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct); }
    public async Task<ILeasingLifecycleTransaction> BeginAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var active = await db.Accounts.AnyAsync(x => x.Id == account && x.IsActive, ct);
            var settings = await db.LeasingNotificationSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account, ct);
            var zone = await db.AgreementReminderSettings.AsNoTracking().Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct);
            var acquisitions = await db.LeasingAcquisitions.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var frameworks = await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var states = await db.LeasingLifecycles.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var equipment = await db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account).Include(x => x.Lines.Where(l => l.AccountId == account)).ToListAsync(ct);
            var followups = await db.LeasingFollowups.Where(x => x.AccountId == account).ToListAsync(ct);
            var memberships = await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account).Select(x => new { x.UserId, x.User.IsActive }).ToListAsync(ct);
            var roles = await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccount.AccountId == account).Select(x => new { x.UserAccount.UserId, x.Role }).ToListAsync(ct);
            var recipients = memberships.ToDictionary(x => x.UserId, x => new LeasingRecipientFacts(x.UserId, x.IsActive, true, roles.Where(r => r.UserId == x.UserId).Select(r => r.Role).ToArray()));
            return new Transaction(db, tx, account, new(active, settings, zone, acquisitions, frameworks, states, equipment, orders, followups, recipients));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public void ReportFailure(Guid account, string errorType) => logger?.LogError("Leasing lifecycle for {AccountId} failed with {ErrorType}.", account, errorType);
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, LeasingLifecycleWork state) : ILeasingLifecycleTransaction
    {
        public LeasingLifecycleWork State => state;
        public async Task SaveFollowupsAsync(CancellationToken ct)
        {
            foreach (var row in state.Followups)
            { if (row.AccountId != account) throw new UnauthorizedAccessException(); if (db.Entry(row).State == EntityState.Detached) db.LeasingFollowups.Add(row); }
            await db.SaveChangesAsync(ct);
        }
        public Task<List<LeasingNotification>> ReadNotificationsAsync(CancellationToken ct) => db.LeasingNotifications.Where(x => x.AccountId == account).ToListAsync(ct);
        public async Task CommitAsync(List<LeasingNotification> notifications, CancellationToken ct)
        {
            foreach (var row in notifications)
            {
                if (row.AccountId != account) throw new UnauthorizedAccessException();
                if (db.Entry(row).State == EntityState.Detached)
                { if (!state.Followups.Any(x => x.Id == row.FollowupId)) throw new UnauthorizedAccessException(); db.LeasingNotifications.Add(row); }
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
