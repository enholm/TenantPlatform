namespace TenantPlatform.Core.Agreements;

public enum AgreementDeadlineKind { Notice = 1, Expiry = 2, Renewal = 3 }
public enum AgreementFollowupStatus { Untreated = 1, InReview = 2, Completed = 3 }
public enum AgreementDeadlineState { Current = 1, Replaced = 2, Withdrawn = 3 }
public enum AgreementReminderMode { Inherit = 0, Custom = 1, Disabled = 2 }
public enum AgreementReminderStatus { Pending = 1, Processing = 2, Sent = 3, Failed = 4, Skipped = 5 }
public enum AgreementFollowupAction { Start = 1, Assign = 2, Comment = 3, Complete = 4, Reopen = 5 }
public enum AgreementHistoryKind { Created = 1, Replaced = 2, Withdrawn = 3, Started = 4, Assigned = 5, Commented = 6, Completed = 7, Reopened = 8, Archived = 9 }

public class AgreementDeadline
{
    // Id is the immutable occurrence identifier, including when a date is reused later.
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public AgreementDeadlineKind Kind { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly PeriodStartDate { get; set; }
    public Guid? AssignedUserId { get; set; }
    public AgreementFollowupStatus Status { get; set; } = AgreementFollowupStatus.Untreated;
    public AgreementDeadlineState State { get; set; } = AgreementDeadlineState.Current;
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
}

public class AgreementDeadlineHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid DeadlineId { get; set; }
    public AgreementHistoryKind Kind { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? Comment { get; set; }
}

public class AgreementReminderSettings
{
    public Guid AccountId { get; set; }
    public bool Enabled { get; set; }
    public int[] Days { get; set; } = [90, 30, 7];
    public string TimeZoneId { get; set; } = "Europe/Oslo";
    public TimeOnly SendAt { get; set; } = new(8, 0);
    public Guid Revision { get; set; }
}

public class AgreementReminder
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid DeadlineId { get; set; }
    public int DaysBefore { get; set; }
    public Guid? RecipientUserId { get; set; }
    public string? RecipientAddress { get; set; }
    public DateTimeOffset ScheduledUtc { get; set; }
    public AgreementReminderStatus Status { get; set; } = AgreementReminderStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? NextAttemptUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public Guid? ReservationId { get; set; }
    public DateTimeOffset? ReservedUntilUtc { get; set; }
    public string? TransportId { get; set; }
    // A resource key, never raw transport exception text.
    public string? ResultKey { get; set; }
}
