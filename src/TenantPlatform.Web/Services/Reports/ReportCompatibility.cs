using TenantPlatform.Core.Reports;
using System.Diagnostics.CodeAnalysis;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportFilterGrain { Agreements, LeasingFrameworks, LeasingAcquisitions }

public sealed record ReportMetricDefinition(
    ReportMetric Metric,
    string LabelResourceKey,
    ReportModule Module,
    ReportGrain Grain,
    ReportAggregation Aggregation,
    ReportDateBasis DateBasis,
    ReportAmountComponent AmountComponent,
    ReportCurrencyRule CurrencyRule,
    ReportAdditivity Additivity,
    ReportFilterGrain FilterGrain,
    bool SupportsTimeGrouping,
    ReportDimensionScope? DimensionScope,
    IReadOnlyList<ReportGroupingKey> FixedGroupings,
    IReadOnlyList<ReportVisualization> Visualizations,
    string SourceRule);

public sealed record ReportTreeDefinition(ReportModule Module, ReportDateBasis DateBasis, string SourceRule);

public sealed record ReportChoiceDefinition<T>(T Id, string LabelResourceKey) where T : struct, Enum;

public sealed record ReportCompatibilityCatalog(
    ReportModule Module,
    IReadOnlyList<ReportMetricDefinition> Metrics,
    ReportTreeDefinition Tree,
    IReadOnlyList<ReportChoiceDefinition<ReportDateBasis>> DateBases,
    IReadOnlyList<ReportChoiceDefinition<ReportAggregation>> Aggregations,
    IReadOnlyList<ReportChoiceDefinition<ReportVisualization>> Visualizations,
    IReadOnlyList<ReportChoiceDefinition<ReportGrain>> Grains);

public sealed record ReportValidationIssue(string Field, string ResourceKey);

public sealed class ReportDefinitionValidationException(IReadOnlyList<ReportValidationIssue> issues)
    : ReportPageValidationException(issues.FirstOrDefault()?.ResourceKey ?? "ReportDefinitionInvalid")
{
    public IReadOnlyList<ReportValidationIssue> Issues { get; } = issues;
}

public sealed record ReportDefinitionSemantics(
    ReportGrain Grain,
    ReportCurrencyRule CurrencyRule,
    ReportAdditivity Additivity,
    bool UniqueTotalRequired,
    bool GroupTotalsAdditive,
    bool MultipleGroupMembership,
    string SourceRule);

public sealed record ReportNormalizationChange(string Field, string ResourceKey);
public sealed record ReportNormalizationResult(ReportCardDefinition Definition,
    IReadOnlyList<ReportNormalizationChange> Changes);

public sealed record ReportResultMetadata(
    IReadOnlyCollection<string> Currencies,
    bool ValuesPartitionedByCurrency,
    bool HasNegativeValues,
    bool HasOverlappingGroups,
    bool HasValues = true);

