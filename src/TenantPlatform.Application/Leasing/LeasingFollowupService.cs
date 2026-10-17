using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;

public sealed record LeasingFollowupPage(List<LeasingFollowup> Rows, int Count, int Page, Dictionary<Guid,string> Names, List<LeasingNotification> Notifications, Dictionary<Guid,LeasingFollowup> NotificationTasks);
public sealed record LeasingFollowupFilter(LeasingFollowupKind? Kind, LeasingFollowupStatus? Status, DateOnly? To, Guid? Owner, int Page);
public interface ILeasingFollowupData
{
    Task<List<LeasingNotification>> ReadHistoryAsync(Guid account, CancellationToken ct);
    Task<LeasingNotificationSettings> ReadSettingsAsync(Guid account, CancellationToken ct);
    Task<LeasingFollowupPage> ReadPageAsync(LeasingUserScope scope, LeasingFollowupFilter filter, CancellationToken ct);
    Task<ILeasingFollowupTransaction> BeginAsync(Guid account, Guid? task, Guid? notification, Guid? request, CancellationToken ct);
}
public interface ILeasingFollowupTransaction : IAsyncDisposable
{
    LeasingFollowup? TaskRow { get; }
    LeasingNotification? Notification { get; }
    LeasingNotificationSettings? Settings { get; }
    LeasingPaymentEvent? PreviousRequest { get; }
    Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct);
    Task<int> CountActiveMembersAsync(Guid[] users, CancellationToken ct);
    Task CommitAsync(LeasingNotificationSettings? settings, LeasingPaymentEvent? history, CancellationToken ct);
}
public sealed class LeasingFollowupService(IAuthorizationService authorization, ILeasingFollowupData data, LeasingLifecycleJob initialization, TimeProvider clock)
{
    private async Task<LeasingUserScope> Actor(Guid account, CancellationToken ct) => LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
    private async Task<LeasingUserScope> Revalidate(LeasingUserScope original, CancellationToken ct)
    {
        var current = await Actor(original.AccountId, ct);
        if (current.UserId != original.UserId) throw new UnauthorizedAccessException();
        return current;
    }
    public async Task<List<LeasingNotification>> LeasingNotificationHistoryAsync(Guid account, CancellationToken ct = default)
    {
        if (!(await Actor(account, ct)).IsAdministrator) throw new UnauthorizedAccessException();
        return await data.ReadHistoryAsync(account, ct);
    }
    public async Task<LeasingNotificationSettings> GetLeasingNotificationSettingsAsync(Guid account, CancellationToken ct = default)
    {
        if (!(await Actor(account, ct)).IsAdministrator) throw new UnauthorizedAccessException();
        return await data.ReadSettingsAsync(account, ct);
    }
    public async Task SaveLeasingNotificationSettingsAsync(Guid account, LeasingNotificationSettings input, string reason, CancellationToken ct = default)
    {
        var revision = input.Revision; var enabled = input.Enabled; var zone = input.TimeZoneId;
        var rawDays = input.Days.ToArray(); var recipients = input.ExtraRecipientIds.ToArray();
        var actor = await Actor(account, ct);
        if (!actor.IsAdministrator) throw new UnauthorizedAccessException();
        await using var tx = await data.BeginAsync(account, null, null, null, ct);
        if (!(await Revalidate(actor, ct)).IsAdministrator) throw new UnauthorizedAccessException();
        Reason(reason); AgreementTimeZones.Validate(zone); var days = AgreementReminderSchedule.ValidateDays(rawDays);
        if (recipients.Length > 100 || await tx.CountActiveMembersAsync(recipients, ct) != recipients.Distinct().Count()) throw new LeasingValidationException("LifeInvalidOwner");
        var settings = tx.Settings ?? new() { AccountId = account };
        Revision(settings.Revision, revision); var before = JsonSerializer.Serialize(settings);
        if (enabled && !settings.Enabled) settings.ActivatedUtc = clock.GetUtcNow();
        settings.Enabled = enabled; settings.Days = days; settings.TimeZoneId = zone;
        settings.ExtraRecipientIds = recipients.Distinct().ToArray(); settings.Revision = Guid.NewGuid();
        await tx.CommitAsync(settings, History(account, actor.UserId, null, "LifecycleNotifications", reason, before, JsonSerializer.Serialize(settings), Guid.NewGuid()), ct);
    }
    public async Task<LeasingFollowupPage> ListLeasingFollowupsAsync(Guid account, LeasingFollowupKind? kind = null, LeasingFollowupStatus? status = null, DateOnly? to = null, Guid? owner = null, int page = 1, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct);
        // Preserve the existing account backfill for this GUI operation; the dedicated system
        // job still requires its exact trusted identity. This is deliberately not a pure reader.
        await initialization.RunAccountAsync(account, ct);
        actor = await Revalidate(actor, ct);
        return await data.ReadPageAsync(actor, new(kind, status, to, owner, page), ct);
    }
    public async Task UpdateLeasingFollowupAsync(Guid account, Guid id, Guid revision, LeasingFollowupStatus status, Guid? owner, string comment, Guid request, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct);
        await using var tx = await data.BeginAsync(account, id, null, request, ct);
        actor = await Revalidate(actor, ct);
        var task = tx.TaskRow ?? throw new UnauthorizedAccessException();
        var facts = await tx.ReadRecipientAsync(actor.UserId, ct);
        if (!LeasingLifecycleRecipientRules.CanRead(facts) || !actor.CanManageLifecycle && facts.Acquisition?.OwnerUserId != actor.UserId && facts.Framework?.OwnerUserId != actor.UserId) throw new UnauthorizedAccessException();
        if (Retry(tx.PreviousRequest, request, actor.UserId, task.AcquisitionId)) return;
        Revision(task.Revision, revision); Reason(comment);
        if (status is not (LeasingFollowupStatus.Open or LeasingFollowupStatus.InProgress or LeasingFollowupStatus.Completed) || task.Status == LeasingFollowupStatus.Obsolete) throw new LeasingValidationException("LeasingInvalidTransition");
        if (owner.HasValue && !LeasingLifecycleRecipientRules.CanRead(await tx.ReadRecipientAsync(owner.Value, ct))) throw new LeasingValidationException("LifeInvalidOwner");
        var before = JsonSerializer.Serialize(task); task.OwnerUserId = owner; task.AssignedExplicitly = true; task.Status = status; task.Comment = comment;
        task.CompletedByUserId = status == LeasingFollowupStatus.Completed ? actor.UserId : null;
        task.CompletedUtc = status == LeasingFollowupStatus.Completed ? clock.GetUtcNow() : null; task.Revision = Guid.NewGuid();
        await tx.CommitAsync(null, History(account, actor.UserId, task.AcquisitionId, "LifecycleTask", comment, before, JsonSerializer.Serialize(task), request), ct);
    }
    public async Task ReadLeasingNotificationAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct);
        await using var tx = await data.BeginAsync(account, null, id, null, ct);
        actor = await Revalidate(actor, ct);
        var note = tx.Notification ?? throw new UnauthorizedAccessException();
        if (note.RecipientUserId != actor.UserId || tx.TaskRow is null || !LeasingLifecycleRecipientRules.CanRead(await tx.ReadRecipientAsync(actor.UserId, ct))) throw new UnauthorizedAccessException();
        note.ReadUtc ??= clock.GetUtcNow(); await tx.CommitAsync(null, null, ct);
    }
    private static void Reason(string reason) { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired"); }
    private static void Revision(Guid actual, Guid supplied) { if (actual != supplied) throw new LeasingValidationException("LeasingConcurrency"); }
    private static bool Retry(LeasingPaymentEvent? old, Guid request, Guid user, Guid? acquisition)
    {
        if (request == Guid.Empty) throw new LeasingValidationException("PaymentInvalid");
        if (old is null) return false;
        if (old.ActorUserId != user || old.Action != "LifecycleTask" || old.AcquisitionId != acquisition || old.InvoiceId is not null) throw new LeasingValidationException("LeasingConcurrency");
        return true;
    }
    private LeasingPaymentEvent History(Guid account, Guid user, Guid? acquisition, string action, string reason, string before, string after, Guid request) =>
        new() { Id = request, AccountId = account, AcquisitionId = acquisition, ActorUserId = user, Action = action, Reason = reason, BeforeJson = before, AfterJson = after, RecordedUtc = clock.GetUtcNow() };
}
