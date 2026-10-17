using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

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
    public bool DeadlinesInitialized { get; init; }
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

public record AgreementAccessDto(Guid UserId, string Name, AgreementAccessLevel Level);
public record AgreementDocumentDto(Guid Id, string FileName, long Size, AgreementDocumentCategory Category,
    string? Description, DateTimeOffset UploadedUtc, string UploadedByName);
public record AgreementNoticeSnapshot
{
    public AgreementForm Form { get; init; }
    public AgreementNoticeMode NoticeMode { get; init; }
    public int? NoticeCount { get; init; }
    public AgreementNoticeUnit? NoticeUnit { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly PeriodStartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateOnly? RenewalDate { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public bool AutoRenew { get; init; }
    public int? RenewalMonths { get; init; }
    public string? Terms { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public int? RegisteredCount { get; init; }
    public AgreementNoticeUnit? RegisteredUnit { get; init; }
    public DateOnly? CessationDate { get; init; }
    public DateTimeOffset? RegisteredUtc { get; init; }
    public Guid? RegisteredByUserId { get; init; }

    public static AgreementNoticeSnapshot From(Agreement a) => new()
    {
        Form = a.Form, NoticeMode = a.NoticeMode, NoticeCount = a.NoticeCount, NoticeUnit = a.NoticeUnit,
        StartDate = a.StartDate, PeriodStartDate = a.CurrentPeriodStartDate, EndDate = a.EndDate,
        RenewalDate = a.RenewalDate, NoticeDeadline = a.NoticeDeadline, AutoRenew = a.AutoRenew,
        RenewalMonths = a.RenewalMonths, Terms = a.Terms, EffectiveDate = a.TerminationEffectiveDate,
        RegisteredCount = a.TerminationNoticeCount, RegisteredUnit = a.TerminationNoticeUnit,
        CessationDate = a.CessationDate, RegisteredUtc = a.TerminationRegisteredUtc,
        RegisteredByUserId = a.TerminationRegisteredByUserId
    };
}

public record AgreementNoticeHistoryDto(AgreementNoticeAction Action, DateTimeOffset CreatedUtc, string Actor,
    string? Comment, AgreementNoticeSnapshot Before, AgreementNoticeSnapshot After);
