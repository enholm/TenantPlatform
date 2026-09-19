namespace TenantPlatform.Core.Agreements;

public class Agreement
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AgreementType Type { get; set; }
    public AgreementDirection? Direction { get; set; }
    public string? Currency { get; set; }
    public Guid CounterpartyOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public AgreementStatus Status { get; set; } = AgreementStatus.Draft;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? NoticeDeadline { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public AgreementForm Form { get; set; }
    public AgreementNoticeMode NoticeMode { get; set; }
    public int? NoticeCount { get; set; }
    public AgreementNoticeUnit? NoticeUnit { get; set; }
    public DateOnly CurrentPeriodStartDate { get; set; }
    // One active registration per agreement, independent of its general notice rule and status.
    public DateOnly? TerminationEffectiveDate { get; set; }
    public int? TerminationNoticeCount { get; set; }
    public AgreementNoticeUnit? TerminationNoticeUnit { get; set; }
    public DateOnly? CessationDate { get; set; }
    public DateTimeOffset? TerminationRegisteredUtc { get; set; }
    public Guid? TerminationRegisteredByUserId { get; set; }
    public bool DeadlinesInitialized { get; set; }
    public bool IsArchived { get; set; }
    public AgreementReminderMode ReminderMode { get; set; }
    public int[] ReminderDays { get; set; } = [];
    public bool AutoRenew { get; set; }
    public int? RenewalMonths { get; set; }
    public string? Terms { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? UnitId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid Revision { get; set; }
}
