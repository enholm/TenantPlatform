namespace TenantPlatform.Core.Agreements;

public enum AgreementDirection { Income = 1, Cost = 2 }
public enum AgreementLineStatus { Draft = 0, Active = 1, Ended = 2, Deactivated = 3 }
public enum AgreementFrequency { Once = 0, Monthly = 1, Quarterly = 3, HalfYearly = 6, Yearly = 12 }
public enum AgreementAnchor { Calendar = 1, Date = 2 }
public enum AgreementBillingTiming { Advance = 1, Arrears = 2 }

public class AgreementLine
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public DateTimeOffset? ActivatedUtc { get; set; }
}

public class AgreementLineVersion
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid LineId { get; set; }
    public int Sequence { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly FirstPayableDate { get; set; }
    public Guid? PayableSourceLineId { get; set; }
    public DateOnly? PayableSourceStartDate { get; set; }
    public int? PayableOffsetMonths { get; set; }
    public AgreementFrequency Frequency { get; set; }
    public AgreementAnchor Anchor { get; set; }
    public DateOnly AnchorDate { get; set; }
    public AgreementBillingTiming BillingTiming { get; set; }
    public AgreementLineStatus Status { get; set; }
    public bool IndexRegulated { get; set; }
    public List<AgreementLineDocument> Documents { get; set; } = [];
}

public class AgreementPriceVersion
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid LineId { get; set; }
    public int Sequence { get; set; }
    public bool Independent { get; set; }
    public Guid? AdjustmentId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    // Internal price anchor, preserved when only quantity changes. Not a configurable index rule.
    public DateOnly? IndexBaseDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
}

public class AgreementLineDocument
{
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid LineVersionId { get; set; }
    public Guid DocumentId { get; set; }
}