public sealed record ReportResultValidationResult(IReadOnlyList<ReportValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class ReportCompatibilityMatrix
{
    private static readonly ReportVisualization[] CountVisualizations =
        [ReportVisualization.KeyFigure, ReportVisualization.Bar, ReportVisualization.Line,
            ReportVisualization.Donut, ReportVisualization.Table];
    private static readonly ReportVisualization[] MoneyVisualizations =
        [ReportVisualization.KeyFigure, ReportVisualization.Bar, ReportVisualization.Line, ReportVisualization.Table];
    private static readonly ReportVisualization[] SnapshotVisualizations =
        [ReportVisualization.KeyFigure, ReportVisualization.Bar, ReportVisualization.Table];
    private static readonly ReportGroupingKey[] AgreementGroupings =
    [
        new AgreementReportGroupingKey(AgreementReportGrouping.Type),
        new AgreementReportGroupingKey(AgreementReportGrouping.Counterparty),
        new AgreementReportGroupingKey(AgreementReportGrouping.Status),
        new AgreementReportGroupingKey(AgreementReportGrouping.Owner)
    ];
    private static readonly ReportGroupingKey[] LeasingGroupings =
    [
        new LeasingReportGroupingKey(LeasingReportGrouping.Kind),
        new LeasingReportGroupingKey(LeasingReportGrouping.Status),
        new LeasingReportGroupingKey(LeasingReportGrouping.Owner)
    ];

    private static readonly IReadOnlyList<ReportMetricDefinition> Definitions =
    [
        Metric(ReportMetric.AgreementCount, "ReportMetricAgreementCount", ReportModule.Agreements,
            ReportGrain.Agreement, ReportAggregation.CountDistinct, ReportDateBasis.AgreementPeriodOverlap,
            ReportAmountComponent.None, ReportCurrencyRule.None, ReportAdditivity.UniqueBeforeGroupExpansion,
            ReportFilterGrain.Agreements, true, ReportDimensionScope.Agreement, AgreementGroupings,
            CountVisualizations, "Agreement.StartDate–(CessationDate ?? EndDate), distinct Agreement.Id"),
        Metric(ReportMetric.AgreementIncomeAmount, "ReportMetricAgreementIncome", ReportModule.Agreements,
            ReportGrain.AgreementFinancialPeriod, ReportAggregation.Sum, ReportDateBasis.AgreementInvoiceDate,
            ReportAmountComponent.Net, ReportCurrencyRule.PartitionByCurrency, ReportAdditivity.UniqueBeforeGroupExpansion,
            ReportFilterGrain.Agreements, true, ReportDimensionScope.Agreement, AgreementGroupings,
            MoneyVisualizations, "AgreementPeriodCalculator.Calculate; Income; InvoiceDate; tax exclusive"),
        Metric(ReportMetric.AgreementCostAmount, "ReportMetricAgreementCost", ReportModule.Agreements,
            ReportGrain.AgreementFinancialPeriod, ReportAggregation.Sum, ReportDateBasis.AgreementInvoiceDate,
            ReportAmountComponent.Net, ReportCurrencyRule.PartitionByCurrency, ReportAdditivity.UniqueBeforeGroupExpansion,
            ReportFilterGrain.Agreements, true, ReportDimensionScope.Agreement, AgreementGroupings,
            MoneyVisualizations, "AgreementPeriodCalculator.Calculate; Cost; InvoiceDate; tax exclusive"),

        Metric(ReportMetric.LeasingFrameworkCount, "ReportMetricLeasingFrameworkCount", ReportModule.Leasing,
            ReportGrain.LeasingFramework, ReportAggregation.CountDistinct,
            ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap, ReportAmountComponent.None,
            ReportCurrencyRule.None, ReportAdditivity.UniqueBeforeGroupExpansion,
            ReportFilterGrain.LeasingFrameworks, true, ReportDimensionScope.LeasingAgreement, LeasingGroupings,
            CountVisualizations, "Distinct LeasingFramework.Id; AcquisitionFrom–AcquisitionTo overlap"),
        Metric(ReportMetric.LeasingAcquisitionCount, "ReportMetricLeasingAcquisitionCount", ReportModule.Leasing,
            ReportGrain.LeasingAcquisition, ReportAggregation.CountDistinct,
            ReportDateBasis.LeasingAcquisitionPurchaseDate, ReportAmountComponent.None,
            ReportCurrencyRule.None, ReportAdditivity.UniqueBeforeGroupExpansion,
            ReportFilterGrain.LeasingAcquisitions, true, ReportDimensionScope.LeasingAgreement, LeasingGroupings,
            CountVisualizations, "Distinct LeasingAcquisition.Id; PurchaseDate"),

        LeasingPurchase(ReportMetric.LeasingOriginalPurchaseNet, "ReportMetricLeasingOriginalNet",
            ReportAmountComponent.Net, "LeasingAcquisition.NetTotal"),
        LeasingPurchase(ReportMetric.LeasingOriginalPurchaseVat, "ReportMetricLeasingOriginalVat",
            ReportAmountComponent.Vat, "LeasingAcquisition.VatTotal"),
        LeasingPurchase(ReportMetric.LeasingOriginalPurchaseGross, "ReportMetricLeasingOriginalGross",
            ReportAmountComponent.Gross, "LeasingAcquisition.GrossTotal"),
        LeasingPurchase(ReportMetric.LeasingAdjustedPurchaseNet, "ReportMetricLeasingAdjustedNet",
            ReportAmountComponent.Net, "NetTotal - CreditNetTotal - ReversedNetTotal"),
        LeasingPurchase(ReportMetric.LeasingAdjustedPurchaseVat, "ReportMetricLeasingAdjustedVat",
            ReportAmountComponent.Vat, "VatTotal - CreditVatTotal - ReversedVatTotal"),
        LeasingPurchase(ReportMetric.LeasingAdjustedPurchaseGross, "ReportMetricLeasingAdjustedGross",
            ReportAmountComponent.Gross, "GrossTotal - credits - reversals (net + VAT)"),

        LeasingCapacity(ReportMetric.LeasingCapacityUsedNet, "ReportMetricLeasingCapacityNet",
            ReportAmountComponent.Net, "Capacity: registered acquisitions Sum(NetTotal - ReleasedNetTotal)"),
        LeasingCapacity(ReportMetric.LeasingCapacityUsedVat, "ReportMetricLeasingCapacityVat",
            ReportAmountComponent.Vat, "Capacity: registered acquisitions Sum(VatTotal - ReleasedVatTotal)"),
        LeasingCapacity(ReportMetric.LeasingCapacityUsedGross, "ReportMetricLeasingCapacityGross",
            ReportAmountComponent.Gross, "Capacity/LeasingCalculator.Used: net + VAT after release")
    ];

    public static ReportCompatibilityCatalog For(ReportModule module)
    {
        if (!Enum.IsDefined(module)) throw new ReportDefinitionValidationException(
            [new("module", "ReportDefinitionModuleInvalid")]);
        var metrics = Definitions.Where(x => x.Module == module).ToList();
        ReportTreeDefinition tree = module switch
        {
            ReportModule.Agreements => new(module, ReportDateBasis.AgreementPeriodOverlap,
                "Account → authorized agreements → financial lines"),
            ReportModule.Leasing => new(module, ReportDateBasis.LeasingAcquisitionPurchaseDate,
                "Account → authorized frameworks → acquisitions → items → equipment"),
            _ => throw new ReportDefinitionValidationException([new("module", "ReportDefinitionModuleInvalid")])
        };
        return new(module, metrics, tree,
            metrics.Select(x => x.DateBasis).Append(tree.DateBasis).Distinct()
                .Select(x => Choice(x, $"ReportDateBasis{x}")).ToList(),
            metrics.Select(x => x.Aggregation).Distinct()
                .Select(x => Choice(x, $"ReportAggregation{x}")).ToList(),
            metrics.SelectMany(x => x.Visualizations).Append(ReportVisualization.RelationshipTree).Distinct()
                .Select(x => Choice(x, $"ReportVisualization{x}")).ToList(),
            metrics.Select(x => x.Grain).Append(ReportGrain.Relationship).Distinct()
                .Select(x => Choice(x, $"ReportGrain{x}")).ToList());
    }

    public static bool AllowsGrouping(ReportMetricDefinition metric, ReportGroupingKey grouping) => grouping switch
    {
        ReportDimensionGroupingKey dimension => metric.DimensionScope == dimension.Scope,
        _ => metric.FixedGroupings.Contains(grouping)
    };

    public static bool AllowsFilter(ReportMetricDefinition? metric, ReportFilterSelection filter)
    {
        if (metric is null || metric.Module != ReportModule.Leasing ||
            filter is not ReportValueFilterSelection value) return true;
        if (value.Key is LeasingReportFilterKey { Field: LeasingCatalogFilter.Kind } &&
            value.Value is ReportEnumValue<LeasingCatalogKind> kind)
            return metric.FilterGrain == ReportFilterGrain.LeasingFrameworks
                ? kind.Value is LeasingCatalogKind.All or LeasingCatalogKind.Frameworks
                : kind.Value is not LeasingCatalogKind.Frameworks;
        if (value.Key is LeasingReportFilterKey { Field: LeasingCatalogFilter.Status } &&
            value.Value is ReportEnumValue<LeasingCatalogStatus> status)
            return metric.FilterGrain == ReportFilterGrain.LeasingFrameworks
                ? status.Value is LeasingCatalogStatus.Open or LeasingCatalogStatus.Closed or LeasingCatalogStatus.Finished
                : status.Value is LeasingCatalogStatus.Draft or LeasingCatalogStatus.Registered or LeasingCatalogStatus.Cancelled;
        return true;
    }

    private static ReportMetricDefinition Metric(ReportMetric metric, string label, ReportModule module,
        ReportGrain grain, ReportAggregation aggregation, ReportDateBasis dateBasis,
        ReportAmountComponent component, ReportCurrencyRule currency, ReportAdditivity additivity,
        ReportFilterGrain filterGrain, bool time, ReportDimensionScope? dimensionScope,
        IReadOnlyList<ReportGroupingKey> fixedGroupings,
        IReadOnlyList<ReportVisualization> visualizations, string source) =>
        new(metric, label, module, grain, aggregation, dateBasis, component, currency, additivity,
            filterGrain, time, dimensionScope, fixedGroupings, visualizations, source);

    private static ReportChoiceDefinition<T> Choice<T>(T id, string resourceKey) where T : struct, Enum =>
        new(id, resourceKey);

    private static ReportMetricDefinition LeasingPurchase(ReportMetric metric, string label,
        ReportAmountComponent component, string source) => Metric(metric, label, ReportModule.Leasing,
        ReportGrain.LeasingAcquisition, ReportAggregation.Sum, ReportDateBasis.LeasingAcquisitionPurchaseDate,
        component, ReportCurrencyRule.PartitionByCurrency, ReportAdditivity.UniqueBeforeGroupExpansion,
        ReportFilterGrain.LeasingAcquisitions, true, ReportDimensionScope.LeasingAgreement,
        LeasingGroupings, MoneyVisualizations, source);

    private static ReportMetricDefinition LeasingCapacity(ReportMetric metric, string label,
        ReportAmountComponent component, string source) => Metric(metric, label, ReportModule.Leasing,
        ReportGrain.LeasingFramework, ReportAggregation.Sum,
        ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap, component,
        ReportCurrencyRule.PartitionByCurrency, ReportAdditivity.CurrentSnapshot,
        ReportFilterGrain.LeasingFrameworks, false, ReportDimensionScope.LeasingAgreement,
        LeasingGroupings, SnapshotVisualizations, source);
}

public sealed class ReportDefinitionCompatibilityValidator : IReportDefinitionCompatibilityValidator
{
    public ReportDefinitionSemantics Validate(ReportCardDefinition definition, ReportModuleCatalog catalog)
    {
        var issues = new List<ReportValidationIssue>();
        if (definition is null || !Enum.IsDefined(definition.Module) || catalog.Module != definition.Module)
            Throw([new("module", "ReportDefinitionModuleInvalid")]);
        var matrix = catalog.Compatibility;
        try { definition.Period?.Validate(); }
        catch (ReportPeriodValidationException) { issues.Add(new("period", "ReportPeriodInvalid")); }
        if (definition.Period is null) issues.Add(new("period", "ReportPeriodInvalid"));
        try { catalog.Validate(definition.Filters ?? []); }
        catch (ReportCatalogValidationException ex) { issues.Add(new("filters", ex.Message)); }

        if (!definition.Visualization.HasValue || !Enum.IsDefined(definition.Visualization.Value))
            issues.Add(new("visualization", "ReportDefinitionVisualizationInvalid"));
        if (definition.Visualization == ReportVisualization.RelationshipTree)
        {
            if (definition.Metric.HasValue) issues.Add(new("metric", "ReportDefinitionTreeMetric"));
            if (definition.Aggregation.HasValue) issues.Add(new("aggregation", "ReportDefinitionTreeMetric"));
            if (definition.Grouping is not null || definition.TimeGrouping.HasValue)
                issues.Add(new("grouping", "ReportDefinitionTreeGrouping"));
            if (definition.DateBasis != matrix.Tree.DateBasis)
                issues.Add(new("dateBasis", "ReportDefinitionDateBasisInvalid"));
            if (issues.Count > 0) Throw(issues);
            return new(ReportGrain.Relationship, ReportCurrencyRule.None,
                ReportAdditivity.UniqueBeforeGroupExpansion, true, true, false, matrix.Tree.SourceRule);
        }

        var metric = definition.Metric.HasValue
            ? matrix.Metrics.SingleOrDefault(x => x.Metric == definition.Metric.Value) : null;
        if (metric is null) issues.Add(new("metric", "ReportDefinitionMetricInvalid"));
        if (metric is not null)
        {
            if (definition.Aggregation != metric.Aggregation)
                issues.Add(new("aggregation", "ReportDefinitionAggregationInvalid"));
            if (definition.DateBasis != metric.DateBasis)
                issues.Add(new("dateBasis", "ReportDefinitionDateBasisInvalid"));
            if (definition.Visualization.HasValue && !metric.Visualizations.Contains(definition.Visualization.Value))
                issues.Add(new("visualization", "ReportDefinitionVisualizationInvalid"));
            if ((definition.Filters ?? []).Any(x => !ReportCompatibilityMatrix.AllowsFilter(metric, x)))
                issues.Add(new("filters", "ReportDefinitionFilterGrainInvalid"));
        }

        var multiple = false;
        if (definition.Grouping is not null)
        {
            try { catalog.ValidateGrouping(definition.Grouping); }
            catch (ReportCatalogValidationException) { issues.Add(new("grouping", "ReportDefinitionGroupingInvalid")); }
            if (metric is not null && !ReportCompatibilityMatrix.AllowsGrouping(metric, definition.Grouping))
                issues.Add(new("grouping", "ReportDefinitionGroupingGrainInvalid"));
            if (definition.Grouping is ReportDimensionGroupingKey dimension)
                multiple = catalog.DimensionFilters.Dimensions
                    .SingleOrDefault(x => x.DimensionId == dimension.DimensionId)?.AllowMultiple == true;
        }
        if (definition.Grouping is not null && definition.TimeGrouping.HasValue)
            issues.Add(new("grouping", "ReportDefinitionGroupingExclusive"));
        if (definition.TimeGrouping.HasValue)
        {
            if (!Enum.IsDefined(definition.TimeGrouping.Value) || metric?.SupportsTimeGrouping != true)
                issues.Add(new("timeGrouping", "ReportDefinitionTimeGroupingInvalid"));
        }

        switch (definition.Visualization)
        {
            case ReportVisualization.KeyFigure when definition.Grouping is not null || definition.TimeGrouping.HasValue:
                issues.Add(new("visualization", "ReportDefinitionKeyFigureGrouping")); break;
            case ReportVisualization.Line when !definition.TimeGrouping.HasValue || definition.Grouping is not null:
                issues.Add(new("visualization", "ReportDefinitionLineTimeGrouping")); break;
            case ReportVisualization.Bar when definition.TimeGrouping.HasValue == (definition.Grouping is not null):
                issues.Add(new("visualization", "ReportDefinitionBarGrouping")); break;
            case ReportVisualization.Donut:
                if (definition.Grouping is null || definition.TimeGrouping.HasValue || metric?.CurrencyRule != ReportCurrencyRule.None || multiple)
                    issues.Add(new("visualization", "ReportDefinitionDonutUnsafe"));
                break;
        }
        if (issues.Count > 0) Throw(issues);
        return new(metric!.Grain, metric.CurrencyRule, metric.Additivity,
            multiple || metric.Additivity == ReportAdditivity.UniqueBeforeGroupExpansion,
            !multiple, multiple, metric.SourceRule);
    }

    [DoesNotReturn]
    private static void Throw(IReadOnlyList<ReportValidationIssue> issues) =>
        throw new ReportDefinitionValidationException(issues);
}

public static class ReportDefinitionNormalizer
{
    public static ReportNormalizationResult Normalize(ReportCardDefinition input, ReportModuleCatalog catalog)
    {
        var filters = new List<ReportFilterSelection>();
        var seen = new HashSet<ReportFilterKey>();
        foreach (var filter in input.Filters ?? [])
        {
            try
            {
                if (seen.Add(filter.Key)) { catalog.Validate([filter]); filters.Add(filter); }
            }
            catch (ReportCatalogValidationException) { }
        }
        var matrix = catalog.Compatibility;
        var metric = input.Module == catalog.Module && input.Metric.HasValue
            ? matrix.Metrics.SingleOrDefault(x => x.Metric == input.Metric.Value) : null;
        var output = input with { Module = catalog.Module, Filters = filters };
        if (output.Visualization == ReportVisualization.RelationshipTree)
        {
            output = output with { Metric = null, Aggregation = null, Grouping = null, TimeGrouping = null };
            if (output.DateBasis != matrix.Tree.DateBasis) output = output with { DateBasis = null };
        }
        else if (metric is null)
        {
            output = output with { Metric = null, Aggregation = null, DateBasis = null,
                Grouping = null, TimeGrouping = null, Visualization = null };
        }
        else
        {
            if (output.Aggregation != metric.Aggregation) output = output with { Aggregation = null };
            if (output.DateBasis != metric.DateBasis) output = output with { DateBasis = null };
            if (output.Grouping is not null && !ValidGrouping(output.Grouping, metric, catalog))
                output = output with { Grouping = null };
            if (output.TimeGrouping.HasValue && (!Enum.IsDefined(output.TimeGrouping.Value) || !metric.SupportsTimeGrouping))
                output = output with { TimeGrouping = null };
            if (output.Grouping is not null && output.TimeGrouping.HasValue)
                output = output.Visualization == ReportVisualization.Line
                    ? output with { Grouping = null }
                    : output with { TimeGrouping = null };
            if (output.Visualization.HasValue && !metric.Visualizations.Contains(output.Visualization.Value))
                output = output with { Visualization = null };
            if (output.Visualization == ReportVisualization.KeyFigure)
                output = output with { Grouping = null, TimeGrouping = null };
            if (output.Visualization == ReportVisualization.Line && !output.TimeGrouping.HasValue)
                output = output with { Visualization = null };
            if (output.Visualization == ReportVisualization.Bar &&
                output.TimeGrouping.HasValue == (output.Grouping is not null))
                output = output with { Visualization = null };
            if (output.Visualization == ReportVisualization.Donut &&
                (output.Grouping is null || output.TimeGrouping.HasValue || metric.CurrencyRule != ReportCurrencyRule.None ||
                 Multiple(output.Grouping, catalog)))
                output = output with { Visualization = null };
            filters = filters.Where(x => ReportCompatibilityMatrix.AllowsFilter(metric, x)).ToList();
            output = output with { Filters = filters };
        }
        return new(output, Changes(input, output));
    }

    private static bool ValidGrouping(ReportGroupingKey grouping, ReportMetricDefinition metric,
        ReportModuleCatalog catalog)
    {
        try { catalog.ValidateGrouping(grouping); }
        catch (ReportCatalogValidationException) { return false; }
        return ReportCompatibilityMatrix.AllowsGrouping(metric, grouping);
    }

    private static bool Multiple(ReportGroupingKey? grouping, ReportModuleCatalog catalog) =>
        grouping is ReportDimensionGroupingKey dimension && catalog.DimensionFilters.Dimensions
            .SingleOrDefault(x => x.DimensionId == dimension.DimensionId)?.AllowMultiple == true;

    private static IReadOnlyList<ReportNormalizationChange> Changes(ReportCardDefinition before,
        ReportCardDefinition after)
    {
        var result = new List<ReportNormalizationChange>();
        void Add(bool changed, string field) { if (changed) result.Add(new(field, "ReportDefinitionNormalized")); }
        Add(before.Module != after.Module, "module");
        Add(!before.Filters.SequenceEqual(after.Filters), "filters");
        Add(before.DateBasis != after.DateBasis, "dateBasis");
        Add(before.Metric != after.Metric, "metric");
        Add(before.Aggregation != after.Aggregation, "aggregation");
        Add(before.Grouping != after.Grouping, "grouping");
        Add(before.TimeGrouping != after.TimeGrouping, "timeGrouping");
        Add(before.Visualization != after.Visualization, "visualization");
        return result;
    }
}

public static class ReportResultValidator
{
    public static ReportResultValidationResult Validate(ReportCardDefinition definition,
        ReportMetricDefinition? metric, ReportResultMetadata metadata)
    {
        var issues = new List<ReportValidationIssue>();
        var currencies = metadata.Currencies.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant()).Distinct().ToList();
        if (metric?.CurrencyRule == ReportCurrencyRule.PartitionByCurrency)
        {
            if ((metadata.HasValues && currencies.Count == 0) ||
                metadata.Currencies.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length != 3))
                issues.Add(new("result.currency", "ReportResultCurrencyMissing"));
            if (currencies.Count > 1 && !metadata.ValuesPartitionedByCurrency)
                issues.Add(new("result.currency", "ReportResultCurrencyMixed"));
            if (metadata.HasValues && definition.Visualization == ReportVisualization.KeyFigure && currencies.Count != 1)
                issues.Add(new("visualization", "ReportResultKeyFigureCurrency"));
        }
        if (definition.Visualization == ReportVisualization.Donut &&
            (metadata.HasNegativeValues || metadata.HasOverlappingGroups))
            issues.Add(new("visualization", "ReportResultDonutUnsafe"));
        return new(issues);
    }
}
