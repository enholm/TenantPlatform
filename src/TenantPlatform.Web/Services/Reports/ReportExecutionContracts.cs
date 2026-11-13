using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportValueUnit { Count, Amount }

public sealed record ReportExecutionRequest(
    ReportCardDefinition Definition,
    int TablePage = 1,
    int TablePageSize = 50);

public sealed record ReportExecutionLabel(string? ResourceKey, string? Text)
{
    public static ReportExecutionLabel Resource(string key) => new(key, null);
    public static ReportExecutionLabel Dynamic(string text) => new(null, text);
}

public sealed record ReportExecutionValue(decimal Value, string? Currency = null);

public sealed record ReportGroupResult(
    string Key,
    ReportExecutionLabel Label,
    IReadOnlyList<ReportExecutionValue> Values,
    int UniqueEntityCount);

public sealed record ReportTimeResult(
    string Key,
    int Year,
    int Bucket,
    DateOnly FullFrom,
    DateOnly FullTo,
    DateOnly OverlapFrom,
    DateOnly OverlapTo,
    IReadOnlyList<ReportExecutionValue> Values,
    int UniqueEntityCount);

public interface IReportTableRow
{
    string Key { get; }
}

public sealed record ReportAgreementTableRow(
    string Key,
    Guid AgreementId,
    string AgreementTitle,
    string Counterparty,
    string Owner,
    AgreementType Type,
    AgreementStatus Status,
    DateOnly? EventDate = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? EventKey = null,
    string? LineName = null,
    decimal? Value = null,
    string? Currency = null) : IReportTableRow;

public sealed record ReportLeasingTableRow(
    string Key,
    Guid RecordId,
    bool IsFramework,
    Guid? FrameworkId,
    string Name,
    string Reference,
    string Owner,
    string StatusResourceKey,
    DateOnly EventDate,
    DateOnly? EventTo = null,
    decimal? Value = null,
    string? Currency = null) : IReportTableRow;

public sealed record ReportTableResult(
    int Page,
    int PageSize,
    int TotalRows,
    IReadOnlyList<IReportTableRow> Rows);

public sealed record ReportExecutionMetadata(
    ReportGrain Grain,
    ReportValueUnit Unit,
    ReportCurrencyRule CurrencyRule,
    ReportAdditivity Additivity,
    bool UniqueTotalBeforeGroupExpansion,
    bool GroupTotalsAdditive,
    bool MultipleGroupMembership,
    bool EmptyTimeBucketsFilled,
    int UniqueEntityCount,
    int GroupOccurrenceCount,
    string SourceRule,
    IReadOnlyList<string> RestrictionResourceKeys);

public sealed record ReportExecutionResult(
    ReportModule Module,
    ReportMetric Metric,
    ReportDateRange Period,
    DateOnly LocalToday,
    string TimeZoneId,
    IReadOnlyList<ReportExecutionValue> Totals,
    IReadOnlyList<ReportGroupResult> Groups,
    IReadOnlyList<ReportTimeResult> TimeSeries,
    ReportTableResult? Table,
    ReportExecutionMetadata Metadata);

public sealed class ReportExecutionException(string resourceKey) : Exception(resourceKey);

public interface IReportExecutionService
{
    Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request,
        CancellationToken ct = default);
}
