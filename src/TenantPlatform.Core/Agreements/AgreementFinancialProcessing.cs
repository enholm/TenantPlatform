namespace TenantPlatform.Core.Agreements;

public enum AgreementIndexResolution { Month = 1, Quarter = 3, Year = 12 }
public enum AgreementAdjustmentKind { Fixed = 0, Index = 1, Percentage = 2, Limit = 3 }
public enum AgreementAdjustmentBasis { Original = 1, Latest = 2 }
public enum AgreementAdjustmentAddition { None = 0, PercentagePoints = 1, PriceMarkup = 2 }
public enum AgreementAdjustmentAnchor { ReferenceDate = 1, Anniversary = 2, AnnualDate = 3 }
public enum AgreementPriceTiming { NextPeriod = 1, Split = 2 }
public enum AgreementProposalStatus { Pending = 0, Approved = 1, Rejected = 2, Stale = 3 }
public enum AgreementBasisStatus { Draft = 0, Approved = 1, Cancelled = 2 }

public class AgreementIndex
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public AgreementIndexResolution Resolution { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
}
public class AgreementIndexValue
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid IndexId { get; set; }
    public DateOnly Period { get; set; }
    public int Revision { get; set; }
    public decimal Value { get; set; }
    public DateOnly? PublishedDate { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; } = "";
}
public class AgreementAdjustmentRule
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid LineId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; } = "";
    public AgreementAdjustmentKind Kind { get; set; }
    public Guid? IndexId { get; set; }
    public decimal SharePercent { get; set; } = 100;
    public AgreementAdjustmentAddition Addition { get; set; }
    public decimal AdditionPercent { get; set; }
    public decimal FixedPercent { get; set; }
    public decimal? FloorPercent { get; set; }
    public decimal? CeilingPercent { get; set; }
    public bool AllowDecrease { get; set; }
    public AgreementAdjustmentBasis Basis { get; set; } = AgreementAdjustmentBasis.Latest;
    public Guid BasePriceVersionId { get; set; }
    public DateOnly BaseIndexPeriod { get; set; }
    public DateOnly FirstAllowedDate { get; set; }
    public int IntervalMonths { get; set; } = 12;
    public AgreementAdjustmentAnchor Anchor { get; set; } = AgreementAdjustmentAnchor.ReferenceDate;
    public DateOnly AnchorDate { get; set; }
    public int ComparisonOffsetMonths { get; set; }
    public AgreementPriceTiming PriceTiming { get; set; } = AgreementPriceTiming.NextPeriod;
    public List<AgreementAdjustmentDocument> Documents { get; set; } = [];
}
public class AgreementAdjustmentDocument
{
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid RuleId { get; set; }
    public Guid DocumentId { get; set; }
}
public class AgreementAdjustmentProposal
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid LineId { get; set; }
    public Guid RuleId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal? ChosenPercent { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string Fingerprint { get; set; } = "";
    public string CalculationJson { get; set; } = "";
    public AgreementProposalStatus Status { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset? DecidedUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? Comment { get; set; }
}
public class AgreementBasis
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public AgreementDirection Direction { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public Guid? OriginalBasisId { get; set; }
    public AgreementBasisStatus Status { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset GeneratedUtc { get; set; }
    public Guid GeneratedByUserId { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? CancelledUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string Reason { get; set; } = "";
}
public class AgreementBasisSnapshot
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid BasisId { get; set; }
    public int Revision { get; set; }
    public string Fingerprint { get; set; } = "";
    public string DataJson { get; set; } = "";
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; } = "";
}
public class AgreementBasisEvent
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid BasisId { get; set; }
    public Guid LineId { get; set; }
    public string EventKey { get; set; } = "";
    public bool Active { get; set; } = true;
}
