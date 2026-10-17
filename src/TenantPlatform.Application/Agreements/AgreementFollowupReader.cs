using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public record AgreementDeadlineRow(Guid Id, Guid AgreementId, string AgreementTitle, string Counterparty,
    AgreementType AgreementType, AgreementDeadlineKind Kind, DateOnly DueDate, int DaysLeft,
    Guid ResponsibleUserId, string ResponsibleName, bool ResponsibleValid, AgreementFollowupStatus Status,
    AgreementDeadlineState State, DateTimeOffset UpdatedUtc);
public record AgreementHistoryDto(Guid Id, AgreementHistoryKind Kind, DateTimeOffset CreatedUtc,
    string? Actor, string? Assignee, string? Comment);
public record AgreementReminderDto(int DaysBefore, AgreementReminderStatus Status, string? Recipient,
    DateTimeOffset ScheduledUtc, int Attempts, DateTimeOffset? LastAttemptUtc, DateTimeOffset? SentUtc,
    string? ResultKey);
public record AgreementDeadlineDetail(AgreementDeadlineRow Deadline, Guid? AssignedUserId,
    List<AgreementHistoryDto> History, List<AgreementReminderDto> Reminders);
public record AgreementFollowupDetails(Guid Revision, bool CanEdit, bool CanArchive, bool IsArchived,
    List<AgreementDeadlineDetail> Deadlines, List<AgreementOptionDto> Editors, AgreementReminderMode ReminderMode,
    int[] ReminderDays, bool AccountRemindersEnabled, int[] AccountDays, string TimeZoneId, TimeOnly SendAt,
    bool DeadlinesInitialized);
public interface IAgreementFollowupData
{
    Task<AgreementFollowupDetails> ReadAsync(AgreementReadScope scope, DateTimeOffset referenceUtc, CancellationToken ct);
}

public interface IAgreementFollowupReader
{
    Task<AgreementFollowupDetails> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
}

public sealed class AgreementFollowupReader(IAuthorizationService authorization, IAgreementFollowupData data,
    TimeProvider clock) : IAgreementFollowupReader
{
    public async Task<AgreementFollowupDetails> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var scope = await authorization.GetAgreementReadScopeAsync(accountId, agreementId, ct);
        return await data.ReadAsync(scope, now, ct);
    }
}

public static class AgreementFollowupRules
{
    // Delivery summaries are operational metadata, not part of ordinary Read access.
    public static bool CanInspectDelivery(AgreementReadScope scope) => scope.CanEdit || scope.CanManageAccess;

    public static string? PublicResultKey(string? key) => key is "FollowupDeliveryUnknown" or "FollowupPastDeadline" or
        "FollowupOlderThreshold" or "FollowupDisabled" or "FollowupObsolete" or "FollowupCompleted" or
        "FollowupMissingOwner" or "FollowupTransportNotConfigured" or "FollowupTransportFailed" ? key : null;
}
