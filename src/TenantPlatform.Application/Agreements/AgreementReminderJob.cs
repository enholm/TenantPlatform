using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed class AgreementReminderJob(IAgreementReminderData data, TimeProvider clock, IAgreementReminderTransport transport, ISystemWorkContext system)
{
    private void Demand() => SystemWorkAccess.Require(system, SystemWorkKind.AgreementReminders);
    public async Task RunAsync(CancellationToken ct = default)
    {
        Demand();
        foreach (var account in await data.ReadActiveAccountsAsync(ct))
        {
            try { await RunAccountAsync(account, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { data.ReportFailure(account, null, ex.GetType().Name, false); }
        }
    }
    private const int MaxAttempts = 3;

    public async Task RunAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        Demand();
        if (!await data.AccountActiveAsync(accountId, ct)) return;
        await data.EnsureDeadlinesAsync(accountId, ct);
        await PlanAccountAsync(accountId, ct);
        var now = clock.GetUtcNow();
        await data.ExpireReservationsAsync(accountId, now, "FollowupDeliveryUnknown", ct);
        var ids = await data.ReadReadyAsync(accountId, now, MaxAttempts, 200, ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { await DeliverAsync(accountId, id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { data.ReportFailure(accountId, id, ex.GetType().Name, false); }
        }
    }

    public async Task PlanAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        Demand();
        var ids = await data.ReadPlanningCandidatesAsync(accountId, ct);
        foreach (var id in ids)
        {
            await using var tx = await data.BeginPlanningAsync(accountId, id, ct);
            var state = tx.State; var settings = state.Settings;
            if (settings?.Enabled != true || !state.AccountActive) return;
            var d = state.Deadline ?? throw new InvalidOperationException("Missing account deadline.");
            var a = state.Agreement ?? throw new InvalidOperationException("Missing account agreement.");
            if (a.IsArchived || a.Status != AgreementStatus.Active || a.ReminderMode == AgreementReminderMode.Disabled ||
                d.State != AgreementDeadlineState.Current || d.Status == AgreementFollowupStatus.Completed) continue;
            var now = clock.GetUtcNow();
            var today = AgreementReminderSchedule.Today(now, settings.TimeZoneId);
            var days = AgreementReminderSchedule.EffectiveDays(a, settings);
            var dueDays = days.Where(n => AgreementReminderSchedule.Scheduled(d.DueDate, n, settings.TimeZoneId, settings.SendAt) <= now).ToArray();
            int? latest = dueDays.Length == 0 ? null : dueDays.Min();
            var existing = state.Rows;
            foreach (var threshold in days)
            {
                var row = existing.SingleOrDefault(x => x.DaysBefore == threshold);
                var scheduled = AgreementReminderSchedule.Scheduled(d.DueDate, threshold, settings.TimeZoneId, settings.SendAt);
                if (row is null)
                {
                    row = new() { Id = Guid.NewGuid(), AccountId = accountId, DeadlineId = id, DaysBefore = threshold, ScheduledUtc = scheduled };
                    state.Rows.Add(row);
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
            await tx.TryCommitPlanAsync(ct);
        }
    }

    private async Task DeliverAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var reservation = Guid.NewGuid();
        if (!await data.ClaimAsync(accountId, id, reservation, now, now.AddMinutes(10), MaxAttempts, ct)) return;
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
            data.ReportFailure(accountId, id, ex.GetType().Name, true);
            await FinishAsync(accountId, id, reservation, AgreementReminderStatus.Failed, key, null, ct);
        }
    }

    private async Task<AgreementReminderMessage?> PrepareAsync(Guid accountId, Guid id, Guid reservation, CancellationToken ct)
    {
        await using var tx = await data.BeginReservedAsync(accountId, id, reservation, ct);
        var state = tx.State; var row = state.Rows.SingleOrDefault();
        if (row is null) return null;
        var d = state.Deadline ?? throw new InvalidOperationException("Missing account deadline.");
        var a = state.Agreement ?? throw new InvalidOperationException("Missing account agreement.");
        var settings = state.Settings;
        string? reason = null;
        if (!state.AccountActive || settings?.Enabled != true ||
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
                row.NextAttemptUtc = null; row.Attempts--; await tx.CommitAsync(ct); return null;
            }
        }
        var recipient = state.RecipientFacts is { } facts && AgreementResponsibleRules.IsEligible(facts) ? state.Recipient : null;
        if (reason is null && (recipient is null || !System.Net.Mail.MailAddress.TryCreate(recipient.Email, out _))) reason = "FollowupMissingOwner";
        if (reason is not null)
        {
            row.Status = AgreementReminderStatus.Skipped; row.ResultKey = reason; row.ReservationId = null;
            await tx.CommitAsync(ct); return null;
        }
        row.RecipientUserId = recipient!.Id; row.RecipientAddress = recipient.Email;
        await tx.CommitAsync(ct);
        return new(row.Id, accountId, a.Id, recipient.Email, recipient.PreferredLanguage, a.Title, d.Kind, d.DueDate);
    }

    private async Task FinishAsync(Guid accountId, Guid id, Guid reservation, AgreementReminderStatus status,
        string? result, string? transportId, CancellationToken ct)
    {
        await using var tx = await data.BeginReservedAsync(accountId, id, reservation, ct);
        var row = tx.State.Rows.SingleOrDefault(); if (row is null) return;
        row.Status = status; row.ResultKey = result; row.ReservationId = null; row.ReservedUntilUtc = null;
        row.TransportId = transportId;
        if (status == AgreementReminderStatus.Sent) { row.SentUtc = clock.GetUtcNow(); row.NextAttemptUtc = null; }
        else row.NextAttemptUtc = row.Attempts < MaxAttempts ? clock.GetUtcNow().AddMinutes(Math.Pow(2, row.Attempts)) : null;
        await tx.CommitAsync(ct);
    }
}
