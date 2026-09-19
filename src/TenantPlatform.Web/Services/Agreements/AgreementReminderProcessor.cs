using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public class AgreementReminderProcessor(IDbContextFactory<TenantPlatformDbContext> factory, TimeProvider clock,
    IAgreementReminderTransport transport, ILogger<AgreementReminderProcessor> logger)
{
    private const int MaxAttempts = 3;

    public async Task RunAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
            if (!await db.Accounts.AnyAsync(x => x.Id == accountId && x.IsActive, ct)) return;
        await new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(accountId, ct);
        await PlanAccountAsync(accountId, ct);
        var now = clock.GetUtcNow();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            // Do not resend an expired reservation: SMTP may have accepted it before a crash.
            await db.AgreementReminders.Where(x => x.AccountId == accountId && x.Status == AgreementReminderStatus.Processing &&
                x.ReservedUntilUtc < now).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, AgreementReminderStatus.Failed).SetProperty(x => x.NextAttemptUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.ResultKey, "FollowupDeliveryUnknown").SetProperty(x => x.ReservationId, (Guid?)null), ct);
        }
        List<Guid> ids;
        await using (var db = await factory.CreateDbContextAsync(ct))
            ids = await db.AgreementReminders.Where(x => x.AccountId == accountId &&
                (x.Status == AgreementReminderStatus.Pending || (x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null)) && x.Attempts < MaxAttempts &&
                x.ScheduledUtc <= now && (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
                .OrderBy(x => x.ScheduledUtc).Take(200).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { await DeliverAsync(accountId, id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError("Agreement reminder {Id} failed with {ErrorType}.", id, ex.GetType().Name); }
        }
    }

    public async Task PlanAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        List<Guid> ids;
        await using (var db = await factory.CreateDbContextAsync(ct))
            ids = await db.AgreementDeadlines.Where(d => d.AccountId == accountId && d.State == AgreementDeadlineState.Current &&
                d.Status != AgreementFollowupStatus.Completed).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            // Serialize only planning for this occurrence. This avoids multi-row unique-index
            // insert deadlocks between planners; no transport call is made in this transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var lockKey = accountId.ToString("N") + ":" + id.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            var settings = await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
            if (settings?.Enabled != true || !await db.Accounts.AnyAsync(x => x.Id == accountId && x.IsActive, ct)) return;
            var d = await db.AgreementDeadlines.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Id == id, ct);
            var a = await db.Agreements.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Id == d.AgreementId, ct);
            if (a.IsArchived || a.Status != AgreementStatus.Active || a.ReminderMode == AgreementReminderMode.Disabled ||
                d.State != AgreementDeadlineState.Current || d.Status == AgreementFollowupStatus.Completed) continue;
            var now = clock.GetUtcNow();
            var today = AgreementReminderSchedule.Today(now, settings.TimeZoneId);
            var days = AgreementReminderSchedule.EffectiveDays(a, settings);
            var dueDays = days.Where(n => AgreementReminderSchedule.Scheduled(d.DueDate, n, settings.TimeZoneId, settings.SendAt) <= now).ToArray();
            int? latest = dueDays.Length == 0 ? null : dueDays.Min();
            var existing = await db.AgreementReminders.Where(x => x.AccountId == accountId && x.DeadlineId == id).ToListAsync(ct);
            foreach (var threshold in days)
            {
                var row = existing.SingleOrDefault(x => x.DaysBefore == threshold);
                var scheduled = AgreementReminderSchedule.Scheduled(d.DueDate, threshold, settings.TimeZoneId, settings.SendAt);
                if (row is null)
                {
                    row = new() { Id = Guid.NewGuid(), AccountId = accountId, DeadlineId = id, DaysBefore = threshold, ScheduledUtc = scheduled };
                    db.AgreementReminders.Add(row);
                }
                if (row.Status == AgreementReminderStatus.Pending ||
                    (row.Status == AgreementReminderStatus.Failed && row.NextAttemptUtc.HasValue))
                {
                    row.ScheduledUtc = scheduled;
                    if (today > d.DueDate || (latest.HasValue && threshold > latest))
                    {
                        row.Status = AgreementReminderStatus.Skipped;
                        row.ResultKey = today > d.DueDate ? "FollowupPastDeadline" : "FollowupOlderThreshold";
                        row.NextAttemptUtc = null;
                    }
                }
            }
            try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* Another worker reserved a row; revalidation happens before transport. */ }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { /* Unique occurrence/threshold identity: another planner already inserted it. */ }
        }
    }

    private async Task DeliverAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var reservation = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var claimed = await db.AgreementReminders.Where(x => x.AccountId == accountId && x.Id == id &&
                (x.Status == AgreementReminderStatus.Pending || (x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null)) && x.Attempts < MaxAttempts &&
                x.ScheduledUtc <= now && (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AgreementReminderStatus.Processing)
                    .SetProperty(x => x.ReservationId, reservation).SetProperty(x => x.ReservedUntilUtc, now.AddMinutes(10))
                    .SetProperty(x => x.LastAttemptUtc, now).SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
            if (claimed == 0) return;
        }
        try
        {
            // Fresh context after reservation; no transaction spans the external call.
            var message = await PrepareAsync(accountId, id, reservation, ct);
            if (message is null) return;
            var transportId = await transport.SendAsync(message, ct);
            await FinishAsync(accountId, id, reservation, AgreementReminderStatus.Sent, null, transportId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var key = ex is AgreementValidationException ? "FollowupTransportNotConfigured" : "FollowupTransportFailed";
            logger.LogWarning("Agreement reminder {Id} transport failed with {ErrorType}.", id, ex.GetType().Name);
            await FinishAsync(accountId, id, reservation, AgreementReminderStatus.Failed, key, null, ct);
        }
    }

    private async Task<AgreementReminderMessage?> PrepareAsync(Guid accountId, Guid id, Guid reservation, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AgreementReminders.SingleAsync(x => x.AccountId == accountId && x.Id == id, ct);
        if (row.Status != AgreementReminderStatus.Processing || row.ReservationId != reservation) return null;
        var d = await db.AgreementDeadlines.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Id == row.DeadlineId, ct);
        var a = await db.Agreements.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Id == d.AgreementId, ct);
        var settings = await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
        string? reason = null;
        if (!await db.Accounts.AnyAsync(x => x.Id == accountId && x.IsActive, ct) || settings?.Enabled != true ||
            a.ReminderMode == AgreementReminderMode.Disabled) reason = "FollowupDisabled";
        else if (a.IsArchived || a.Status != AgreementStatus.Active || d.State != AgreementDeadlineState.Current) reason = "FollowupObsolete";
        else if (d.Status == AgreementFollowupStatus.Completed) reason = "FollowupCompleted";
        else
        {
            var now = clock.GetUtcNow();
            var days = AgreementReminderSchedule.EffectiveDays(a, settings);
            var latest = days.Where(n => AgreementReminderSchedule.Scheduled(d.DueDate, n, settings.TimeZoneId, settings.SendAt) <= now)
                .Order().Cast<int?>().FirstOrDefault();
            if (AgreementReminderSchedule.Today(now, settings.TimeZoneId) > d.DueDate) reason = "FollowupPastDeadline";
            else if (!days.Contains(row.DaysBefore)) reason = "FollowupDisabled";
            else if (latest.HasValue && row.DaysBefore > latest) reason = "FollowupOlderThreshold";
            else if (latest != row.DaysBefore)
            {
                row.Status = AgreementReminderStatus.Pending; row.ReservationId = null;
                row.ScheduledUtc = AgreementReminderSchedule.Scheduled(d.DueDate, row.DaysBefore, settings.TimeZoneId, settings.SendAt);
                row.NextAttemptUtc = null; row.Attempts--; await db.SaveChangesAsync(ct); return null;
            }
        }
        var recipient = await AgreementFollowupAccess.Editors(db, accountId, a).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == (d.AssignedUserId ?? a.OwnerUserId), ct);
        if (reason is null && (recipient is null || !System.Net.Mail.MailAddress.TryCreate(recipient.Email, out _))) reason = "FollowupMissingOwner";
        if (reason is not null)
        {
            row.Status = AgreementReminderStatus.Skipped; row.ResultKey = reason; row.ReservationId = null;
            await db.SaveChangesAsync(ct); return null;
        }
        row.RecipientUserId = recipient!.Id; row.RecipientAddress = recipient.Email;
        await db.SaveChangesAsync(ct);
        return new(row.Id, accountId, a.Id, recipient.Email, recipient.PreferredLanguage, a.Title, d.Kind, d.DueDate);
    }

    private async Task FinishAsync(Guid accountId, Guid id, Guid reservation, AgreementReminderStatus status,
        string? result, string? transportId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AgreementReminders.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == id &&
            x.ReservationId == reservation && x.Status == AgreementReminderStatus.Processing, ct);
        if (row is null) return;
        row.Status = status; row.ResultKey = result; row.ReservationId = null; row.ReservedUntilUtc = null;
        row.TransportId = transportId;
        if (status == AgreementReminderStatus.Sent) { row.SentUtc = clock.GetUtcNow(); row.NextAttemptUtc = null; }
        else row.NextAttemptUtc = row.Attempts < MaxAttempts ? clock.GetUtcNow().AddMinutes(Math.Pow(2, row.Attempts)) : null;
        await db.SaveChangesAsync(ct);
    }
}

public class AgreementReminderWorker(IServiceScopeFactory scopes, IOptions<AgreementReminderOptions> options,
    ILogger<AgreementReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>();
                List<Guid> accounts;
                await using (var db = await factory.CreateDbContextAsync(stoppingToken))
                    accounts = await db.Accounts.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var accountId in accounts)
                {
                    try { await scope.ServiceProvider.GetRequiredService<AgreementReminderProcessor>().RunAccountAsync(accountId, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogError("Agreement reminders for account {AccountId} failed with {ErrorType}.", accountId, ex.GetType().Name); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError("Agreement reminder worker failed with {ErrorType}.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
