using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public class SaveAgreementRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AgreementDirection? Direction { get; set; }
    public string? Currency { get; set; }
    public Guid? IndexId { get; set; }
    public bool ResolveIndexSetup { get; set; }
    public AgreementType Type { get; set; } = AgreementType.Other;
    public Guid CounterpartyOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public AgreementStatus Status { get; set; } = AgreementStatus.Draft;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; set; }
    public DateOnly? NoticeDeadline { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public AgreementForm Form { get; set; }
    public AgreementNoticeMode NoticeMode { get; set; }
    public int? NoticeCount { get; set; }
    public AgreementNoticeUnit? NoticeUnit { get; set; }
    public bool BeginNewPeriod { get; set; }
    public DateOnly? NewPeriodStartDate { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalMonths { get; set; }
    public string? Terms { get; set; }
    public Guid? OrganizationElementId { get; set; }
    public Guid? GeographicAreaId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid Revision { get; set; }
}

public class AgreementDetailsDto : SaveAgreementRequest
{
    public bool IndexSetupNeedsReview { get; init; }
    public string? IndexName { get; init; }
    public DateOnly CurrentPeriodStartDate { get; init; }
    public AgreementNoticeSnapshot NoticeSnapshot { get; set; } = new();
    public string? TerminationRegisteredByName { get; set; }
    public List<AgreementNoticeHistoryDto> NoticeHistory { get; set; } = [];
    public bool IsArchived { get; init; }
    public Guid Id { get; init; }
    public string CounterpartyName { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
    public string? OrganizationElementName { get; init; }
    public string? GeographicAreaName { get; init; }
    public string? BuildingName { get; init; }
    public string? UnitName { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
    public string UpdatedByName { get; init; } = string.Empty;
    public bool CanEdit { get; set; }
    public bool CanManageAccess { get; set; }
    public List<AgreementDocumentDto> Documents { get; set; } = [];
    public List<AgreementAccessDto> Access { get; set; } = [];
}

public record AgreementListItemDto(Guid Id, string Title, string CounterpartyName, AgreementType Type,
    AgreementStatus Status, Guid OwnerUserId, string OwnerName, DateOnly? EndDate, DateOnly? NoticeDeadline, bool IsArchived = false);
public record AgreementOptionDto(Guid Id, string Name, Guid? BuildingId = null, bool IsActive = true);
public record AgreementPageDto(List<AgreementListItemDto> Items, int TotalCount, int Page, int PageSize, List<AgreementOptionDto> Owners);
public class AgreementFilter
{
    public string? Search { get; set; }
    public AgreementStatus? Status { get; set; }
    public AgreementType? Type { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int Page { get; set; } = 1;
}
public class AgreementOptionsDto
{
    public List<AgreementOptionDto> OrganizationElements { get; init; } = [];
    public List<AgreementOptionDto> GeographicAreas { get; init; } = [];
    public List<AgreementOptionDto> Indices { get; init; } = [];
    public List<AgreementOptionDto> Counterparties { get; init; } = [];
    public List<AgreementOptionDto> Members { get; init; } = [];
    public List<AgreementOptionDto> Buildings { get; init; } = [];
    public List<AgreementOptionDto> Units { get; init; } = [];
}
public record AgreementAccessDto(Guid UserId, string Name, AgreementAccessLevel Level);
public record AgreementDocumentDto(Guid Id, string FileName, long Size, AgreementDocumentCategory Category,
    string? Description, DateTimeOffset UploadedUtc, string UploadedByName);
public record AgreementDownload(Stream Content, string FileName, string MediaType);
public class AgreementValidationException(string resourceKey) : Exception(resourceKey);
