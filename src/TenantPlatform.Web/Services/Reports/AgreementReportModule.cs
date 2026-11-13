using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Reports;

public sealed class AgreementReportModule(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization)
{
    private const int MaxMaterializedAgreements = 10_000;
    private const int MaxFinancialAgreements = 1_000;
    private const int MaxFinancialRows = 20_000;

    internal async Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request,
        ReportModuleCatalog catalog, ReportDefinitionSemantics semantics, ReportDateRange period,
        ReportLocalDate local, CancellationToken ct)
    {
        var definition = request.Definition;
        var metric = catalog.Compatibility.Metrics.Single(x => x.Metric == definition.Metric);
        await using var db = await factory.CreateDbContextAsync(ct);
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !await db.UserAccounts.AsNoTracking().AnyAsync(x => x.AccountId == accountId &&
                x.UserId == current.UserId && x.User.IsActive, ct) ||
            !await authorization.CanUseAgreementsAsync(ct))
            throw new UnauthorizedAccessException();
        var admin = await authorization.CanCreateAgreementAsync(ct);
        var filter = Filters(definition.Filters);
        var visible = AgreementListQuery.Authorized(db, accountId, current.UserId, admin).AsNoTracking();
        visible = AgreementListQuery.ApplyAttention(visible, filter.Attention,
            filter.Attention.HasValue ? local.Today : null);
        var dimensionSelections = definition.Filters.OfType<ReportDimensionFilterSelection>()
            .Select(x => new DimensionListFilterSelection
            {
                DimensionId = x.DimensionKey.DimensionId,
                ValueId = x.ValueId,
                IncludeUnspecified = x.IncludeUnspecified
            }).ToList();
        visible = AgreementListQuery.ApplyDimensions(db, accountId, visible,
            DimensionListFilters.Plan(catalog.DimensionFilters, dimensionSelections));
        var query = AgreementListQuery.ApplyRecordFilters(
            AgreementListQuery.Project(db, accountId, visible), filter).AsNoTracking();

        return metric.Metric switch
        {
            ReportMetric.AgreementCount => await CountAsync(db, accountId, query, request, catalog, semantics,
                metric, period, local, ct),
            ReportMetric.AgreementIncomeAmount => await AmountAsync(db, accountId,
                query.Where(x => x.Agreement.Direction == AgreementDirection.Income), request, catalog,
                semantics, metric, period, local, AgreementDirection.Income, ct),
            ReportMetric.AgreementCostAmount => await AmountAsync(db, accountId,
                query.Where(x => x.Agreement.Direction == AgreementDirection.Cost), request, catalog,
                semantics, metric, period, local, AgreementDirection.Cost, ct),
            _ => throw new ReportExecutionException("ReportExecutionUnsupportedMetric")
        };
    }

    internal static AgreementFilter Filters(IReadOnlyList<ReportFilterSelection> selections)
    {
        var result = new AgreementFilter();
        foreach (var selection in selections.OfType<ReportValueFilterSelection>())
        {
            if (selection.Key is not AgreementReportFilterKey key) continue;
            switch (key.Field)
            {
                case AgreementReportFilter.Search:
                    result.Search = ((ReportTextValue)selection.Value).Value;
                    break;
                case AgreementReportFilter.Status:
                    result.Status = ((ReportEnumValue<AgreementStatus>)selection.Value).Value;
                    break;
                case AgreementReportFilter.Type:
                    result.Type = ((ReportEnumValue<AgreementType>)selection.Value).Value;
                    break;
                case AgreementReportFilter.Owner:
                    result.OwnerUserId = ((ReportEntityValue)selection.Value).Id;
                    break;
                case AgreementReportFilter.Attention:
                    result.Attention = ((ReportEnumValue<AgreementAttention>)selection.Value).Value;
                    break;
            }
        }
        return result;
    }

    internal static IQueryable<AgreementListQueryRow> Overlap(
        IQueryable<AgreementListQueryRow> source, ReportDateRange period) =>
        source.Where(x => x.Agreement.StartDate <= period.To &&
            (!(x.Agreement.CessationDate ?? x.Agreement.EndDate).HasValue ||
             (x.Agreement.CessationDate ?? x.Agreement.EndDate) >= period.From));

    private static async Task<ReportExecutionResult> CountAsync(TenantPlatformDbContext db, Guid accountId,
        IQueryable<AgreementListQueryRow> source, ReportExecutionRequest request, ReportModuleCatalog catalog,
        ReportDefinitionSemantics semantics, ReportMetricDefinition metric, ReportDateRange period,
        ReportLocalDate local, CancellationToken ct)
    {
        var query = Overlap(source, period);
        var total = await query.Select(x => x.Agreement.Id).Distinct().CountAsync(ct);
        var groups = new List<ReportGroupResult>();
        var actualMultiple = false;
        if (request.Definition.Grouping is not null)
        {
            if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
            {
                var rows = await MaterializeAsync(query, MaxMaterializedAgreements, ct);
                var expansion = await DimensionExpansionAsync(db, accountId, rows, dimension, catalog, ct);
                actualMultiple = expansion.Multiple;
                groups.AddRange(expansion.Memberships.GroupBy(x => x.Group)
                    .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
                    .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label,
                        [new(x.Select(y => y.Row.Agreement.Id).Distinct().Count())],
                        x.Select(y => y.Row.Agreement.Id).Distinct().Count())));
            }
            else
            {
                groups.AddRange(await FixedCountGroupsAsync(query, request.Definition.Grouping, ct));
            }
        }

        var time = new List<ReportTimeResult>();
        if (request.Definition.TimeGrouping.HasValue)
        {
            var rows = await MaterializeAsync(query, MaxMaterializedAgreements, ct);
            foreach (var bucket in ReportTimeBuckets.Create(request.Definition.TimeGrouping.Value, period))
            {
                var count = rows.Count(x => x.Agreement.StartDate <= bucket.OverlapTo &&
                    (!(x.Agreement.CessationDate ?? x.Agreement.EndDate).HasValue ||
                     (x.Agreement.CessationDate ?? x.Agreement.EndDate) >= bucket.OverlapFrom));
                time.Add(new(bucket.Key.MachineKey, bucket.Key.Year, bucket.Key.Bucket,
                    bucket.FullFrom, bucket.FullTo, bucket.OverlapFrom, bucket.OverlapTo,
                    [new(count)], count));
            }
        }

        ReportTableResult? table = null;
        if (request.Definition.Visualization == ReportVisualization.Table)
        {
            var pageRows = await query.OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id)
                .Skip((request.TablePage - 1) * request.TablePageSize).Take(request.TablePageSize)
                .Select(x => new
                {
                    x.Agreement.Id, x.Agreement.Title, x.Counterparty, x.Owner,
                    x.Agreement.Type, x.Agreement.Status
                }).ToListAsync(ct);
            table = new(request.TablePage, request.TablePageSize, total, pageRows.Select(x =>
                new ReportAgreementTableRow($"agreement:{x.Id:N}", x.Id, x.Title, x.Counterparty,
                    x.Owner, x.Type, x.Status)).ToList());
        }

        return ValidateResult(request.Definition, metric, period, local, semantics,
            [new(total)], groups, time, table, total,
            groups.Count > 0 ? groups.Sum(x => x.UniqueEntityCount) : time.Sum(x => x.UniqueEntityCount),
            actualMultiple, actualMultiple || semantics.MultipleGroupMembership ||
                request.Definition.TimeGrouping.HasValue,
            true, ["ReportRestrictionOverlapTotalsUnique"]);
    }

    private static async Task<ReportExecutionResult> AmountAsync(TenantPlatformDbContext db, Guid accountId,
        IQueryable<AgreementListQueryRow> source, ReportExecutionRequest request, ReportModuleCatalog catalog,
        ReportDefinitionSemantics semantics, ReportMetricDefinition metric, ReportDateRange period,
        ReportLocalDate local, AgreementDirection direction, CancellationToken ct)
    {
        if (period.From.Year < 3 || period.To.Year > 9996 || period.To.DayNumber - period.From.DayNumber > 366)
            throw new ReportExecutionException("ReportExecutionTooBroad");
        var headers = await source.OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id)
            .Take(MaxFinancialAgreements + 1).ToListAsync(ct);
        if (headers.Count > MaxFinancialAgreements) TooBroad();
        var agreementIds = headers.Select(x => x.Agreement.Id).ToArray();
        var versions = await db.AgreementLineVersions.AsNoTracking()
            .Where(x => x.AccountId == accountId &&
                agreementIds.Contains(x.AgreementId) && x.Status != AgreementLineStatus.Draft)
            .OrderBy(x => x.AgreementId).ThenBy(x => x.LineId).ThenBy(x => x.Sequence)
            .Take(MaxFinancialRows + 1).ToListAsync(ct);
        var prices = await db.AgreementPriceVersions.AsNoTracking()
            .Where(x => x.AccountId == accountId &&
                agreementIds.Contains(x.AgreementId))
            .OrderBy(x => x.AgreementId).ThenBy(x => x.LineId).ThenBy(x => x.Sequence)
            .Take(MaxFinancialRows + 1).ToListAsync(ct);
        if (versions.Count > MaxFinancialRows || prices.Count > MaxFinancialRows) TooBroad();
        var selections = await db.AgreementIndexSelections.AsNoTracking()
            .Where(x => x.AccountId == accountId && agreementIds.Contains(x.AgreementId))
            .OrderBy(x => x.AgreementId).ThenBy(x => x.Sequence).ToListAsync(ct);
        var indexIds = selections.Where(x => x.IndexId.HasValue).Select(x => x.IndexId!.Value).Distinct().ToArray();
        var indices = await db.AgreementIndexRecords.AsNoTracking()
            .Where(x => x.AccountId == accountId && indexIds.Contains(x.Id))
            .ToListAsync(ct);
        var indexValues = await db.AgreementIndexValueRecords.AsNoTracking()
            .Where(x => x.AccountId == accountId &&
                indexIds.Contains(x.IndexId) && !x.Superseded).ToListAsync(ct);

        var financial = new List<FinancialRow>();
        foreach (var header in headers)
        {
            ct.ThrowIfCancellationRequested();
            var agreementVersions = versions.Where(x => x.AgreementId == header.Agreement.Id).ToList();
            var sequences = agreementVersions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
            var agreementPrices = prices.Where(x => x.AgreementId == header.Agreement.Id &&
                (x.Independent || sequences.Contains((x.LineId, x.Sequence)))).ToList();
            var automatic = AgreementAutomaticIndexCalculator.Project(header.Agreement.IndexSetupNeedsReview,
                agreementVersions, agreementPrices,
                selections.Where(x => x.AgreementId == header.Agreement.Id).ToList(), indices, indexValues);
            var calculated = AgreementPeriodCalculator.CalculateInvoicePeriods(header.Agreement.Id, direction,
                header.Agreement.Currency ?? throw new AgreementValidationException("FinanceSetupRequired"),
                agreementVersions, agreementPrices, period.From, period.To, automatic);
            financial.AddRange(calculated.Select(x => new FinancialRow(header, x)));
            if (financial.Count > MaxFinancialRows) TooBroad();
        }

        var totals = Values(financial);
        var groups = new List<ReportGroupResult>();
        var actualMultiple = false;
        if (request.Definition.Grouping is not null)
        {
            IReadOnlyList<FinancialMembership> memberships;
            if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
            {
                var expansion = await DimensionExpansionAsync(db, accountId, headers, dimension, catalog, ct);
                actualMultiple = expansion.Multiple;
                var byAgreement = expansion.Memberships.GroupBy(x => x.Row.Agreement.Id)
                    .ToDictionary(x => x.Key, x => x.Select(y => y.Group).ToList());
                memberships = financial.SelectMany(row => byAgreement.GetValueOrDefault(row.Header.Agreement.Id, [])
                    .Select(group => new FinancialMembership(row, group))).ToList();
            }
            else
            {
                memberships = financial.Select(x => new FinancialMembership(x,
                    FixedGroup(x.Header, request.Definition.Grouping))).ToList();
            }
            groups.AddRange(memberships.GroupBy(x => x.Group)
                .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
                .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label, Values(x.Select(y => y.Row)),
                    x.Select(y => y.Row.Header.Agreement.Id).Distinct().Count())));
        }

        var time = new List<ReportTimeResult>();
        if (request.Definition.TimeGrouping.HasValue)
        {
            var currencies = financial.Select(x => x.Period.Currency).Distinct().Order().ToArray();
            foreach (var bucket in ReportTimeBuckets.Create(request.Definition.TimeGrouping.Value, period))
            {
                var rows = financial.Where(x => x.Period.InvoiceDate >= bucket.OverlapFrom &&
                    x.Period.InvoiceDate <= bucket.OverlapTo).ToList();
                var values = currencies.Select(currency => new ReportExecutionValue(
                    rows.Where(x => x.Period.Currency == currency).Sum(x => x.Period.Amount), currency)).ToList();
                time.Add(new(bucket.Key.MachineKey, bucket.Key.Year, bucket.Key.Bucket,
                    bucket.FullFrom, bucket.FullTo, bucket.OverlapFrom, bucket.OverlapTo, values,
                    rows.Select(x => x.Header.Agreement.Id).Distinct().Count()));
            }
        }

        ReportTableResult? table = null;
        if (request.Definition.Visualization == ReportVisualization.Table)
        {
            var ordered = financial.OrderBy(x => x.Period.InvoiceDate)
                .ThenBy(x => x.Header.Agreement.Title).ThenBy(x => x.Header.Agreement.Id)
                .ThenBy(x => x.Period.EventKey).ThenBy(x => x.Period.From)
                .ThenBy(x => x.Period.LineId).ThenBy(x => x.Period.PriceVersionId).ToList();
            var page = ordered.Skip((request.TablePage - 1) * request.TablePageSize)
                .Take(request.TablePageSize).Select(x => new ReportAgreementTableRow(
                    $"period:{x.Header.Agreement.Id:N}:{x.Period.EventKey}:{x.Period.From:yyyyMMdd}:{x.Period.PriceVersionId:N}",
                    x.Header.Agreement.Id, x.Header.Agreement.Title, x.Header.Counterparty, x.Header.Owner,
                    x.Header.Agreement.Type, x.Header.Agreement.Status, x.Period.InvoiceDate,
                    x.Period.From, x.Period.To, x.Period.EventKey, x.Period.LineName,
                    x.Period.Amount, x.Period.Currency)).ToList();
            table = new(request.TablePage, request.TablePageSize, ordered.Count, page);
        }

        return ValidateResult(request.Definition, metric, period, local, semantics, totals, groups, time,
            table, financial.Select(x => x.Header.Agreement.Id).Distinct().Count(),
            groups.Count > 0 ? groups.Sum(x => x.UniqueEntityCount) : time.Sum(x => x.UniqueEntityCount),
            actualMultiple, actualMultiple || semantics.MultipleGroupMembership, true,
            actualMultiple || semantics.MultipleGroupMembership
                ? ["ReportRestrictionCurrencySeparated", "ReportRestrictionGroupsNonAdditive"]
                : ["ReportRestrictionCurrencySeparated"]);
    }

    private static async Task<List<AgreementListQueryRow>> MaterializeAsync(
        IQueryable<AgreementListQueryRow> query, int limit, CancellationToken ct)
    {
        var rows = await query.OrderBy(x => x.Agreement.Id).Take(limit + 1).ToListAsync(ct);
        if (rows.Count > limit) TooBroad();
        return rows;
    }

    private static async Task<IReadOnlyList<ReportGroupResult>> FixedCountGroupsAsync(
        IQueryable<AgreementListQueryRow> query, ReportGroupingKey grouping, CancellationToken ct)
    {
        if (grouping is AgreementReportGroupingKey { Field: AgreementReportGrouping.Type })
        {
            var rows = await query.GroupBy(x => x.Agreement.Type)
                .Select(x => new { Key = x.Key, Count = x.Select(y => y.Agreement.Id).Distinct().Count() })
                .OrderBy(x => x.Key).ToListAsync(ct);
            return rows.Select(x => new ReportGroupResult($"type:{(int)x.Key}",
                ReportExecutionLabel.Resource($"AgreementType{x.Key}"), [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is AgreementReportGroupingKey { Field: AgreementReportGrouping.Status })
        {
            var rows = await query.GroupBy(x => x.Agreement.Status)
                .Select(x => new { Key = x.Key, Count = x.Select(y => y.Agreement.Id).Distinct().Count() })
                .OrderBy(x => x.Key).ToListAsync(ct);
            return rows.Select(x => new ReportGroupResult($"status:{(int)x.Key}",
                ReportExecutionLabel.Resource($"AgreementStatus{x.Key}"), [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is AgreementReportGroupingKey { Field: AgreementReportGrouping.Counterparty })
        {
            var rows = await query.GroupBy(x => new { x.Agreement.CounterpartyOrganizationId, x.Counterparty })
                .Select(x => new { x.Key.CounterpartyOrganizationId, x.Key.Counterparty,
                    Count = x.Select(y => y.Agreement.Id).Distinct().Count() })
                .OrderBy(x => x.Counterparty).ThenBy(x => x.CounterpartyOrganizationId).ToListAsync(ct);
            return rows.Select(x => new ReportGroupResult($"counterparty:{x.CounterpartyOrganizationId:N}",
                ReportExecutionLabel.Dynamic(x.Counterparty), [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is AgreementReportGroupingKey { Field: AgreementReportGrouping.Owner })
        {
            var rows = await query.GroupBy(x => new { x.Agreement.OwnerUserId, x.Owner })
                .Select(x => new { x.Key.OwnerUserId, x.Key.Owner,
                    Count = x.Select(y => y.Agreement.Id).Distinct().Count() })
                .OrderBy(x => x.Owner).ThenBy(x => x.OwnerUserId).ToListAsync(ct);
            return rows.Select(x => new ReportGroupResult($"owner:{x.OwnerUserId:N}",
                ReportExecutionLabel.Dynamic(x.Owner), [new(x.Count)], x.Count)).ToList();
        }
        throw new ReportExecutionException("ReportExecutionUnsupportedGrouping");
    }

    private static async Task<DimensionExpansion> DimensionExpansionAsync(TenantPlatformDbContext db,
        Guid accountId, IReadOnlyCollection<AgreementListQueryRow> rows, ReportDimensionGroupingKey grouping,
        ReportModuleCatalog catalog, CancellationToken ct)
    {
        var definition = catalog.DimensionFilters.Dimensions.Single(x => x.DimensionId == grouping.DimensionId);
        var ids = rows.Select(x => x.Agreement.Id).ToArray();
        var assignments = await db.AgreementDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.AgreementId) &&
                x.DimensionId == grouping.DimensionId)
            .Select(x => new { x.AgreementId, x.ValueId }).Distinct().ToListAsync(ct);
        var values = definition.Values.ToDictionary(x => x.Id);
        var byAgreement = assignments.GroupBy(x => x.AgreementId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ValueId).Where(values.ContainsKey).Distinct().ToList());
        var result = new List<AgreementMembership>();
        var multiple = false;
        foreach (var row in rows)
        {
            var selected = byAgreement.GetValueOrDefault(row.Agreement.Id, []);
            if (selected.Count > 1) multiple = true;
            if (selected.Count == 0)
            {
                result.Add(new(row, new($"dimension:{grouping.DimensionId:N}:unspecified",
                    ReportExecutionLabel.Resource("DashboardUnspecified"))));
                continue;
            }
            foreach (var id in selected)
                result.Add(new(row, new($"dimension:{grouping.DimensionId:N}:{id:N}",
                    ReportExecutionLabel.Dynamic(values[id].Path))));
        }
        return new(result, multiple);
    }

    private static GroupIdentity FixedGroup(AgreementListQueryRow row, ReportGroupingKey grouping) => grouping switch
    {
        AgreementReportGroupingKey { Field: AgreementReportGrouping.Type } =>
            new($"type:{(int)row.Agreement.Type}", ReportExecutionLabel.Resource($"AgreementType{row.Agreement.Type}")),
        AgreementReportGroupingKey { Field: AgreementReportGrouping.Status } =>
            new($"status:{(int)row.Agreement.Status}", ReportExecutionLabel.Resource($"AgreementStatus{row.Agreement.Status}")),
        AgreementReportGroupingKey { Field: AgreementReportGrouping.Counterparty } =>
            new($"counterparty:{row.Agreement.CounterpartyOrganizationId:N}", ReportExecutionLabel.Dynamic(row.Counterparty)),
        AgreementReportGroupingKey { Field: AgreementReportGrouping.Owner } =>
            new($"owner:{row.Agreement.OwnerUserId:N}", ReportExecutionLabel.Dynamic(row.Owner)),
        _ => throw new ReportExecutionException("ReportExecutionUnsupportedGrouping")
    };

    private static IReadOnlyList<ReportExecutionValue> Values(IEnumerable<FinancialRow> rows) =>
        rows.GroupBy(x => x.Period.Currency).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ReportExecutionValue(x.Sum(y => y.Period.Amount), x.Key)).ToList();

    private static ReportExecutionResult ValidateResult(ReportCardDefinition definition,
        ReportMetricDefinition metric, ReportDateRange period, ReportLocalDate local,
        ReportDefinitionSemantics semantics, IReadOnlyList<ReportExecutionValue> totals,
        IReadOnlyList<ReportGroupResult> groups, IReadOnlyList<ReportTimeResult> time,
        ReportTableResult? table, int uniqueEntities, int groupOccurrences, bool actualMultiple,
        bool nonAdditiveGroups, bool emptyBuckets, IReadOnlyList<string> restrictions)
    {
        var currencies = totals.Where(x => x.Currency is not null).Select(x => x.Currency!).ToArray();
        var resultValidation = ReportResultValidator.Validate(definition, metric,
            new(currencies, true,
                totals.Any(x => x.Value < 0) || groups.SelectMany(x => x.Values).Any(x => x.Value < 0),
                actualMultiple, totals.Count > 0));
        if (!resultValidation.IsValid) throw new ReportDefinitionValidationException(resultValidation.Issues);
        var multipleMembership = actualMultiple || semantics.MultipleGroupMembership;
        return new(definition.Module, metric.Metric, period, local.Today, local.TimeZoneId,
            totals, groups, time, table,
            new(semantics.Grain, metric.AmountComponent == ReportAmountComponent.None
                    ? ReportValueUnit.Count : ReportValueUnit.Amount,
                semantics.CurrencyRule, semantics.Additivity,
                nonAdditiveGroups || semantics.UniqueTotalRequired,
                !nonAdditiveGroups && semantics.GroupTotalsAdditive,
                multipleMembership, emptyBuckets && definition.TimeGrouping.HasValue,
                uniqueEntities, groupOccurrences, semantics.SourceRule, restrictions));
    }

    private static void TooBroad() => throw new ReportExecutionException("ReportExecutionTooBroad");

    private sealed record GroupIdentity(string Key, ReportExecutionLabel Label);
    private sealed record AgreementMembership(AgreementListQueryRow Row, GroupIdentity Group);
    private sealed record DimensionExpansion(IReadOnlyList<AgreementMembership> Memberships, bool Multiple);
    private sealed record FinancialRow(AgreementListQueryRow Header, AgreementCalculatedPeriod Period);
    private sealed record FinancialMembership(FinancialRow Row, GroupIdentity Group);
}
