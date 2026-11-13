using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Reports;

static class ExecutionChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var admin = f.Execution(f.Admin);
        var year = Period(new(2026, 1, 1), new(2026, 12, 31));
        var countTable = Definition(ReportMetric.AgreementCount, ReportDateBasis.AgreementPeriodOverlap,
            ReportAggregation.CountDistinct, ReportVisualization.Table, year);

        var count = await admin.ExecuteAsync(f.Account, new(countTable, 1, 2));
        Check.That(count.Totals.Single().Value == 4 && count.Metadata.UniqueEntityCount == 4 &&
            count.Table is { TotalRows: 4, PageSize: 2 } && count.Table.Rows.Count == 2,
            "agreement count uses distinct authorized ids, includes the established archived list scope and pages rows separately from totals");
        var secondPage = await admin.ExecuteAsync(f.Account, new(countTable, 2, 2));
        Check.That(secondPage.Totals.Single().Value == 4 && secondPage.Table is { TotalRows: 4 } &&
            secondPage.Table.Rows.Count == 2 && !secondPage.Table.Rows.OfType<ReportAgreementTableRow>()
                .Select(x => x.AgreementId).Intersect(count.Table!.Rows.OfType<ReportAgreementTableRow>()
                    .Select(x => x.AgreementId)).Any(),
            "agreement table paging is deterministic and never changes the whole-selection total");

        var reader = await f.Execution(f.Reader).ExecuteAsync(f.Account, new(countTable));
        Check.That(reader.Totals.Single().Value == 2 &&
            reader.Table!.Rows.OfType<ReportAgreementTableRow>().Select(x => x.AgreementId)
                .ToHashSet().SetEquals([f.AgreementAlpha, f.AgreementBeta]),
            "direct report execution reapplies current-user agreement resource access");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Execution(f.NoModuleAccess)
            .ExecuteAsync(f.Account, new(countTable)), "execution rejects an active member without Agreements access");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Execution(f.Reader, f.ForeignAccount)
            .ExecuteAsync(f.Account, new(countTable)), "execution rejects a forged account outside the selected context");

        var parentFilter = countTable with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.Agreement, f.DimensionOne), f.Root, false)
            ]
        };
        var filtered = await admin.ExecuteAsync(f.Account, new(parentFilter));
        Check.That(filtered.Totals.Single().Value == 2 && filtered.Table!.Rows.OfType<ReportAgreementTableRow>()
                .Select(x => x.AgreementId).ToHashSet().SetEquals([f.AgreementAlpha, f.AgreementBeta]),
            "execution reuses hierarchical parent-filter semantics from the Agreements list query");
        var recordFilters = countTable with
        {
            Filters =
            [
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search),
                    new ReportTextValue("alpha")),
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Status),
                    new ReportEnumValue<AgreementStatus>(AgreementStatus.Active)),
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Type),
                    new ReportEnumValue<AgreementType>(AgreementType.Lease)),
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Owner),
                    new ReportEntityValue(f.OwnerA)),
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Attention),
                    new ReportEnumValue<AgreementAttention>(AgreementAttention.Notice))
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(recordFilters))).Totals.Single().Value == 1,
            "execution applies search, status, type, owner and account-local attention with list parity");
        var historical = countTable with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.Agreement, f.HistoricalDimension),
                    f.HistoricalValue, false)
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(historical))).Totals.Single().Value == 1,
            "execution preserves historical dimension filter choices from the authorized catalog");
        var unspecified = countTable with
        {
            Filters =
            [
                new ReportDimensionFilterSelection(
                    new ReportDimensionFilterKey(ReportDimensionScope.Agreement, f.DimensionOne), null, true)
            ]
        };
        Check.That((await admin.ExecuteAsync(f.Account, new(unspecified))).Totals.Single().Value == 2,
            "execution preserves not-specified dimension semantics");

        var fixedGroups = await admin.ExecuteAsync(f.Account, new(countTable with
        {
            Grouping = new AgreementReportGroupingKey(AgreementReportGrouping.Type)
        }));
        Check.That(fixedGroups.Groups.Sum(x => x.Values.Single().Value) == 4 &&
            fixedGroups.Groups.Select(x => x.Key).Order().SequenceEqual(["type:1", "type:4", "type:5"]),
            "fixed agreement grouping uses stable keys and database-side distinct counts");

        var dimensionGroups = await admin.ExecuteAsync(f.Account, new(countTable with
        {
            Grouping = new ReportDimensionGroupingKey(ReportDimensionScope.Agreement, f.DimensionOne)
        }));
        Check.That(dimensionGroups.Totals.Single().Value == 4 &&
            dimensionGroups.Groups.Sum(x => x.UniqueEntityCount) == 5 &&
            dimensionGroups.Metadata.MultipleGroupMembership && !dimensionGroups.Metadata.GroupTotalsAdditive &&
            dimensionGroups.Metadata.UniqueTotalBeforeGroupExpansion,
            "multi-value dimension grouping preserves one unique total and exposes non-additive group occurrences");

        var monthlyCount = await admin.ExecuteAsync(f.Account, new(countTable with
        {
            Visualization = ReportVisualization.Line,
            TimeGrouping = ReportTimeGrouping.Month
        }));
        Check.That(monthlyCount.TimeSeries.Count == 12 && monthlyCount.TimeSeries.All(x => x.Values.Single().Value == 4) &&
            monthlyCount.Metadata.UniqueEntityCount == 4 && monthlyCount.Metadata.GroupOccurrenceCount == 48 &&
            !monthlyCount.Metadata.GroupTotalsAdditive,
            "overlap count may occur in every chronological bucket while the period total remains distinct");

        var income2026 = Definition(ReportMetric.AgreementIncomeAmount,
            ReportDateBasis.AgreementInvoiceDate, ReportAggregation.Sum, ReportVisualization.Table, year);
        var income = await admin.ExecuteAsync(f.Account, new(income2026, 1, 3));
        Check.That(income.Totals.SequenceEqual([new ReportExecutionValue(1155.48m, "NOK")]) &&
            income.Table is { TotalRows: 12 } && income.Table.Rows.Count == 3 &&
            income.Table.Rows.OfType<ReportAgreementTableRow>()
                .All(x => x.AgreementId == f.AgreementAlpha && x.EventDate!.Value.Year == 2026),
            "invoice-date income reuses free-period, version, automatic-index and event rounding rules without duplicate joins");
        var incomePage2 = await admin.ExecuteAsync(f.Account, new(income2026, 2, 3));
        Check.That(incomePage2.Totals.SequenceEqual(income.Totals) && incomePage2.Table!.TotalRows == 12,
            "financial table paging leaves the calculator total unchanged");

        var monthlyIncome = await admin.ExecuteAsync(f.Account, new(income2026 with
        {
            Visualization = ReportVisualization.Line,
            TimeGrouping = ReportTimeGrouping.Month
        }));
        Check.That(monthlyIncome.TimeSeries.Count == 12 &&
            monthlyIncome.TimeSeries[0].Values.Single().Value == 0 &&
            monthlyIncome.TimeSeries[1].Values.Single().Value == 100 &&
            monthlyIncome.TimeSeries[6].Values.Single().Value == 105.48m &&
            monthlyIncome.TimeSeries.SelectMany(x => x.Values).Sum(x => x.Value) == 1155.48m,
            "invoice-date time series fills empty additive buckets and follows advance invoice dates chronologically");

        var january2027 = Period(new(2027, 1, 1), new(2027, 1, 31));
        var mixed = await admin.ExecuteAsync(f.Account, new(income2026 with { Period = january2027 }));
        Check.That(mixed.Totals.OrderBy(x => x.Currency).SequenceEqual(
                [new ReportExecutionValue(1200m, "EUR"), new ReportExecutionValue(110m, "NOK")]) &&
            mixed.Table!.Rows.OfType<ReportAgreementTableRow>()
                .Any(x => x.AgreementId == f.AgreementBeta && x.EventDate == new DateOnly(2027, 1, 1)),
            "arrears events are found by invoice date outside their delivery year and currencies remain separate");
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => admin.ExecuteAsync(f.Account,
            new(income2026 with { Period = january2027, Visualization = ReportVisualization.KeyFigure })),
            "a monetary KPI rejects a result containing more than one currency");

        var cost = await admin.ExecuteAsync(f.Account, new(Definition(ReportMetric.AgreementCostAmount,
            ReportDateBasis.AgreementInvoiceDate, ReportAggregation.Sum, ReportVisualization.Table,
            Period(new(2026, 1, 1), new(2026, 3, 31)))));
        Check.That(cost.Totals.SequenceEqual([new ReportExecutionValue(100m, "GBP")]) &&
            cost.Table!.Rows.OfType<ReportAgreementTableRow>().All(x => x.AgreementId == f.AgreementGamma),
            "cost execution remains separate from income and preserves the calculator's positive amount and currency semantics");

        var empty = await admin.ExecuteAsync(f.Account, new(income2026 with
        {
            Period = Period(new(2030, 1, 1), new(2030, 12, 31)),
            Visualization = ReportVisualization.Line,
            TimeGrouping = ReportTimeGrouping.Month
        }));
        Check.That(empty.Totals.Count == 0 && empty.TimeSeries.Count == 12 &&
            empty.TimeSeries.All(x => x.Values.Count == 0),
            "empty monetary selections retain chronological buckets without inventing a currency");

        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => admin.ExecuteAsync(f.Account,
            new(income2026 with { DateBasis = ReportDateBasis.AgreementPeriodOverlap })),
            "execution rejects deferred applies-to-period economics through the compatibility validator");
        await Check.ThrowsAsync<ReportExecutionException>(() => admin.ExecuteAsync(f.Account,
            new(income2026 with { Period = Period(new(2024, 1, 1), new(2026, 1, 2)) })),
            "financial execution applies an explicit scope limit instead of silently truncating calculator input");
    }

    private static ReportPeriodSelection Period(DateOnly from, DateOnly to) =>
        new(ReportPeriodRule.Custom, from, to);

    private static ReportCardDefinition Definition(ReportMetric metric, ReportDateBasis date,
        ReportAggregation aggregation, ReportVisualization visualization, ReportPeriodSelection period) =>
        new(ReportModule.Agreements, 1, [], period, date, metric, aggregation,
            Visualization: visualization);
}
