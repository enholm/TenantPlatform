using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

namespace TenantPlatform.Web.Services.Reports;

public sealed class LeasingReportModule(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization)
{
    private const int MaxMaterializedRecords = 1_000;
    private const int MaxCapacityFrameworks = 1_000;

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
            !await authorization.CanUseLeasingAsync(ct))
            throw new UnauthorizedAccessException();

        var admin = await authorization.CanCreateAgreementAsync(ct);
        var frameworkReader = admin || await authorization.CanChangeLeasingLimitAsync(ct);
        var scopes = new LeasingListScopes(
            LeasingListQuery.AuthorizedFrameworks(db, accountId, current.UserId, frameworkReader).AsNoTracking(),
            LeasingListQuery.AuthorizedAcquisitions(db, accountId, current.UserId, admin).AsNoTracking());
        var filter = Filters(definition.Filters);
        scopes = LeasingListQuery.ApplyKind(scopes, filter.Kind);
        var dimensionSelections = definition.Filters.OfType<ReportDimensionFilterSelection>()
            .Select(x => new DimensionListFilterSelection
            {
                DimensionId = x.DimensionKey.DimensionId,
                ValueId = x.ValueId,
                IncludeUnspecified = x.IncludeUnspecified
            }).ToList();
        scopes = LeasingListQuery.ApplyDimensions(db, accountId, scopes,
            DimensionListFilters.Plan(catalog.DimensionFilters, dimensionSelections));
        scopes = LeasingListQuery.ApplyRecordFilters(scopes, filter);

        return metric.Metric switch
        {
            ReportMetric.LeasingFrameworkCount => await FrameworkCountAsync(db, accountId,
                scopes.Frameworks, request, catalog, semantics, metric, period, local, ct),
            ReportMetric.LeasingAcquisitionCount => await AcquisitionCountAsync(db, accountId,
                scopes.Acquisitions, request, catalog, semantics, metric, period, local, ct),
            ReportMetric.LeasingOriginalPurchaseNet or
            ReportMetric.LeasingOriginalPurchaseVat or
            ReportMetric.LeasingOriginalPurchaseGross or
            ReportMetric.LeasingAdjustedPurchaseNet or
            ReportMetric.LeasingAdjustedPurchaseVat or
            ReportMetric.LeasingAdjustedPurchaseGross => await PurchaseAsync(db, accountId,
                scopes.Acquisitions, request, catalog, semantics, metric, period, local, ct),
            ReportMetric.LeasingCapacityUsedNet or
            ReportMetric.LeasingCapacityUsedVat or
            ReportMetric.LeasingCapacityUsedGross => await CapacityAsync(db, accountId,
                scopes.Frameworks, request, catalog, semantics, metric, period, local, ct),
            _ => throw new ReportExecutionException("ReportExecutionUnsupportedMetric")
        };
    }

    internal static LeasingListFilter Filters(IReadOnlyList<ReportFilterSelection> selections)
    {
        var result = new LeasingListFilter { Kind = "all" };
        foreach (var selection in selections.OfType<ReportValueFilterSelection>())
        {
            if (selection.Key is not LeasingReportFilterKey key) continue;
            switch (key.Field)
            {
                case LeasingCatalogFilter.Search:
                    result.Search = ((ReportTextValue)selection.Value).Value;
                    break;
                case LeasingCatalogFilter.Kind:
                    result.Kind = ((ReportEnumValue<LeasingCatalogKind>)selection.Value).Value switch
                    {
                        LeasingCatalogKind.Frameworks => "frameworks",
                        LeasingCatalogKind.Acquisitions => "acquisitions",
                        LeasingCatalogKind.StandaloneAcquisitions => "orders",
                        _ => "all"
                    };
                    break;
                case LeasingCatalogFilter.Status:
                    result.Status = ((ReportEnumValue<LeasingCatalogStatus>)selection.Value).Value.ToString();
                    break;
                case LeasingCatalogFilter.Owner:
                    result.Owner = ((ReportEntityValue)selection.Value).Id;
                    break;
            }
        }
        return result;
    }

    private static IQueryable<LeasingFramework> FrameworkPeriod(
        IQueryable<LeasingFramework> source, ReportDateRange period) =>
        source.Where(x => x.AcquisitionFrom <= period.To && x.AcquisitionTo >= period.From);

    internal static IQueryable<LeasingAcquisition> AcquisitionPeriod(
        IQueryable<LeasingAcquisition> source, ReportDateRange period) =>
        source.Where(x => x.PurchaseDate >= period.From && x.PurchaseDate <= period.To);

    private static async Task<ReportExecutionResult> FrameworkCountAsync(TenantPlatformDbContext db,
        Guid accountId, IQueryable<LeasingFramework> source, ReportExecutionRequest request,
        ReportModuleCatalog catalog, ReportDefinitionSemantics semantics, ReportMetricDefinition metric,
        ReportDateRange period, ReportLocalDate local, CancellationToken ct)
    {
        var query = FrameworkPeriod(source, period);
        var total = await query.Select(x => x.Id).Distinct().CountAsync(ct);
        var groups = new List<ReportGroupResult>();
        var multiple = false;
        if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
        {
            var rows = await FrameworkRowsAsync(db, query, MaxMaterializedRecords, ct);
            var expansion = await FrameworkDimensionExpansionAsync(db, accountId, rows, dimension, catalog, ct);
            multiple = expansion.Multiple;
            groups.AddRange(CountGroups(expansion.Memberships));
        }
        else if (request.Definition.Grouping is not null)
        {
            groups.AddRange(await FrameworkCountGroupsAsync(db, query, request.Definition.Grouping, ct));
        }

        var time = new List<ReportTimeResult>();
        if (request.Definition.TimeGrouping.HasValue)
        {
            var rows = await FrameworkRowsAsync(db, query, MaxMaterializedRecords, ct);
            foreach (var bucket in ReportTimeBuckets.Create(request.Definition.TimeGrouping.Value, period))
            {
                var count = rows.Count(x => x.Framework.AcquisitionFrom <= bucket.OverlapTo &&
                    x.Framework.AcquisitionTo >= bucket.OverlapFrom);
                time.Add(Time(bucket, [new(count)], count));
            }
        }

        var table = request.Definition.Visualization == ReportVisualization.Table
            ? await FrameworkTableAsync(db, query, request, total, ct) : null;
        return Result(request.Definition, metric, period, local, semantics, [new(total)], groups, time,
            table, total, Occurrences(groups, time), multiple,
            multiple || request.Definition.TimeGrouping.HasValue, true,
            ["ReportRestrictionOverlapTotalsUnique"]);
    }

    private static async Task<ReportExecutionResult> AcquisitionCountAsync(TenantPlatformDbContext db,
        Guid accountId, IQueryable<LeasingAcquisition> source, ReportExecutionRequest request,
        ReportModuleCatalog catalog, ReportDefinitionSemantics semantics, ReportMetricDefinition metric,
        ReportDateRange period, ReportLocalDate local, CancellationToken ct)
    {
        var query = AcquisitionPeriod(source, period);
        var total = await query.Select(x => x.Id).Distinct().CountAsync(ct);
        var groups = new List<ReportGroupResult>();
        var multiple = false;
        if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
        {
            var rows = await AcquisitionRowsAsync(db, query, MaxMaterializedRecords, ct);
            var expansion = await AcquisitionDimensionExpansionAsync(db, accountId, rows, dimension, catalog, ct);
            multiple = expansion.Multiple;
            groups.AddRange(CountGroups(expansion.Memberships));
        }
        else if (request.Definition.Grouping is not null)
        {
            groups.AddRange(await AcquisitionCountGroupsAsync(db, query, request.Definition.Grouping, ct));
        }

        var time = new List<ReportTimeResult>();
        if (request.Definition.TimeGrouping.HasValue)
        {
            foreach (var bucket in ReportTimeBuckets.Create(request.Definition.TimeGrouping.Value, period))
            {
                var count = await query.Where(x => x.PurchaseDate >= bucket.OverlapFrom &&
                    x.PurchaseDate <= bucket.OverlapTo).Select(x => x.Id).Distinct().CountAsync(ct);
                time.Add(Time(bucket, [new(count)], count));
            }
        }

        var table = request.Definition.Visualization == ReportVisualization.Table
            ? await AcquisitionTableAsync(db, query, request, total, ct) : null;
        return Result(request.Definition, metric, period, local, semantics, [new(total)], groups, time,
            table, total, Occurrences(groups, time), multiple, multiple, true,
            multiple ? ["ReportRestrictionGroupsNonAdditive"] : []);
    }

    private static async Task<ReportExecutionResult> PurchaseAsync(TenantPlatformDbContext db,
        Guid accountId, IQueryable<LeasingAcquisition> source, ReportExecutionRequest request,
        ReportModuleCatalog catalog, ReportDefinitionSemantics semantics, ReportMetricDefinition metric,
        ReportDateRange period, ReportLocalDate local, CancellationToken ct)
    {
        var query = AcquisitionPeriod(source, period);
        var amounts = ProjectAmounts(query, metric.Metric);
        var totals = await AmountValuesAsync(amounts, ct);
        var unique = await query.Select(x => x.Id).Distinct().CountAsync(ct);
        var groups = new List<ReportGroupResult>();
        var multiple = false;
        if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
        {
            var rows = await AmountRowsAsync(amounts, MaxMaterializedRecords, ct);
            var expansion = await AmountDimensionExpansionAsync(db, accountId, rows, dimension, catalog, ct);
            multiple = expansion.Multiple;
            groups.AddRange(expansion.Memberships.GroupBy(x => x.Group)
                .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
                .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label,
                    Values(x.Select(y => y.Row)), x.Select(y => y.Row.Id).Distinct().Count())));
        }
        else if (request.Definition.Grouping is not null)
        {
            groups.AddRange(await AcquisitionAmountGroupsAsync(db, amounts, request.Definition.Grouping, ct));
        }

        var time = new List<ReportTimeResult>();
        if (request.Definition.TimeGrouping.HasValue)
        {
            var currencies = totals.Select(x => x.Currency!).ToArray();
            foreach (var bucket in ReportTimeBuckets.Create(request.Definition.TimeGrouping.Value, period))
            {
                var values = await amounts.Where(x => x.PurchaseDate >= bucket.OverlapFrom &&
                        x.PurchaseDate <= bucket.OverlapTo)
                    .GroupBy(x => x.Currency).Select(x => new ReportExecutionValue(x.Sum(y => y.Value), x.Key))
                    .ToListAsync(ct);
                var byCurrency = values.ToDictionary(x => x.Currency!);
                var filled = currencies.Select(x => byCurrency.GetValueOrDefault(x,
                    new ReportExecutionValue(0, x))).ToList();
                var count = await query.Where(x => x.PurchaseDate >= bucket.OverlapFrom &&
                    x.PurchaseDate <= bucket.OverlapTo).Select(x => x.Id).Distinct().CountAsync(ct);
                time.Add(Time(bucket, filled, count));
            }
        }

        var table = request.Definition.Visualization == ReportVisualization.Table
            ? await AcquisitionAmountTableAsync(db, amounts, request, unique, ct) : null;
        var restrictions = multiple
            ? new[] { "ReportRestrictionCurrencySeparated", "ReportRestrictionGroupsNonAdditive" }
            : ["ReportRestrictionCurrencySeparated"];
        return Result(request.Definition, metric, period, local, semantics, totals, groups, time,
            table, unique, Occurrences(groups, time), multiple, multiple, true, restrictions);
    }

    private static async Task<ReportExecutionResult> CapacityAsync(TenantPlatformDbContext db,
        Guid accountId, IQueryable<LeasingFramework> source, ReportExecutionRequest request,
        ReportModuleCatalog catalog, ReportDefinitionSemantics semantics, ReportMetricDefinition metric,
        ReportDateRange period, ReportLocalDate local, CancellationToken ct)
    {
        var query = FrameworkPeriod(source, period);
        var rows = await FrameworkRowsAsync(db, query, MaxCapacityFrameworks, ct);
        var used = await LeasingCapacityQuery.UsedByFrameworkAsync(db, accountId,
            rows.Select(x => x.Framework.Id).ToArray(), ct);
        var capacity = rows.Select(x => new CapacityRow(x,
            CapacityValue(used.GetValueOrDefault(x.Framework.Id), metric.AmountComponent))).ToList();
        var totals = Values(capacity);
        var groups = new List<ReportGroupResult>();
        var multiple = false;
        if (request.Definition.Grouping is ReportDimensionGroupingKey dimension)
        {
            var expansion = await CapacityDimensionExpansionAsync(db, accountId, capacity, dimension, catalog, ct);
            multiple = expansion.Multiple;
            groups.AddRange(expansion.Memberships.GroupBy(x => x.Group)
                .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
                .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label,
                    Values(x.Select(y => y.Row)),
                    x.Select(y => y.Row.Header.Framework.Id).Distinct().Count())));
        }
        else if (request.Definition.Grouping is not null)
        {
            groups.AddRange(capacity.GroupBy(x => FrameworkGroup(x.Header, request.Definition.Grouping))
                .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
                .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label, Values(x),
                    x.Select(y => y.Header.Framework.Id).Distinct().Count())));
        }

        ReportTableResult? table = null;
        if (request.Definition.Visualization == ReportVisualization.Table)
        {
            var page = capacity.OrderBy(x => x.Header.Framework.Name).ThenBy(x => x.Header.Framework.Id)
                .Skip((request.TablePage - 1) * request.TablePageSize).Take(request.TablePageSize)
                .Select(x => (IReportTableRow)new ReportLeasingTableRow(
                    $"leasing-framework:{x.Header.Framework.Id:N}", x.Header.Framework.Id, true, null,
                    x.Header.Framework.Name, x.Header.Framework.Number, x.Header.Owner,
                    $"LeasingStatus{x.Header.Framework.Status}", x.Header.Framework.AcquisitionFrom,
                    x.Header.Framework.AcquisitionTo, x.Value, x.Header.Framework.Currency)).ToList();
            table = new(request.TablePage, request.TablePageSize, capacity.Count, page);
        }
        var restrictions = multiple
            ? new[] { "ReportRestrictionCurrencySeparated", "ReportRestrictionCurrentSnapshot", "ReportRestrictionGroupsNonAdditive" }
            : ["ReportRestrictionCurrencySeparated", "ReportRestrictionCurrentSnapshot"];
        return Result(request.Definition, metric, period, local, semantics, totals, groups, [], table,
            capacity.Count, groups.Sum(x => x.UniqueEntityCount), multiple, multiple, false, restrictions);
    }

    private static IQueryable<AcquisitionAmountRow> ProjectAmounts(
        IQueryable<LeasingAcquisition> query, ReportMetric metric)
    {
        var values = LeasingPurchaseValueQuery.Project(query);
        return metric switch
        {
            ReportMetric.LeasingOriginalPurchaseNet => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.OriginalNet
            }),
            ReportMetric.LeasingOriginalPurchaseVat => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.OriginalVat
            }),
            ReportMetric.LeasingOriginalPurchaseGross => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.OriginalGross
            }),
            ReportMetric.LeasingAdjustedPurchaseNet => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.AdjustedNet
            }),
            ReportMetric.LeasingAdjustedPurchaseVat => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.AdjustedVat
            }),
            ReportMetric.LeasingAdjustedPurchaseGross => values.Select(x => new AcquisitionAmountRow
            {
                Id = x.Acquisition.Id, FrameworkId = x.Acquisition.FrameworkId,
                Name = x.Acquisition.Name, Reference = x.Acquisition.Reference,
                OwnerUserId = x.Acquisition.OwnerUserId, Status = x.Acquisition.Status,
                PurchaseDate = x.Acquisition.PurchaseDate, EndDate = x.Acquisition.EndDate,
                Currency = x.Acquisition.Currency, Value = x.AdjustedGross
            }),
            _ => throw new ReportExecutionException("ReportExecutionUnsupportedMetric")
        };
    }

    private static decimal CapacityValue(LeasingUsedCapacity? used, ReportAmountComponent component) =>
        (used, component) switch
        {
            (null, _) => 0,
            (_, ReportAmountComponent.Net) => used.Net,
            (_, ReportAmountComponent.Vat) => used.Vat,
            (_, ReportAmountComponent.Gross) => used.Gross,
            _ => throw new ReportExecutionException("ReportExecutionUnsupportedMetric")
        };

    private static async Task<List<FrameworkRow>> FrameworkRowsAsync(TenantPlatformDbContext db,
        IQueryable<LeasingFramework> query, int limit, CancellationToken ct)
    {
        var rows = await (from framework in query
            join owner in db.Users.AsNoTracking() on framework.OwnerUserId equals owner.Id
            orderby framework.Id
            select new FrameworkRow { Framework = framework, Owner = owner.FirstName + " " + owner.LastName })
            .Take(limit + 1).ToListAsync(ct);
        if (rows.Count > limit) TooBroad();
        return rows;
    }

    private static async Task<List<AcquisitionRow>> AcquisitionRowsAsync(TenantPlatformDbContext db,
        IQueryable<LeasingAcquisition> query, int limit, CancellationToken ct)
    {
        var rows = await (from acquisition in query
            join owner in db.Users.AsNoTracking() on acquisition.OwnerUserId equals owner.Id
            orderby acquisition.Id
            select new AcquisitionRow { Acquisition = acquisition, Owner = owner.FirstName + " " + owner.LastName })
            .Take(limit + 1).ToListAsync(ct);
        if (rows.Count > limit) TooBroad();
        return rows;
    }

    private static async Task<List<AcquisitionAmountRow>> AmountRowsAsync(
        IQueryable<AcquisitionAmountRow> query, int limit, CancellationToken ct)
    {
        var rows = await query.OrderBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        if (rows.Count > limit) TooBroad();
        return rows;
    }

    private static async Task<IReadOnlyList<ReportGroupResult>> FrameworkCountGroupsAsync(
        TenantPlatformDbContext db, IQueryable<LeasingFramework> query, ReportGroupingKey grouping,
        CancellationToken ct)
    {
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Kind })
        {
            var count = await query.Select(x => x.Id).Distinct().CountAsync(ct);
            return count == 0 ? [] : [new("kind:framework", ReportExecutionLabel.Resource("LeasingFrameworks"), [new(count)], count)];
        }
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Status })
        {
            var rows = await query.GroupBy(x => x.Status)
                .Select(x => new { x.Key, Count = x.Select(y => y.Id).Distinct().Count() }).ToListAsync(ct);
            return rows.OrderBy(x => x.Key).Select(x => new ReportGroupResult($"status:{(int)x.Key}",
                ReportExecutionLabel.Resource($"LeasingStatus{x.Key}"), [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Owner })
        {
            var rows = await (from framework in query
                join owner in db.Users.AsNoTracking() on framework.OwnerUserId equals owner.Id
                group framework by new { framework.OwnerUserId, Owner = owner.FirstName + " " + owner.LastName } into grouped
                select new { grouped.Key.OwnerUserId, grouped.Key.Owner, Count = grouped.Select(x => x.Id).Distinct().Count() })
                .ToListAsync(ct);
            return rows.OrderBy(x => x.Owner).ThenBy(x => x.OwnerUserId).Select(x => new ReportGroupResult(
                $"owner:{x.OwnerUserId:N}", ReportExecutionLabel.Dynamic(x.Owner), [new(x.Count)], x.Count)).ToList();
        }
        throw new ReportExecutionException("ReportExecutionUnsupportedGrouping");
    }

    private static async Task<IReadOnlyList<ReportGroupResult>> AcquisitionCountGroupsAsync(
        TenantPlatformDbContext db, IQueryable<LeasingAcquisition> query, ReportGroupingKey grouping,
        CancellationToken ct)
    {
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Kind })
        {
            var rows = await query.GroupBy(x => x.FrameworkId.HasValue)
                .Select(x => new { HasFramework = x.Key, Count = x.Select(y => y.Id).Distinct().Count() }).ToListAsync(ct);
            return rows.OrderByDescending(x => x.HasFramework).Select(x => new ReportGroupResult(
                x.HasFramework ? "kind:acquisition" : "kind:standalone-acquisition",
                ReportExecutionLabel.Resource(x.HasFramework ? "LeasingAcquisitions" : "LeasingStandaloneAcquisitions"),
                [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Status })
        {
            var rows = await query.GroupBy(x => x.Status)
                .Select(x => new { x.Key, Count = x.Select(y => y.Id).Distinct().Count() }).ToListAsync(ct);
            return rows.OrderBy(x => x.Key).Select(x => new ReportGroupResult($"status:{(int)x.Key}",
                ReportExecutionLabel.Resource($"LeasingStatus{x.Key}"), [new(x.Count)], x.Count)).ToList();
        }
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Owner })
        {
            var rows = await (from acquisition in query
                join owner in db.Users.AsNoTracking() on acquisition.OwnerUserId equals owner.Id
                group acquisition by new { acquisition.OwnerUserId, Owner = owner.FirstName + " " + owner.LastName } into grouped
                select new { grouped.Key.OwnerUserId, grouped.Key.Owner, Count = grouped.Select(x => x.Id).Distinct().Count() })
                .ToListAsync(ct);
            return rows.OrderBy(x => x.Owner).ThenBy(x => x.OwnerUserId).Select(x => new ReportGroupResult(
                $"owner:{x.OwnerUserId:N}", ReportExecutionLabel.Dynamic(x.Owner), [new(x.Count)], x.Count)).ToList();
        }
        throw new ReportExecutionException("ReportExecutionUnsupportedGrouping");
    }

    private static async Task<IReadOnlyList<ReportGroupResult>> AcquisitionAmountGroupsAsync(
        TenantPlatformDbContext db, IQueryable<AcquisitionAmountRow> query,
        ReportGroupingKey grouping, CancellationToken ct)
    {
        List<AmountGroupRow> rows;
        if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Kind })
            rows = await query.GroupBy(x => new { x.FrameworkId.HasValue, x.Currency })
                .Select(x => new AmountGroupRow
                {
                    HasFramework = x.Key.HasValue,
                    Currency = x.Key.Currency, Value = x.Sum(y => y.Value), Count = x.Select(y => y.Id).Distinct().Count()
                }).ToListAsync(ct);
        else if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Status })
            rows = await query.GroupBy(x => new { x.Status, x.Currency })
                .Select(x => new AmountGroupRow
                {
                    Status = x.Key.Status,
                    Currency = x.Key.Currency, Value = x.Sum(y => y.Value), Count = x.Select(y => y.Id).Distinct().Count()
                }).ToListAsync(ct);
        else if (grouping is LeasingReportGroupingKey { Field: LeasingReportGrouping.Owner })
            rows = await (from amount in query
                join owner in db.Users.AsNoTracking() on amount.OwnerUserId equals owner.Id
                group amount by new
                {
                    amount.OwnerUserId,
                    Owner = owner.FirstName + " " + owner.LastName,
                    amount.Currency
                } into grouped
                select new AmountGroupRow
                {
                    OwnerId = grouped.Key.OwnerUserId,
                    LabelText = grouped.Key.Owner,
                    Currency = grouped.Key.Currency,
                    Value = grouped.Sum(x => x.Value),
                    Count = grouped.Select(x => x.Id).Distinct().Count()
                }).ToListAsync(ct);
        else throw new ReportExecutionException("ReportExecutionUnsupportedGrouping");

        return rows.GroupBy(AmountGroup)
            .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
            .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label,
                x.OrderBy(y => y.Currency).Select(y => new ReportExecutionValue(y.Value, y.Currency)).ToList(),
                x.Sum(y => y.Count))).ToList();
    }

    private static GroupIdentity AmountGroup(AmountGroupRow row)
    {
        if (row.OwnerId.HasValue)
            return new($"owner:{row.OwnerId.Value:N}", ReportExecutionLabel.Dynamic(row.LabelText!));
        if (row.HasFramework.HasValue)
            return row.HasFramework.Value
                ? new("kind:acquisition", ReportExecutionLabel.Resource("LeasingAcquisitions"))
                : new("kind:standalone-acquisition", ReportExecutionLabel.Resource("LeasingStandaloneAcquisitions"));
        if (row.Status.HasValue)
            return new($"status:{(int)row.Status.Value}",
                ReportExecutionLabel.Resource($"LeasingStatus{row.Status.Value}"));
        throw new InvalidOperationException();
    }

    private static async Task<IReadOnlyList<ReportExecutionValue>> AmountValuesAsync(
        IQueryable<AcquisitionAmountRow> query, CancellationToken ct) =>
        await query.GroupBy(x => x.Currency).OrderBy(x => x.Key)
            .Select(x => new ReportExecutionValue(x.Sum(y => y.Value), x.Key)).ToListAsync(ct);

    private static async Task<ReportTableResult> FrameworkTableAsync(TenantPlatformDbContext db,
        IQueryable<LeasingFramework> query, ReportExecutionRequest request, int total, CancellationToken ct)
    {
        var source = await (from framework in query
            join owner in db.Users.AsNoTracking() on framework.OwnerUserId equals owner.Id
            orderby framework.Name, framework.Id
            select new
            {
                framework.Id, framework.Name, framework.Number, framework.Status,
                framework.AcquisitionFrom, framework.AcquisitionTo,
                Owner = owner.FirstName + " " + owner.LastName
            })
            .Skip((request.TablePage - 1) * request.TablePageSize).Take(request.TablePageSize).ToListAsync(ct);
        var rows = source.Select(x => (IReportTableRow)new ReportLeasingTableRow(
            $"leasing-framework:{x.Id:N}", x.Id, true, null, x.Name, x.Number, x.Owner,
            $"LeasingStatus{x.Status}", x.AcquisitionFrom, x.AcquisitionTo)).ToList();
        return new(request.TablePage, request.TablePageSize, total, rows);
    }

    private static async Task<ReportTableResult> AcquisitionTableAsync(TenantPlatformDbContext db,
        IQueryable<LeasingAcquisition> query, ReportExecutionRequest request, int total, CancellationToken ct)
    {
        var source = await (from acquisition in query
            join owner in db.Users.AsNoTracking() on acquisition.OwnerUserId equals owner.Id
            orderby acquisition.PurchaseDate, acquisition.Name, acquisition.Id
            select new
            {
                acquisition.Id, acquisition.FrameworkId, acquisition.Name, acquisition.Reference,
                acquisition.Status, acquisition.PurchaseDate, acquisition.EndDate,
                Owner = owner.FirstName + " " + owner.LastName
            })
            .Skip((request.TablePage - 1) * request.TablePageSize).Take(request.TablePageSize).ToListAsync(ct);
        var rows = source.Select(x => (IReportTableRow)new ReportLeasingTableRow(
            $"leasing-acquisition:{x.Id:N}", x.Id, false, x.FrameworkId, x.Name, x.Reference,
            x.Owner, $"LeasingStatus{x.Status}", x.PurchaseDate, x.EndDate)).ToList();
        return new(request.TablePage, request.TablePageSize, total, rows);
    }

    private static async Task<ReportTableResult> AcquisitionAmountTableAsync(TenantPlatformDbContext db,
        IQueryable<AcquisitionAmountRow> query, ReportExecutionRequest request, int total,
        CancellationToken ct)
    {
        var source = await (from amount in query
            join owner in db.Users.AsNoTracking() on amount.OwnerUserId equals owner.Id
            orderby amount.PurchaseDate, amount.Name, amount.Id
            select new
            {
                amount.Id, amount.FrameworkId, amount.Name, amount.Reference, amount.Status,
                amount.PurchaseDate, amount.EndDate, amount.Value, amount.Currency,
                Owner = owner.FirstName + " " + owner.LastName
            })
            .Skip((request.TablePage - 1) * request.TablePageSize).Take(request.TablePageSize).ToListAsync(ct);
        var rows = source.Select(x => (IReportTableRow)new ReportLeasingTableRow(
            $"leasing-acquisition:{x.Id:N}", x.Id, false, x.FrameworkId, x.Name, x.Reference,
            x.Owner, $"LeasingStatus{x.Status}", x.PurchaseDate, x.EndDate, x.Value, x.Currency)).ToList();
        return new(request.TablePage, request.TablePageSize, total, rows);
    }

    private static async Task<DimensionExpansion<FrameworkRow>> FrameworkDimensionExpansionAsync(
        TenantPlatformDbContext db, Guid accountId, IReadOnlyCollection<FrameworkRow> rows,
        ReportDimensionGroupingKey grouping, ReportModuleCatalog catalog, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Framework.Id).ToArray();
        var assignments = await db.LeasingFrameworkDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.FrameworkId) &&
                x.DimensionId == grouping.DimensionId)
            .Select(x => new Assignment(x.FrameworkId, x.ValueId)).Distinct().ToListAsync(ct);
        return Expand(rows, x => x.Framework.Id, assignments, grouping, catalog);
    }

    private static async Task<DimensionExpansion<AcquisitionRow>> AcquisitionDimensionExpansionAsync(
        TenantPlatformDbContext db, Guid accountId, IReadOnlyCollection<AcquisitionRow> rows,
        ReportDimensionGroupingKey grouping, ReportModuleCatalog catalog, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Acquisition.Id).ToArray();
        var assignments = await db.LeasingAcquisitionDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.AcquisitionId) &&
                x.DimensionId == grouping.DimensionId)
            .Select(x => new Assignment(x.AcquisitionId, x.ValueId)).Distinct().ToListAsync(ct);
        return Expand(rows, x => x.Acquisition.Id, assignments, grouping, catalog);
    }

    private static async Task<DimensionExpansion<AcquisitionAmountRow>> AmountDimensionExpansionAsync(
        TenantPlatformDbContext db, Guid accountId, IReadOnlyCollection<AcquisitionAmountRow> rows,
        ReportDimensionGroupingKey grouping, ReportModuleCatalog catalog, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var assignments = await db.LeasingAcquisitionDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.AcquisitionId) &&
                x.DimensionId == grouping.DimensionId)
            .Select(x => new Assignment(x.AcquisitionId, x.ValueId)).Distinct().ToListAsync(ct);
        return Expand(rows, x => x.Id, assignments, grouping, catalog);
    }

    private static async Task<DimensionExpansion<CapacityRow>> CapacityDimensionExpansionAsync(
        TenantPlatformDbContext db, Guid accountId, IReadOnlyCollection<CapacityRow> rows,
        ReportDimensionGroupingKey grouping, ReportModuleCatalog catalog, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Header.Framework.Id).ToArray();
        var assignments = await db.LeasingFrameworkDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.FrameworkId) &&
                x.DimensionId == grouping.DimensionId)
            .Select(x => new Assignment(x.FrameworkId, x.ValueId)).Distinct().ToListAsync(ct);
        return Expand(rows, x => x.Header.Framework.Id, assignments, grouping, catalog);
    }

    private static DimensionExpansion<T> Expand<T>(IReadOnlyCollection<T> rows, Func<T, Guid> id,
        IReadOnlyCollection<Assignment> assignments, ReportDimensionGroupingKey grouping,
        ReportModuleCatalog catalog)
    {
        var definition = catalog.DimensionFilters.Dimensions.Single(x => x.DimensionId == grouping.DimensionId);
        var values = definition.Values.ToDictionary(x => x.Id);
        var byRecord = assignments.GroupBy(x => x.RecordId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ValueId).Where(values.ContainsKey).Distinct().ToList());
        var memberships = new List<Membership<T>>();
        var multiple = false;
        foreach (var row in rows)
        {
            var selected = byRecord.GetValueOrDefault(id(row), []);
            if (selected.Count > 1) multiple = true;
            if (selected.Count == 0)
            {
                memberships.Add(new(row, new($"dimension:{grouping.DimensionId:N}:unspecified",
                    ReportExecutionLabel.Resource("DashboardUnspecified"))));
                continue;
            }
            foreach (var valueId in selected)
                memberships.Add(new(row, new($"dimension:{grouping.DimensionId:N}:{valueId:N}",
                    ReportExecutionLabel.Dynamic(values[valueId].Path))));
        }
        return new(memberships, multiple);
    }

    private static IReadOnlyList<ReportGroupResult> CountGroups<T>(IEnumerable<Membership<T>> memberships)
        where T : class => memberships.GroupBy(x => x.Group)
            .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
            .Select(x => new ReportGroupResult(x.Key.Key, x.Key.Label,
                [new(x.Select(y => RecordId(y.Row)).Distinct().Count())],
                x.Select(y => RecordId(y.Row)).Distinct().Count())).ToList();

    private static Guid RecordId<T>(T row) => row switch
    {
        FrameworkRow framework => framework.Framework.Id,
        AcquisitionRow acquisition => acquisition.Acquisition.Id,
        _ => throw new InvalidOperationException()
    };

    private static GroupIdentity FrameworkGroup(FrameworkRow row, ReportGroupingKey grouping) => grouping switch
    {
        LeasingReportGroupingKey { Field: LeasingReportGrouping.Kind } =>
            new("kind:framework", ReportExecutionLabel.Resource("LeasingFrameworks")),
        LeasingReportGroupingKey { Field: LeasingReportGrouping.Status } =>
            new($"status:{(int)row.Framework.Status}", ReportExecutionLabel.Resource($"LeasingStatus{row.Framework.Status}")),
        LeasingReportGroupingKey { Field: LeasingReportGrouping.Owner } =>
            new($"owner:{row.Framework.OwnerUserId:N}", ReportExecutionLabel.Dynamic(row.Owner)),
        _ => throw new ReportExecutionException("ReportExecutionUnsupportedGrouping")
    };

    private static IReadOnlyList<ReportExecutionValue> Values(IEnumerable<AcquisitionAmountRow> rows) =>
        rows.GroupBy(x => x.Currency).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ReportExecutionValue(x.Sum(y => y.Value), x.Key)).ToList();

    private static IReadOnlyList<ReportExecutionValue> Values(IEnumerable<CapacityRow> rows) =>
        rows.GroupBy(x => x.Header.Framework.Currency).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ReportExecutionValue(x.Sum(y => y.Value), x.Key)).ToList();

    private static ReportTimeResult Time(ReportTimeBucket bucket,
        IReadOnlyList<ReportExecutionValue> values, int count) => new(bucket.Key.MachineKey,
        bucket.Key.Year, bucket.Key.Bucket, bucket.FullFrom, bucket.FullTo,
        bucket.OverlapFrom, bucket.OverlapTo, values, count);

    private static int Occurrences(IReadOnlyCollection<ReportGroupResult> groups,
        IReadOnlyCollection<ReportTimeResult> time) =>
        groups.Count > 0 ? groups.Sum(x => x.UniqueEntityCount) : time.Sum(x => x.UniqueEntityCount);

    private static ReportExecutionResult Result(ReportCardDefinition definition,
        ReportMetricDefinition metric, ReportDateRange period, ReportLocalDate local,
        ReportDefinitionSemantics semantics, IReadOnlyList<ReportExecutionValue> totals,
        IReadOnlyList<ReportGroupResult> groups, IReadOnlyList<ReportTimeResult> time,
        ReportTableResult? table, int uniqueEntities, int groupOccurrences, bool actualMultiple,
        bool nonAdditiveGroups, bool emptyBuckets, IReadOnlyList<string> restrictions)
    {
        var currencies = totals.Where(x => x.Currency is not null).Select(x => x.Currency!).ToArray();
        var validation = ReportResultValidator.Validate(definition, metric,
            new(currencies, true,
                totals.Any(x => x.Value < 0) || groups.SelectMany(x => x.Values).Any(x => x.Value < 0),
                actualMultiple, totals.Count > 0));
        if (!validation.IsValid) throw new ReportDefinitionValidationException(validation.Issues);
        var multiple = actualMultiple || semantics.MultipleGroupMembership;
        return new(definition.Module, metric.Metric, period, local.Today, local.TimeZoneId,
            totals, groups, time, table,
            new(semantics.Grain, metric.AmountComponent == ReportAmountComponent.None
                    ? ReportValueUnit.Count : ReportValueUnit.Amount,
                semantics.CurrencyRule, semantics.Additivity,
                nonAdditiveGroups || semantics.UniqueTotalRequired,
                !nonAdditiveGroups && semantics.GroupTotalsAdditive,
                multiple, emptyBuckets && definition.TimeGrouping.HasValue,
                uniqueEntities, groupOccurrences, semantics.SourceRule, restrictions));
    }

    private static void TooBroad() => throw new ReportExecutionException("ReportExecutionTooBroad");

    private sealed class FrameworkRow
    {
        public required LeasingFramework Framework { get; init; }
        public required string Owner { get; init; }
    }

    private sealed class AcquisitionRow
    {
        public required LeasingAcquisition Acquisition { get; init; }
        public required string Owner { get; init; }
    }

    private sealed class AcquisitionAmountRow
    {
        public Guid Id { get; init; }
        public Guid? FrameworkId { get; init; }
        public required string Name { get; init; }
        public required string Reference { get; init; }
        public Guid OwnerUserId { get; init; }
        public LeasingAcquisitionStatus Status { get; init; }
        public DateOnly PurchaseDate { get; init; }
        public DateOnly EndDate { get; init; }
        public required string Currency { get; init; }
        public decimal Value { get; init; }
    }

    private sealed class AmountGroupRow
    {
        public string? LabelText { get; init; }
        public Guid? OwnerId { get; init; }
        public bool? HasFramework { get; init; }
        public LeasingAcquisitionStatus? Status { get; init; }
        public required string Currency { get; init; }
        public decimal Value { get; init; }
        public int Count { get; init; }
    }

    private sealed record CapacityRow(FrameworkRow Header, decimal Value);
    private sealed record Assignment(Guid RecordId, Guid ValueId);
    private sealed record GroupIdentity(string Key, ReportExecutionLabel Label);
    private sealed record Membership<T>(T Row, GroupIdentity Group);
    private sealed record DimensionExpansion<T>(IReadOnlyList<Membership<T>> Memberships, bool Multiple);
}
