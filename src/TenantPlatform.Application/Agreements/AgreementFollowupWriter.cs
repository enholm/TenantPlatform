using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public sealed class AgreementFollowupWriteState
{
    public Agreement? Agreement { get; init; }
    public List<AgreementDeadline> Deadlines { get; init; } = [];
    public List<AgreementReminder> Reminders { get; init; } = [];
    public IReadOnlySet<Guid> ResponsibleCandidates { get; init; } = new HashSet<Guid>();
    public AgreementReminderSettings? Settings { get; set; }
    public List<AgreementDeadlineHistory> History { get; } = [];
    public List<AgreementNoticeHistory> NoticeHistory { get; } = [];
}
public interface IAgreementFollowupWriteData
{
    Task<Guid?> ReadDeadlineAgreementAsync(Guid account, Guid deadline, CancellationToken ct);
    Task<AgreementReminderSettings?> ReadSettingsAsync(Guid account, CancellationToken ct);
    Task<IAgreementFollowupWriteTransaction> BeginAsync(Guid account, Guid? agreement, CancellationToken ct);
}
public interface IAgreementFollowupWriteTransaction : IAsyncDisposable
{
    AgreementFollowupWriteState State { get; }
    Task SynchronizeAsync(DateTimeOffset now, Guid user, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
public sealed class AgreementFollowupWriter(IAuthorizationService authorization, IAgreementFollowupWriteData data, TimeProvider clock)
{
    private async Task<Guid> Require(Guid account, Guid agreement, bool manage, CancellationToken ct)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        // The shared Update rule removes edit at archive. Preserve the existing GUI validation key.
        if (!scope.CanEdit)
        {
            if (scope.IsArchived && scope.CanEditBeforeArchive)
                throw new AgreementValidationException("FollowupArchived");
            throw new UnauthorizedAccessException();
        }
        if (manage && !scope.CanManageAccess) throw new UnauthorizedAccessException();
        return scope.ListScope.UserId;
    }
    private static void Revision(Agreement agreement, Guid revision)
    { if (agreement.Revision != revision) throw new AgreementValidationException("AgreementConcurrencyConflict"); }
    private static void Touch(Agreement a, Guid user, DateTimeOffset now)
    { a.Revision = Guid.NewGuid(); a.UpdatedUtc = now; a.UpdatedByUserId = user; }
    private static void Cancel(AgreementFollowupWriteState state, Guid? deadline, string reason)
    {
        foreach (var row in state.Reminders.Where(x => (!deadline.HasValue || x.DeadlineId == deadline) &&
            (x.Status == AgreementReminderStatus.Pending || x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null)))
        { row.Status = AgreementReminderStatus.Skipped; row.ResultKey = reason; row.NextAttemptUtc = null; }
    }
    public async Task FollowupAsync(Guid accountId, Guid deadlineId, Guid revision, AgreementFollowupAction action, Guid? assignedUserId, string? comment, CancellationToken ct = default)
    {
        var caller = await authorization.GetAccountActorScopeAsync(accountId, ct);
        var agreementId = await data.ReadDeadlineAgreementAsync(accountId, deadlineId, ct) ?? throw new UnauthorizedAccessException();
        var initial = await Require(accountId, agreementId, false, ct);
        if (initial != caller.UserId) throw new UnauthorizedAccessException();
        await using var tx = await data.BeginAsync(accountId, agreementId, ct);
        var user = await Require(accountId, agreementId, false, ct);
        if (user != initial) throw new UnauthorizedAccessException();
        var state = tx.State; var a = state.Agreement ?? throw new UnauthorizedAccessException();
        var d = state.Deadlines.SingleOrDefault(x => x.Id == deadlineId) ?? throw new UnauthorizedAccessException();
        Revision(a, revision);
        if (d.State != AgreementDeadlineState.Current) throw new AgreementValidationException("FollowupObsolete");
        if (!Enum.IsDefined(action) || comment?.Length > 2000) throw new AgreementValidationException("FollowupInvalidComment");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if ((action is AgreementFollowupAction.Complete or AgreementFollowupAction.Comment) && comment is null) throw new AgreementValidationException("FollowupCommentRequired");
        var kind = AgreementHistoryKind.Commented; var now = clock.GetUtcNow();
        switch (action)
        {
            case AgreementFollowupAction.Start:
                if (d.Status != AgreementFollowupStatus.Untreated) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.InReview; kind = AgreementHistoryKind.Started; break;
            case AgreementFollowupAction.Assign:
                if (assignedUserId.HasValue && !state.ResponsibleCandidates.Contains(assignedUserId.Value)) throw new AgreementValidationException("FollowupInvalidOwner");
                d.AssignedUserId = assignedUserId; kind = AgreementHistoryKind.Assigned; break;
            case AgreementFollowupAction.Complete:
                if (d.Status == AgreementFollowupStatus.Completed) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.Completed; d.CompletedUtc = now; d.CompletedByUserId = user; kind = AgreementHistoryKind.Completed;
                Cancel(state, deadlineId, "FollowupCompleted"); break;
            case AgreementFollowupAction.Reopen:
                if (d.Status != AgreementFollowupStatus.Completed) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.InReview; d.CompletedUtc = null; d.CompletedByUserId = null; kind = AgreementHistoryKind.Reopened; break;
        }
        d.UpdatedUtc = now; d.UpdatedByUserId = user;
        state.History.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, DeadlineId = deadlineId, Kind = kind, CreatedUtc = now, ActorUserId = user,
            AssignedUserId = d.AssignedUserId ?? a.OwnerUserId, Comment = comment });
        Touch(a, user, now); await tx.CommitAsync(ct);
    }
    public async Task ArchiveAsync(Guid accountId, Guid agreementId, Guid revision, CancellationToken ct = default)
    {
        var initial = await Require(accountId, agreementId, true, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, ct);
        var user = await Require(accountId, agreementId, true, ct); if (user != initial) throw new UnauthorizedAccessException();
        var a = tx.State.Agreement ?? throw new UnauthorizedAccessException(); Revision(a, revision);
        var now = clock.GetUtcNow(); a.IsArchived = true; Touch(a, user, now);
        await tx.SynchronizeAsync(now, user, ct); await tx.CommitAsync(ct);
    }
    public async Task SetReminderPreferencesAsync(Guid accountId, Guid agreementId, Guid revision, AgreementReminderMode mode, int[] days, CancellationToken ct = default)
    {
        var snapshot = days?.ToArray();
        if (!Enum.IsDefined(mode)) throw new AgreementValidationException("FollowupInvalidDays");
        var initial = await Require(accountId, agreementId, false, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, ct);
        var user = await Require(accountId, agreementId, false, ct); if (user != initial) throw new UnauthorizedAccessException();
        var a = tx.State.Agreement ?? throw new UnauthorizedAccessException(); Revision(a, revision);
        a.ReminderMode = mode; a.ReminderDays = mode == AgreementReminderMode.Custom ? AgreementReminderSchedule.ValidateDays(snapshot) : [];
        Touch(a, user, clock.GetUtcNow()); if (mode == AgreementReminderMode.Disabled) Cancel(tx.State, null, "FollowupDisabled");
        await tx.CommitAsync(ct);
    }
    private async Task<Guid> RequireSettings(Guid account, CancellationToken ct)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!actor.Assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Agreements.Create) && !x.OrganizationId.HasValue && !x.BuildingId.HasValue)) throw new UnauthorizedAccessException();
        return actor.UserId;
    }
    public async Task<SaveAgreementReminderSettings> GetReminderSettingsAsync(Guid accountId, CancellationToken ct = default)
    {
        await RequireSettings(accountId, ct);
        var settings = await data.ReadSettingsAsync(accountId, ct) ?? new AgreementReminderSettings { AccountId = accountId };
        return new() { Enabled = settings.Enabled, Days = settings.Days.ToArray(), TimeZoneId = settings.TimeZoneId, SendAt = settings.SendAt, Revision = settings.Revision };
    }
    public async Task SaveReminderSettingsAsync(Guid accountId, SaveAgreementReminderSettings request, CancellationToken ct = default)
    {
        var enabled = request.Enabled; var days = request.Days?.ToArray(); var zone = request.TimeZoneId; var sendAt = request.SendAt; var revision = request.Revision;
        var initial = await RequireSettings(accountId, ct);
        days = AgreementReminderSchedule.ValidateDays(days); zone = AgreementTimeZones.Validate(zone);
        await using var tx = await data.BeginAsync(accountId, null, ct);
        if (await RequireSettings(accountId, ct) != initial) throw new UnauthorizedAccessException();
        var settings = tx.State.Settings;
        if ((settings?.Revision ?? Guid.Empty) != revision) throw new AgreementValidationException("AgreementConcurrencyConflict");
        settings ??= new() { AccountId = accountId }; tx.State.Settings = settings;
        settings.Enabled = enabled; settings.Days = days; settings.TimeZoneId = zone; settings.SendAt = sendAt; settings.Revision = Guid.NewGuid();
        if (!enabled) Cancel(tx.State, null, "FollowupDisabled"); await tx.CommitAsync(ct);
    }
    public async Task RecordTerminationAsync(Guid accountId, Guid agreementId, AgreementTerminationRequest request, CancellationToken ct = default)
    {
        var revision = request.Revision; var action = request.Action; var date = request.EffectiveDate; var comment = request.Comment;
        var initial = await Require(accountId, agreementId, false, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, ct);
        var user = await Require(accountId, agreementId, false, ct); if (user != initial) throw new UnauthorizedAccessException();
        var a = tx.State.Agreement ?? throw new UnauthorizedAccessException(); Revision(a, revision);
        if (a.Form != AgreementForm.Ongoing || comment?.Length > 2000) throw new AgreementValidationException("NoticeInvalidRule");
        var before = AgreementNoticeSnapshot.From(a); var now = clock.GetUtcNow();
        switch (action)
        {
            case AgreementNoticeAction.Registered:
                if (a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeAlreadyRegistered");
                a.TerminationNoticeCount = a.NoticeCount; a.TerminationNoticeUnit = a.NoticeUnit; a.TerminationRegisteredUtc = now;
                a.TerminationRegisteredByUserId = user; a.TerminationEffectiveDate = date; break;
            case AgreementNoticeAction.Corrected:
                if (!a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeNotRegistered");
                if (string.IsNullOrWhiteSpace(comment)) throw new AgreementValidationException("NoticeReasonRequired"); a.TerminationEffectiveDate = date; break;
            case AgreementNoticeAction.Withdrawn:
                if (!a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeNotRegistered");
                a.TerminationEffectiveDate = null; a.TerminationNoticeCount = null; a.TerminationNoticeUnit = null;
                a.TerminationRegisteredUtc = null; a.TerminationRegisteredByUserId = null; break;
            default: throw new AgreementValidationException("NoticeInvalidRule");
        }
        if (a.TerminationEffectiveDate == default(DateOnly)) throw new AgreementValidationException("AgreementInvalidDates");
        AgreementNoticeRules.Recalculate(a);
        tx.State.NoticeHistory.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, Action = action, CreatedUtc = now,
            ActorUserId = user, Comment = comment?.Trim(), BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(AgreementNoticeSnapshot.From(a)) });
        Touch(a, user, now); await tx.SynchronizeAsync(now, user, ct); await tx.CommitAsync(ct);
    }
}
