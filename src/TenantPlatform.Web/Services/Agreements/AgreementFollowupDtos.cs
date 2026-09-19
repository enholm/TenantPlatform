using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public class AgreementDeadlineFilter
{
    public bool Mine { get; set; }
    public Guid? AssignedUserId { get; set; }
    public AgreementType? AgreementType { get; set; }
    public AgreementDeadlineKind? Kind { get; set; }
    public AgreementFollowupStatus? Status { get; set; }
    public AgreementDeadlineState? State { get; set; } = AgreementDeadlineState.Current;
    public bool IncludeCompleted { get; set; }
    public int HorizonDays { get; set; } = 90;
    public bool CustomDates { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
}
public record AgreementDeadlineRow(Guid Id, Guid AgreementId, string AgreementTitle, string Counterparty,
    AgreementType AgreementType, AgreementDeadlineKind Kind, DateOnly DueDate, int DaysLeft,
    Guid ResponsibleUserId, string ResponsibleName, bool ResponsibleValid, AgreementFollowupStatus Status,
    AgreementDeadlineState State, DateTimeOffset UpdatedUtc);
public record AgreementDeadlinePage(List<AgreementDeadlineRow> Items, int Total, int Page, int PageSize,
    List<AgreementOptionDto> Owners, int Overdue, int Next30, int MissingOwner);
public record AgreementHistoryDto(Guid Id, AgreementHistoryKind Kind, DateTimeOffset CreatedUtc,
    string? Actor, string? Assignee, string? Comment);
public record AgreementReminderDto(int DaysBefore, AgreementReminderStatus Status, string? Recipient,
    DateTimeOffset ScheduledUtc, int Attempts, DateTimeOffset? LastAttemptUtc, DateTimeOffset? SentUtc,
    string? TransportId, string? ResultKey);
public record AgreementDeadlineDetail(AgreementDeadlineRow Deadline, Guid? AssignedUserId,
    List<AgreementHistoryDto> History, List<AgreementReminderDto> Reminders);
public record AgreementFollowupDetails(Guid Revision, bool CanEdit, bool CanArchive, bool IsArchived,
    List<AgreementDeadlineDetail> Deadlines, List<AgreementOptionDto> Editors, AgreementReminderMode ReminderMode,
    int[] ReminderDays, bool AccountRemindersEnabled, int[] AccountDays, string TimeZoneId, TimeOnly SendAt,
    string TransportMode);
public class SaveAgreementReminderSettings
{
    public bool Enabled { get; set; }
    public int[] Days { get; set; } = [90, 30, 7];
    public string TimeZoneId { get; set; } = "Europe/Oslo";
    public TimeOnly SendAt { get; set; } = new(8, 0);
    public Guid Revision { get; set; }
}

public interface IAgreementFollowupService
{
    Task<AgreementDeadlinePage> ListDeadlinesAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default);
    Task<AgreementFollowupDetails> GetFollowupAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task FollowupAsync(Guid accountId, Guid deadlineId, Guid revision, AgreementFollowupAction action, Guid? assignedUserId, string? comment, CancellationToken ct = default);
    Task SetReminderPreferencesAsync(Guid accountId, Guid agreementId, Guid revision, AgreementReminderMode mode, int[] days, CancellationToken ct = default);
    Task<SaveAgreementReminderSettings> GetReminderSettingsAsync(Guid accountId, CancellationToken ct = default);
    Task SaveReminderSettingsAsync(Guid accountId, SaveAgreementReminderSettings request, CancellationToken ct = default);
    Task ArchiveAsync(Guid accountId, Guid agreementId, Guid revision, CancellationToken ct = default);
}

