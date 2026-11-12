namespace TenantPlatform.Web.Services.Agreements;

public sealed class AgreementDimensionPatch
{
    public Guid DimensionId { get; set; }
    // Null is invalid, [] explicitly clears this dimension; never infer a clear.
    public List<Guid>? ValueIds { get; set; }
}

public sealed record AgreementDimensionRuleDto(Guid DimensionId, string Code, string Name, string? Description,
    bool IsActive, bool AllowMultiple, bool LeafOnly, bool IsEnabled, bool IsRequired, Guid Revision);

public sealed record AgreementDimensionValueDto(Guid Id, Guid? ParentId, string Name, string Code, string Path,
    int Depth, bool IsActive, bool Selectable, bool IsAssigned)
{
    public bool IsHistorical => IsAssigned && !Selectable;
}

public sealed record AgreementDimensionOptionDto(Guid DimensionId, string Code, string Name, string? Description,
    bool IsActive, bool IsEnabled, bool IsRequired, bool AllowMultiple, bool LeafOnly, List<AgreementDimensionValueDto> Values);

public sealed record AgreementDimensionOptionsDto(Guid? AgreementId, Guid Revision, bool CanEdit, List<AgreementDimensionOptionDto> Dimensions);

// Immutable fallback labels/paths recorded at the time of the change; not UI translations.
public sealed record AgreementDimensionValueSnapshot(Guid ValueId, string Code, string Name, string Path);
public sealed record AgreementDimensionSetSnapshot(Guid DimensionId, string Code, string Name, List<AgreementDimensionValueSnapshot> Values);
public sealed record AgreementDimensionHistoryDto(Guid Id, DateTimeOffset RecordedUtc, Guid ActorUserId, string ActorName,
    List<AgreementDimensionSetSnapshot> Before, List<AgreementDimensionSetSnapshot> After);
