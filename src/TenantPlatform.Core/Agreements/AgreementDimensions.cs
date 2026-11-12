namespace TenantPlatform.Core.Agreements;

// Agreements owns availability/required; global selection rules remain on Dimension.
public sealed class AgreementDimensionRule
{
    public Guid AccountId { get; set; }
    public Guid DimensionId { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRequired { get; set; }
    public Guid Revision { get; set; }
}

public sealed class AgreementDimensionAssignment
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid ValueId { get; set; }
}

// Append-only explicit classification changes, independent of notice/financial history.
public sealed class AgreementDimensionHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public string BeforeJson { get; set; } = "[]";
    public string AfterJson { get; set; } = "[]";
}
