using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public sealed record ReportCardDefinition(
    ReportModule Module,
    int DefinitionVersion,
    IReadOnlyList<ReportFilterSelection> Filters,
    ReportPeriodSelection Period,
    ReportDateBasis? DateBasis = null,
    ReportMetric? Metric = null,
    ReportAggregation? Aggregation = null,
    ReportGroupingKey? Grouping = null,
    ReportTimeGrouping? TimeGrouping = null,
    ReportVisualization? Visualization = null,
    string DisplayJson = "{}")
{
    public ReportTitlePlacement TitlePlacement => ReportDisplaySettingsJson.Deserialize(DisplayJson).TitlePlacement;
    public string? Subtitle => ReportDisplaySettingsJson.Deserialize(DisplayJson).Subtitle;
}

public sealed record ReportCardEdit(
    Guid? Id,
    string Name,
    ReportCardDefinition Definition,
    int GridColumn,
    int GridRow,
    int GridWidth,
    int GridHeight,
    int SortOrder);

public sealed record ReportCardDto(
    Guid Id,
    string Name,
    ReportCardDefinition Definition,
    int GridColumn,
    int GridRow,
    int GridWidth,
    int GridHeight,
    int SortOrder);

public sealed record ReportPageSummaryDto(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    Guid Revision,
    int CardCount,
    DateTimeOffset UpdatedUtc);

public sealed record ReportPageDto(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    Guid Revision,
    DateTimeOffset CreatedUtc,
    Guid CreatedByUserId,
    DateTimeOffset UpdatedUtc,
    Guid UpdatedByUserId,
    IReadOnlyList<ReportCardDto> Cards);

public sealed record CreateReportPageRequest(string Name);

// Each page contains its complete desired card list. Existing cards omitted from every
// affected page are deleted; an existing card listed on another affected page is moved;
// a null card Id creates a new server-assigned card.
public sealed record ReportWorkspacePageEdit(
    Guid PageId,
    Guid ExpectedRevision,
    string Name,
    IReadOnlyList<ReportCardEdit> Cards);

public sealed record SaveReportWorkspaceRequest(IReadOnlyList<ReportWorkspacePageEdit> Pages);

public interface IReportPageService
{
    Task<IReadOnlyList<ReportPageSummaryDto>> ListAsync(Guid accountId, CancellationToken ct = default);
    Task<ReportPageDto> GetAsync(Guid accountId, Guid pageId, CancellationToken ct = default);
    Task<ReportPageDto> CreateAsync(Guid accountId, CreateReportPageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ReportPageDto>> SaveWorkspaceAsync(Guid accountId, SaveReportWorkspaceRequest request,
        CancellationToken ct = default);
    Task<ReportPageDto> DuplicateAsync(Guid accountId, Guid pageId, Guid expectedRevision,
        CancellationToken ct = default);
    Task DeleteAsync(Guid accountId, Guid pageId, Guid expectedRevision, CancellationToken ct = default);
}

public class ReportPageValidationException(string resourceKey) : Exception(resourceKey);

public sealed class ReportPageConflictException() : ReportPageValidationException("ReportPageConcurrency");

public interface IReportDefinitionCompatibilityValidator
{
    ReportDefinitionSemantics Validate(ReportCardDefinition definition, ReportModuleCatalog catalog);
}
