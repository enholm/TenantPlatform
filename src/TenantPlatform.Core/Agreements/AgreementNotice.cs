namespace TenantPlatform.Core.Agreements;

public enum AgreementForm { Legacy = 0, FixedTerm = 1, Renewing = 2, Ongoing = 3 }
public enum AgreementNoticeMode { None = 0, Manual = 1, BeforeRenewal = 2 }
public enum AgreementNoticeUnit { Days = 1, Months = 2 }
public enum AgreementNoticeAction { RuleChanged = 1, Registered = 2, Corrected = 3, Withdrawn = 4, PeriodStarted = 5 }

public static class AgreementNoticeCalculator
{
    public static DateOnly Calculate(DateOnly date, int count, AgreementNoticeUnit unit, bool before)
    {
        if (count <= 0 || !Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(count));
        return unit == AgreementNoticeUnit.Days
            ? date.AddDays(before ? -count : count)
            : date.AddMonths(before ? -count : count);
    }
}

// Append-only snapshots: later edits never rewrite the conditions used for a registered notice.
public class AgreementNoticeHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public AgreementNoticeAction Action { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Comment { get; set; }
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}
