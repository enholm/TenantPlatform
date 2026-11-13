namespace TenantPlatform.Core.Reports;

public enum ReportModule { Agreements, Leasing }

public enum ReportPeriodRule
{
    CurrentMonth,
    PreviousMonth,
    NextMonth,
    CurrentQuarter,
    PreviousQuarter,
    NextQuarter,
    CurrentTertial,
    PreviousTertial,
    NextTertial,
    CurrentHalfYear,
    PreviousHalfYear,
    NextHalfYear,
    CurrentYear,
    PreviousYear,
    NextYear,
    YearToDate,
    Rolling12Months,
    Custom
}

public enum ReportTimeGrouping { Month, Quarter, Tertial, HalfYear, Year }

public enum ReportGroupingKind { None, Time, Catalog, Dimension }

public enum ReportVisualization { KeyFigure, Bar, Line, Donut, Table, RelationshipTree }

public enum ReportGrain
{
    Agreement,
    AgreementFinancialPeriod,
    LeasingFramework,
    LeasingAcquisition,
    Relationship
}

public enum ReportMetric
{
    AgreementCount,
    AgreementIncomeAmount,
    AgreementCostAmount,
    LeasingFrameworkCount,
    LeasingAcquisitionCount,
    LeasingOriginalPurchaseNet,
    LeasingOriginalPurchaseVat,
    LeasingOriginalPurchaseGross,
    LeasingAdjustedPurchaseNet,
    LeasingAdjustedPurchaseVat,
    LeasingAdjustedPurchaseGross,
    LeasingCapacityUsedNet,
    LeasingCapacityUsedVat,
    LeasingCapacityUsedGross
}

public enum ReportAggregation { CountDistinct, Sum }

public enum ReportDateBasis
{
    AgreementPeriodOverlap,
    AgreementInvoiceDate,
    LeasingFrameworkAcquisitionWindowOverlap,
    LeasingAcquisitionPurchaseDate
}

public enum ReportAmountComponent { None, Net, Vat, Gross }

public enum ReportCurrencyRule { None, PartitionByCurrency }

public enum ReportAdditivity { Additive, UniqueBeforeGroupExpansion, CurrentSnapshot }

public class ReportPage
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid Revision { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
}

public class ReportCard
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ReportPageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ReportModule Module { get; set; }
    public int DefinitionVersion { get; set; }
    public string FiltersJson { get; set; } = "[]";
    public ReportPeriodRule PeriodRule { get; set; }
    public DateOnly? CustomFrom { get; set; }
    public DateOnly? CustomTo { get; set; }
    public string? DateBasisId { get; set; }
    public string? MetricId { get; set; }
    public string? AggregationId { get; set; }
    public ReportGroupingKind GroupingKind { get; set; }
    public int? GroupingKey { get; set; }
    public int? GroupingScope { get; set; }
    public Guid? GroupingDimensionId { get; set; }
    public ReportTimeGrouping? TimeGrouping { get; set; }
    public ReportVisualization? Visualization { get; set; }
    public string DisplayJson { get; set; } = "{}";
    public int GridColumn { get; set; }
    public int GridRow { get; set; }
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public int SortOrder { get; set; }
}
