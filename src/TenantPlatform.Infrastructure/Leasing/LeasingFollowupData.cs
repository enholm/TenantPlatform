using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingFollowupData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingFollowupData
{
    public async Task<List<LeasingNotification>> ReadHistoryAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingNotifications.AsNoTracking().Where(x => x.AccountId == account).OrderByDescending(x => x.ScheduledUtc).Take(200).ToListAsync(ct);
    }
    public async Task<LeasingNotificationSettings> ReadSettingsAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingNotificationSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account, ct) ?? new() { AccountId = account, TimeZoneId = await db.AgreementReminderSettings.Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo" };
    }
    public async Task<LeasingFollowupPage> ReadPageAsync(LeasingUserScope scope, LeasingFollowupFilter filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.AccountId; var user = scope.UserId; var elevated = scope.CanReadLifecycle;
        var acquisitions = db.LeasingAcquisitions.AsNoTracking().Where(x => x.AccountId == account && (elevated || x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)));
        var frameworks = db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == account && (elevated || x.OwnerUserId == user));
        var visible = db.LeasingFollowups.AsNoTracking().Where(x => x.AccountId == account && (x.AcquisitionId.HasValue && acquisitions.Any(a => a.Id == x.AcquisitionId) || x.FrameworkId.HasValue && frameworks.Any(f => f.Id == x.FrameworkId)));
        var query = visible;
        if (filter.Kind.HasValue) query = query.Where(x => x.Kind == filter.Kind);
        query = filter.Status.HasValue ? query.Where(x => x.Status == filter.Status) : query.Where(x => x.Status != LeasingFollowupStatus.Obsolete);
        if (filter.To.HasValue) query = query.Where(x => x.DueDate == null || x.DueDate <= filter.To);
        if (filter.Owner.HasValue) query = query.Where(x => x.OwnerUserId == filter.Owner);
        var count = await query.CountAsync(ct); var page = Math.Clamp(filter.Page, 1, Math.Max(1, (count + 49) / 50));
        var rows = await query.OrderBy(x => x.DueDate).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(ct);
        var names = await acquisitions.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        foreach (var f in await frameworks.ToListAsync(ct)) names[f.Id] = f.Name;
        var notes = await db.LeasingNotifications.AsNoTracking().Where(x => x.AccountId == account && x.RecipientUserId == user && x.Status == LeasingNotificationStatus.Delivered && visible.Any(t => t.Id == x.FollowupId)).OrderByDescending(x => x.DeliveredUtc).Take(100).ToListAsync(ct);
        var ids = notes.Select(x => x.FollowupId).ToArray();
        return new(rows, count, page, names, notes, await visible.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct));
    }
    public async Task<ILeasingFollowupTransaction> BeginAsync(Guid account, Guid? task, Guid? notification, Guid? request, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var note = notification.HasValue ? await db.LeasingNotifications.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == notification, ct) : null;
            var taskId = task ?? note?.FollowupId;
            var row = taskId.HasValue ? await db.LeasingFollowups.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == taskId, ct) : null;
            var settings = await db.LeasingNotificationSettings.SingleOrDefaultAsync(x => x.AccountId == account, ct);
            var old = request.HasValue ? await db.LeasingPaymentEvents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct) : null;
            return new Transaction(db, tx, account, row, note, settings, old);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, LeasingFollowup? row, LeasingNotification? note, LeasingNotificationSettings? settings, LeasingPaymentEvent? old) : ILeasingFollowupTransaction
    {
        public LeasingFollowup? TaskRow => row;
        public LeasingNotification? Notification => note;
        public LeasingNotificationSettings? Settings => settings;
        public LeasingPaymentEvent? PreviousRequest => old;
        public Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct) => LeasingLifecycleRecipientData.ReadAsync(db, account, user, row?.AcquisitionId, row?.FrameworkId, ct);
        public Task<int> CountActiveMembersAsync(Guid[] users, CancellationToken ct) => db.UserAccounts.CountAsync(x => x.AccountId == account && x.User.IsActive && users.Contains(x.UserId), ct);
        public async Task CommitAsync(LeasingNotificationSettings? value, LeasingPaymentEvent? history, CancellationToken ct)
        {
            if (value is not null) { if (value.AccountId != account) throw new UnauthorizedAccessException(); if (settings is null) db.LeasingNotificationSettings.Add(value); }
            if (history is not null) { if (history.AccountId != account || history.AcquisitionId != row?.AcquisitionId) throw new UnauthorizedAccessException(); db.LeasingPaymentEvents.Add(history); }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
