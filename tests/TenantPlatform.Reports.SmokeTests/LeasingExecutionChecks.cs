using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class LeasingExecutionChecks
{
    public static async Task RunAsync(Fixture f)
    {
        Check.That(LeasingCalculator.Line(new LeasingItem
            {
                Quantity = 1, UnitPrice = 0.025m, VatPercent = 25
            }) == new LeasingTotals(.03m, .01m),
            "Leasing fixture reconciliation retains the existing per-line midpoint-away-from-zero net/VAT rounding");
        var admin = f.Execution(f.Admin);
        var year = Period(new(2026, 1, 1), new(2026, 12, 31));
        var frameworkCount = Definition(ReportMetric.LeasingFrameworkCount,
            ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap, ReportAggregation.CountDistinct,
            ReportVisualization.Table, year);
        var acquisitionCount = Definition(ReportMetric.LeasingAcquisitionCount,
            ReportDateBasis.LeasingAcquisitionPurchaseDate, ReportAggregation.CountDistinct,
            ReportVisualization.Table, year);

        var frameworks = await admin.ExecuteAsync(f.Account, new(frameworkCount, 1, 1));
        var frameworkPage2 = await admin.ExecuteAsync(f.Account, new(frameworkCount, 2, 1));
        Check.That(frameworks.Totals.Single().Value == 2 &&
            frameworks.Table is { TotalRows: 2 } && frameworks.Table.Rows.Count == 1 &&
            frameworkPage2.Totals.Single().Value == 2 && frameworkPage2.Table!.Rows.Count == 1 &&
            frameworks.Table.Rows.Single().Key != frameworkPage2.Table.Rows.Single().Key,
            "Leasing framework count uses distinct authorized framework ids and deterministic paging without changing totals");

        var acquisitions = await admin.ExecuteAsync(f.Account, new(acquisitionCount, 1, 2));
        var acquisitionRows = acquisitions.Table!.Rows.OfType<ReportLeasingTableRow>().ToList();
        Check.That(acquisitions.Totals.Single().Value == 3 && acquisitions.Metadata.UniqueEntityCount == 3 &&
            acquisitions.Table.TotalRows == 3 && acquisitionRows.Count == 2 &&
            acquisitionRows.All(x => !x.IsFramework),
            "Leasing acquisition count keeps framework children and standalone acquisitions at acquisition grain");

        var ownerFrameworks = await f.Execution(f.OwnerA).ExecuteAsync(f.Account, new(frameworkCount));
        var ownerAcquisitions = await f.Execution(f.OwnerA).ExecuteAsync(f.Account, new(acquisitionCount));
        Check.That(ownerFrameworks.Totals.Single().Value == 1 &&
            ownerAcquisitions.Totals.Single().Value == 2,
            "direct Leasing execution reapplies current framework ownership and child/standalone acquisition access");
        var limitReader = await f.Execution(f.LimitReader).ExecuteAsync(f.Account, new(frameworkCount));
        Check.That(limitReader.Totals.Single().Value == 2,
            "the shared Leasing scope preserves limit-reader expansion for frameworks");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Execution(f.NoModuleAccess)
            .ExecuteAsync(f.Account, new(acquisitionCount)),
            "Leasing execution rejects an active member without Leasing access");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Execution(f.OwnerA, f.ForeignAccount)
            .ExecuteAsync(f.Account, new(acquisitionCount)),
            "Leasing execution rejects a forged account outside the selected context");

        var standalone = acquisitionCount with
        {
            Filters =
            [
                Value(LeasingCatalogFilter.Kind,
                    new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.StandaloneAcquisitions))
            ]
        };
        var standaloneResult = await admin.ExecuteAsync(f.Account, new(standalone));
        Check.That(standaloneResult.Totals.Single().Value == 2 &&
            standaloneResult.Table!.Rows.OfType<ReportLeasingTableRow>().All(x => x.FrameworkId is null),
            "Leasing kind orders remains the stable standalone-acquisition filter and never means LeasingOrder");
        var registeredStandalone = standalone with
        {
            Filters =
            [
                Value(LeasingCatalogFilter.Kind,
                    new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.StandaloneAcquisitions)),
                Value(LeasingCatalogFilter.Status,
                    new ReportEnumValue<LeasingCatalogStatus>(LeasingCatalogStatus.Registered))
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(registeredStandalone))).Totals.Single().Value == 1,
            "Leasing kind and acquisition status combine with the existing list semantics");
        var closedFramework = frameworkCount with
        {
            Filters =
            [
                Value(LeasingCatalogFilter.Kind,
                    new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.Frameworks)),
                Value(LeasingCatalogFilter.Status,
                    new ReportEnumValue<LeasingCatalogStatus>(LeasingCatalogStatus.Closed))
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(closedFramework))).Totals.Single().Value == 1,
            "framework kind and framework status use their distinct Leasing status grain");
        var searched = acquisitionCount with
        {
            Filters =
            [
                Value(LeasingCatalogFilter.Search, new ReportTextValue("standalone-ref")),
                Value(LeasingCatalogFilter.Owner, new ReportEntityValue(f.OwnerA))
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(searched))).Totals.Single().Value == 1,
            "Leasing search and owner filters retain list parity");

        var inherited = acquisitionCount with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.LeasingAgreement, f.DimensionTwo),
                    f.DepartmentA, false)
            ]
        };
        var inheritedResult = await admin.ExecuteAsync(f.Account, new(inherited));
        Check.That(inheritedResult.Totals.Single().Value == 1 &&
            inheritedResult.Table!.Rows.OfType<ReportLeasingTableRow>().Single().RecordId == f.AcquisitionChild,
            "acquisition execution filters only the effective inherited snapshot stored on the acquisition");
        var unspecified = acquisitionCount with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.LeasingAgreement, f.DimensionOne),
                    null, true)
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(unspecified))).Totals.Single().Value == 1,
            "Leasing execution preserves not-specified dimension semantics");
        var parentAnd = acquisitionCount with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.LeasingAgreement, f.DimensionOne),
                    f.Root, false),
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.LeasingAgreement, f.DimensionTwo),
                    f.DepartmentA, false)
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(parentAnd))).Totals.Single().Value == 1,
            "Leasing execution keeps parent-with-descendants matching and AND between dimensions without duplicate rows");
        var historicalFramework = frameworkCount with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.LeasingAgreement, f.HistoricalDimension),
                    f.HistoricalValue, false)
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(historicalFramework))).Totals.Single().Value == 1,
            "historical Leasing agreement-dimension choices remain executable");

        var dimensionGroups = await admin.ExecuteAsync(f.Account, new(acquisitionCount with
        {
            Grouping = new ReportDimensionGroupingKey(ReportDimensionScope.LeasingAgreement, f.DimensionOne)
        }));
        Check.That(dimensionGroups.Totals.Single().Value == 3 &&
            dimensionGroups.Groups.Sum(x => x.UniqueEntityCount) == 4 &&
            dimensionGroups.Metadata.MultipleGroupMembership &&
            dimensionGroups.Metadata.UniqueTotalBeforeGroupExpansion &&
            !dimensionGroups.Metadata.GroupTotalsAdditive,
            "multi-value Leasing dimensions expand groups while the acquisition total stays unique");

        var kindGroups = await admin.ExecuteAsync(f.Account, new(acquisitionCount with
        {
            Grouping = new LeasingReportGroupingKey(LeasingReportGrouping.Kind)
        }));
        Check.That(kindGroups.Groups.Single(x => x.Key == "kind:acquisition").Values.Single().Value == 1 &&
            kindGroups.Groups.Single(x => x.Key == "kind:standalone-acquisition").Values.Single().Value == 2,
            "Leasing kind grouping distinguishes framework children from standalone acquisitions without parent duplication");

        await VerifyPurchaseMetricsAsync(admin, f, year);
        await VerifyCapacityMetricsAsync(admin, f, year);

        var monthly = await admin.ExecuteAsync(f.Account, new(acquisitionCount with
        {
            Visualization = ReportVisualization.Line,
            TimeGrouping = ReportTimeGrouping.Month
        }));
        Check.That(monthly.TimeSeries.Count == 12 &&
            monthly.TimeSeries[0].Values.Single().Value == 3 &&
            monthly.TimeSeries.Skip(1).All(x => x.Values.Single().Value == 0),
            "purchase-date acquisition count uses chronological calendar buckets and fills empty buckets");

        var empty = await admin.ExecuteAsync(f.Account, new(acquisitionCount with
        {
            Period = Period(new(2030, 1, 1), new(2030, 12, 31)),
            Visualization = ReportVisualization.Line,
            TimeGrouping = ReportTimeGrouping.Month
        }));
        Check.That(empty.Totals.Single().Value == 0 && empty.TimeSeries.Count == 12 &&
            empty.TimeSeries.All(x => x.Values.Single().Value == 0),
            "empty Leasing selections retain defined count buckets without invented records");

        var capacityDefinition = Definition(ReportMetric.LeasingCapacityUsedGross,
            ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap, ReportAggregation.Sum,
            ReportVisualization.Table, year);
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => admin.ExecuteAsync(f.Account,
            new(capacityDefinition with
            {
                Visualization = ReportVisualization.Line,
                TimeGrouping = ReportTimeGrouping.Month
            })), "capacity history and time grouping stay rejected by the compatibility matrix");
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => admin.ExecuteAsync(f.Account,
            new(Definition(ReportMetric.LeasingOriginalPurchaseGross,
                ReportDateBasis.LeasingAcquisitionPurchaseDate, ReportAggregation.Sum,
                ReportVisualization.KeyFigure, year))),
            "a Leasing monetary KPI rejects a mixed-currency result instead of summing currencies");

        await SeedScopeLimitAsync(f);
        var tooBroad = acquisitionCount with
        {
            Visualization = ReportVisualization.Bar,
            Filters =
            [
                Value(LeasingCatalogFilter.Search, new ReportTextValue("scope-limit-"))
            ],
            Grouping = new ReportDimensionGroupingKey(ReportDimensionScope.LeasingAgreement, f.DimensionOne)
        };
        await Check.ThrowsAsync<ReportExecutionException>(() => admin.ExecuteAsync(f.Account, new(tooBroad)),
            "Leasing dimension execution fails explicitly at its materialization limit and never silently truncates");
    }

    private static async Task VerifyPurchaseMetricsAsync(ReportExecutionService execution, Fixture f,
        ReportPeriodSelection year)
    {
        var expected = new Dictionary<ReportMetric, ReportExecutionValue[]>
        {
            [ReportMetric.LeasingOriginalPurchaseNet] =
                [new(200, "EUR"), new(400, "NOK")],
            [ReportMetric.LeasingOriginalPurchaseVat] =
                [new(40, "EUR"), new(100, "NOK")],
            [ReportMetric.LeasingOriginalPurchaseGross] =
                [new(240, "EUR"), new(500, "NOK")],
            [ReportMetric.LeasingAdjustedPurchaseNet] =
                [new(180, "EUR"), new(385, "NOK")],
            [ReportMetric.LeasingAdjustedPurchaseVat] =
                [new(36, "EUR"), new(96.25m, "NOK")],
            [ReportMetric.LeasingAdjustedPurchaseGross] =
                [new(216, "EUR"), new(481.25m, "NOK")]
        };
        foreach (var (metric, values) in expected)
        {
            var result = await execution.ExecuteAsync(f.Account, new(Definition(metric,
                ReportDateBasis.LeasingAcquisitionPurchaseDate, ReportAggregation.Sum,
                ReportVisualization.Table, year), 1, 1));
            Check.That(result.Totals.SequenceEqual(values) && result.Table is { TotalRows: 3 } &&
                result.Table.Rows.Count == 1 && result.Metadata.Unit == ReportValueUnit.Amount,
                $"{metric} uses its approved original/credit/reversal component, keeps currencies separate and pages independently");
        }

        var ownerGroups = await execution.ExecuteAsync(f.Account, new(Definition(
            ReportMetric.LeasingAdjustedPurchaseGross, ReportDateBasis.LeasingAcquisitionPurchaseDate,
            ReportAggregation.Sum, ReportVisualization.Bar, year) with
        {
            Grouping = new LeasingReportGroupingKey(LeasingReportGrouping.Owner)
        }));
        Check.That(ownerGroups.Groups.Count == 2 &&
            ownerGroups.Groups.SelectMany(x => x.Values).Sum(x => x.Value) == 697.25m,
            "adjusted purchase values support the approved owner grouping without duplicating acquisition amounts");
    }

    private static async Task VerifyCapacityMetricsAsync(ReportExecutionService execution, Fixture f,
        ReportPeriodSelection year)
    {
        await using var db = f.Open();
        var existing = await db.LeasingAcquisitions.AsNoTracking()
            .Where(x => x.AccountId == f.Account && x.FrameworkId == f.FrameworkAlpha).ToListAsync();
        var calculatorGross = LeasingCalculator.Used(existing, true);

        foreach (var (metric, nok) in new[]
        {
            (ReportMetric.LeasingCapacityUsedNet, 92m),
            (ReportMetric.LeasingCapacityUsedVat, 23m),
            (ReportMetric.LeasingCapacityUsedGross, 115m)
        })
        {
            var result = await execution.ExecuteAsync(f.Account, new(Definition(metric,
                ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap, ReportAggregation.Sum,
                ReportVisualization.Table, year)));
            Check.That(result.Totals.SequenceEqual(
                    [new ReportExecutionValue(0, "EUR"), new ReportExecutionValue(nok, "NOK")]) &&
                result.Metadata.Additivity == ReportAdditivity.CurrentSnapshot &&
                result.Metadata.RestrictionResourceKeys.Contains("ReportRestrictionCurrentSnapshot"),
                $"{metric} uses the registered post-release capacity snapshot and preserves framework currency");
        }
        Check.That(calculatorGross == 115m,
            "capacity gross reconciles exactly with the existing LeasingCalculator.Used rule");

        var statusGroups = await execution.ExecuteAsync(f.Account, new(Definition(
            ReportMetric.LeasingCapacityUsedGross, ReportDateBasis.LeasingFrameworkAcquisitionWindowOverlap,
            ReportAggregation.Sum, ReportVisualization.Bar, year) with
        {
            Grouping = new LeasingReportGroupingKey(LeasingReportGrouping.Status)
        }));
        Check.That(statusGroups.Groups.Count == 2 &&
            statusGroups.Groups.SelectMany(x => x.Values).Sum(x => x.Value) == 115m,
            "capacity supports approved framework status grouping without including reserved orders or limits");
    }

    private static async Task SeedScopeLimitAsync(Fixture f)
    {
        await using var db = f.Open();
        for (var i = 0; i < 1_001; i++)
        {
            var id = Guid.NewGuid();
            db.LeasingAcquisitions.Add(new LeasingAcquisition
            {
                Id = id, AccountId = f.Account, Name = $"scope-limit-{i:D4}",
                Reference = $"scope-limit-{i:D4}", OwnerUserId = f.Admin,
                SupplierOrganizationId = f.PartyA, FinanceOrganizationId = f.PartyA,
                PurchaseDate = new(2026, 2, 1), EndDate = new(2027, 2, 1),
                Currency = "NOK", Status = LeasingAcquisitionStatus.Registered,
                NetTotal = 1, GrossTotal = 1, Revision = Guid.NewGuid()
            });
            db.LeasingAcquisitionDimensionAssignments.Add(new LeasingAcquisitionDimensionAssignment
            {
                Id = Guid.NewGuid(), AccountId = f.Account, AcquisitionId = id,
                DimensionId = f.DimensionOne, ValueId = f.Root,
                DimensionName = "GEO", DimensionCode = "GEO",
                ValueName = "Norge", ValueCode = "NO", ValuePath = "Norge"
            });
        }
        await db.SaveChangesAsync();
    }

    private static ReportValueFilterSelection Value(LeasingCatalogFilter field, ReportFilterValue value) =>
        new(new LeasingReportFilterKey(field), value);

    private static ReportPeriodSelection Period(DateOnly from, DateOnly to) =>
        new(ReportPeriodRule.Custom, from, to);

    private static ReportCardDefinition Definition(ReportMetric metric, ReportDateBasis date,
        ReportAggregation aggregation, ReportVisualization visualization, ReportPeriodSelection period) =>
        new(ReportModule.Leasing, 1, [], period, date, metric, aggregation,
            Visualization: visualization);
}
