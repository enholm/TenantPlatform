using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class CompatibilityChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var agreementCatalog = await f.Catalog(f.Admin).GetAsync(f.Account, ReportModule.Agreements);
        var leasingCatalog = await f.Catalog(f.Admin).GetAsync(f.Account, ReportModule.Leasing);
        var validator = new ReportDefinitionCompatibilityValidator();

        Check.That(agreementCatalog.Compatibility.Metrics.Count == 3 &&
            leasingCatalog.Compatibility.Metrics.Count == 11 &&
            agreementCatalog.Compatibility.DateBases.All(x => x.LabelResourceKey.StartsWith("ReportDateBasis")) &&
            leasingCatalog.Compatibility.Visualizations.Any(x => x.Id == ReportVisualization.RelationshipTree),
            "catalog publishes the same typed compatibility matrix and localized identities used by validation");

        foreach (var metric in agreementCatalog.Compatibility.Metrics.Concat(leasingCatalog.Compatibility.Metrics))
        {
            var catalog = metric.Module == ReportModule.Agreements ? agreementCatalog : leasingCatalog;
            var definition = Valid(metric, ReportVisualization.Table);
            var semantics = validator.Validate(definition, catalog);
            Check.That(semantics.Grain == metric.Grain && semantics.SourceRule == metric.SourceRule,
                $"matrix accepts supported {metric.Metric} definition with its declared grain and source rule");
        }

        var agreementAmount = agreementCatalog.Compatibility.Metrics.Single(x =>
            x.Metric == ReportMetric.AgreementIncomeAmount);
        Check.That(agreementAmount.DateBasis == ReportDateBasis.AgreementInvoiceDate &&
            agreementAmount.SourceRule.Contains("AgreementPeriodCalculator") &&
            agreementAmount.AmountComponent == ReportAmountComponent.Net,
            "agreement financial metrics use calculated invoice date and the existing tax-exclusive period calculator");
        var deferredPeriodization = Valid(agreementAmount, ReportVisualization.Table) with
            { DateBasis = ReportDateBasis.AgreementPeriodOverlap };
        CheckIssue(validator, deferredPeriodization, agreementCatalog, "dateBasis", "ReportDefinitionDateBasisInvalid",
            "deferred agreement overlap periodization cannot be enabled by a direct request");

        var leasingMetrics = leasingCatalog.Compatibility.Metrics;
        Check.That(leasingMetrics.Count(x => x.Metric.ToString().StartsWith("LeasingOriginalPurchase")) == 3 &&
            leasingMetrics.Count(x => x.Metric.ToString().StartsWith("LeasingAdjustedPurchase")) == 3 &&
            leasingMetrics.Count(x => x.Metric.ToString().StartsWith("LeasingCapacityUsed")) == 3 &&
            leasingMetrics.Where(x => x.CurrencyRule == ReportCurrencyRule.PartitionByCurrency)
                .Select(x => x.AmountComponent).ToHashSet().IsSupersetOf(
                    [ReportAmountComponent.Net, ReportAmountComponent.Vat, ReportAmountComponent.Gross]),
            "leasing exposes separate original, adjusted and released-capacity metrics with net, VAT and gross identities");

        var capacity = leasingMetrics.Single(x => x.Metric == ReportMetric.LeasingCapacityUsedGross);
        Check.That(capacity.Additivity == ReportAdditivity.CurrentSnapshot && !capacity.SupportsTimeGrouping &&
            !capacity.Visualizations.Contains(ReportVisualization.Line) && capacity.SourceRule.Contains("LeasingCalculator.Used"),
            "capacity is a current snapshot using the existing post-release rule, never a purchase-date history");
        var capacitySeries = Valid(capacity, ReportVisualization.Table) with
            { TimeGrouping = ReportTimeGrouping.Month, Visualization = ReportVisualization.Line };
        CheckIssue(validator, capacitySeries, leasingCatalog, "timeGrouping", "ReportDefinitionTimeGroupingInvalid",
            "historical capacity time series is rejected");

        var acquisition = leasingMetrics.Single(x => x.Metric == ReportMetric.LeasingAdjustedPurchaseGross);
        var lineDimension = Valid(acquisition, ReportVisualization.Bar) with
        {
            Grouping = new ReportDimensionGroupingKey(ReportDimensionScope.LeasingLineAllocation, f.DimensionOne)
        };
        CheckIssue(validator, lineDimension, leasingCatalog, "grouping", "ReportDefinitionGroupingInvalid",
            "deferred leasing line and allocation dimensions are rejected");
        Check.That(leasingCatalog.Groupings.OfType<ReportGroupingDefinition>().All(x =>
                x.Key is not ReportDimensionGroupingKey key || key.Scope == ReportDimensionScope.LeasingAgreement) &&
            leasingCatalog.Compatibility.Metrics.All(x => x.DimensionScope == ReportDimensionScope.LeasingAgreement),
            "first-delivery leasing compatibility offers only agreement-level dimensions");

        var agreementCount = agreementCatalog.Compatibility.Metrics.Single(x => x.Metric == ReportMetric.AgreementCount);
        var multiGrouping = Valid(agreementCount, ReportVisualization.Bar) with
        {
            Grouping = new ReportDimensionGroupingKey(ReportDimensionScope.Agreement, f.DimensionOne)
        };
        var multiSemantics = validator.Validate(multiGrouping, agreementCatalog);
        Check.That(multiSemantics.MultipleGroupMembership && multiSemantics.UniqueTotalRequired &&
            !multiSemantics.GroupTotalsAdditive,
            "multi-value grouping exposes unique-total and non-additive occurrence metadata");
        CheckIssue(validator, multiGrouping with { Visualization = ReportVisualization.Donut }, agreementCatalog,
            "visualization", "ReportDefinitionDonutUnsafe",
            "donut rejects overlapping multi-value groups");

        var exclusiveDonut = Valid(agreementCount, ReportVisualization.Donut) with
            { Grouping = new AgreementReportGroupingKey(AgreementReportGrouping.Status) };
        Check.That(validator.Validate(exclusiveDonut, agreementCatalog).GroupTotalsAdditive,
            "donut permits an exclusive non-monetary part-of-whole grouping");
        var moneyDonut = Valid(agreementAmount, ReportVisualization.Donut) with
            { Grouping = new AgreementReportGroupingKey(AgreementReportGrouping.Status) };
        CheckIssue(validator, moneyDonut, agreementCatalog, "visualization", "ReportDefinitionVisualizationInvalid",
            "monetary donut is rejected before result rendering");

        var agreementLine = Valid(agreementAmount, ReportVisualization.Line) with
            { TimeGrouping = ReportTimeGrouping.Quarter };
        Check.That(validator.Validate(agreementLine, agreementCatalog).CurrencyRule == ReportCurrencyRule.PartitionByCurrency,
            "report period and quarterly time grouping remain independent for invoice-date amounts");
        CheckIssue(validator, agreementLine with { DateBasis = (ReportDateBasis)999 }, agreementCatalog,
            "dateBasis", "ReportDefinitionDateBasisInvalid", "unknown date-basis identity is rejected");
        CheckIssue(validator, agreementLine with { Metric = (ReportMetric)999 }, agreementCatalog,
            "metric", "ReportDefinitionMetricInvalid", "unknown metric identity is rejected");
        CheckIssue(validator, agreementLine with { Module = (ReportModule)999 }, agreementCatalog,
            "module", "ReportDefinitionModuleInvalid", "unknown module identity is rejected");
        CheckIssue(validator, agreementLine with { Visualization = (ReportVisualization)999 }, agreementCatalog,
            "visualization", "ReportDefinitionVisualizationInvalid", "unknown visualization identity is rejected");

        var tree = new ReportCardDefinition(ReportModule.Agreements, 1, [],
            new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
            Visualization: ReportVisualization.RelationshipTree);
        Check.That(validator.Validate(tree, agreementCatalog).Grain == ReportGrain.Relationship,
            "relationship tree needs neither aggregate metric nor time grouping");
        CheckIssue(validator, tree with { Metric = ReportMetric.AgreementCount }, agreementCatalog,
            "metric", "ReportDefinitionTreeMetric", "relationship tree rejects an aggregate metric");

        var moneyKpi = Valid(agreementAmount, ReportVisualization.KeyFigure);
        var metricDefinition = agreementAmount;
        Check.That(ReportResultValidator.Validate(moneyKpi, metricDefinition,
            new(["NOK"], false, false, false)).IsValid,
            "one-currency monetary KPI passes result validation");
        var multiCurrencyKpi = ReportResultValidator.Validate(moneyKpi, metricDefinition,
            new(["NOK", "EUR"], true, false, false));
        Check.That(!multiCurrencyKpi.IsValid && multiCurrencyKpi.Issues.Any(x =>
            x.ResourceKey == "ReportResultKeyFigureCurrency"),
            "one KPI cannot combine multiple currencies even when series metadata is partitioned");
        var moneyTable = Valid(agreementAmount, ReportVisualization.Table);
        Check.That(ReportResultValidator.Validate(moneyTable, metricDefinition,
            new(["NOK", "EUR"], true, false, false)).IsValid,
            "multi-currency monetary table is valid only when values are partitioned by currency");
        Check.That(!ReportResultValidator.Validate(moneyTable, metricDefinition,
            new(["NOK", "EUR"], false, false, false)).IsValid,
            "unpartitioned mixed-currency result is rejected");
        Check.That(!ReportResultValidator.Validate(exclusiveDonut, agreementCount,
            new([], false, true, true)).IsValid,
            "runtime donut validation rejects negative or overlapping synthetic results");

        var invalidCapacity = Valid(capacity, ReportVisualization.Line) with
        {
            TimeGrouping = ReportTimeGrouping.Month,
            Grouping = new LeasingReportGroupingKey(LeasingReportGrouping.Owner)
        };
        var normalized = ReportDefinitionNormalizer.Normalize(invalidCapacity, leasingCatalog);
        var normalizedAgain = ReportDefinitionNormalizer.Normalize(normalized.Definition, leasingCatalog);
        Check.That(normalized.Definition.TimeGrouping is null && normalized.Definition.Grouping is not null &&
            normalized.Definition.Visualization is null && normalized.Changes.Count > 0 &&
            Equivalent(normalizedAgain.Definition, normalized.Definition) && normalizedAgain.Changes.Count == 0,
            "normalization is deterministic and idempotent without choosing currency or economic semantics");

        var moduleSwitch = Valid(acquisition, ReportVisualization.Table) with
        {
            Module = ReportModule.Agreements,
            Filters =
            [
                new ReportValueFilterSelection(new LeasingReportFilterKey(LeasingCatalogFilter.Kind),
                    new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.Acquisitions))
            ]
        };
        var switched = ReportDefinitionNormalizer.Normalize(moduleSwitch, agreementCatalog);
        Check.That(switched.Definition.Module == ReportModule.Agreements && switched.Definition.Metric is null &&
            switched.Definition.Filters.Count == 0 && switched.Definition.DateBasis is null,
            "module normalization clears incompatible identities and filters without silently selecting replacements");
    }

    private static ReportCardDefinition Valid(ReportMetricDefinition metric, ReportVisualization visualization) => new(
        metric.Module, 1, [], new(ReportPeriodRule.CurrentYear), metric.DateBasis, metric.Metric,
        metric.Aggregation, Visualization: visualization);

    private static bool Equivalent(ReportCardDefinition left, ReportCardDefinition right) =>
        left.Module == right.Module && left.DefinitionVersion == right.DefinitionVersion &&
        left.Filters.SequenceEqual(right.Filters) && left.Period == right.Period &&
        left.DateBasis == right.DateBasis && left.Metric == right.Metric &&
        left.Aggregation == right.Aggregation && left.Grouping == right.Grouping &&
        left.TimeGrouping == right.TimeGrouping && left.Visualization == right.Visualization &&
        left.DisplayJson == right.DisplayJson;

    private static void CheckIssue(ReportDefinitionCompatibilityValidator validator,
        ReportCardDefinition definition, ReportModuleCatalog catalog, string field, string key, string message)
    {
        try { validator.Validate(definition, catalog); }
        catch (ReportDefinitionValidationException ex)
        {
            Check.That(ex.Issues.Any(x => x.Field == field && x.ResourceKey == key), message);
            return;
        }
        throw new Exception("FAIL: " + message);
    }
}
