using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

namespace TenantPlatform.Web.Services.Reports;

public sealed class LeasingReportTreeModule(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization,
    IStringLocalizer<TenantPlatformResources> localizer)
{
    internal async Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, ct);
        var name = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.Name).SingleAsync(ct);
        return new(Account(accountId), name, null, await scope.Acquisitions.AnyAsync(ct), null);
    }

    internal async Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, ReportTreeNodeId parentId,
        int offset, int pageSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, ct);
        if (parentId == Account(accountId)) return await RootChildrenAsync(db, accountId, scope.Acquisitions,
            parentId, offset, pageSize, ct);
        if (parentId.Type == ReportTreeNodeType.LeasingFramework && parentId.EntityId.HasValue)
        {
            var query = scope.Acquisitions.Where(x => x.FrameworkId == parentId.EntityId.Value);
            if (!await query.AnyAsync(ct)) throw new ReportTreeException("ReportTreeNodeUnavailable");
            return await AcquisitionsAsync(db, accountId, query, parentId, offset, pageSize, ct);
        }
        if (parentId.Type == ReportTreeNodeType.Synthetic &&
            parentId.Synthetic == ReportTreeSyntheticNode.LeasingWithoutFramework)
        {
            var query = scope.Acquisitions.Where(x => x.FrameworkId == null);
            if (!await query.AnyAsync(ct)) throw new ReportTreeException("ReportTreeNodeUnavailable");
            return await AcquisitionsAsync(db, accountId, query, parentId, offset, pageSize, ct);
        }
        if (parentId.Type == ReportTreeNodeType.LeasingAcquisition && parentId.EntityId.HasValue)
        {
            if (!await scope.Acquisitions.AnyAsync(x => x.Id == parentId.EntityId.Value, ct))
                throw new ReportTreeException("ReportTreeNodeUnavailable");
            var query = db.LeasingItems.AsNoTracking().Where(x => x.AccountId == accountId &&
                x.AcquisitionId == parentId.EntityId.Value);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.Position).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.Description, x.ItemNumber,
                    HasChildren = db.LeasingEquipment.Any(e => e.AccountId == accountId && e.ItemId == x.Id) })
                .ToListAsync(ct);
            return Page(rows.Select(x => new ReportTreeNode(Item(x.Id), x.Description,
                x.ItemNumber, x.HasChildren, parentId)).ToList(), offset, total, pageSize);
        }
        if (parentId.Type == ReportTreeNodeType.LeasingItem && parentId.EntityId.HasValue)
        {
            var acquisitionIds = scope.Acquisitions.Select(x => x.Id);
            var item = await db.LeasingItems.AsNoTracking().Where(x => x.AccountId == accountId &&
                    x.Id == parentId.EntityId.Value && acquisitionIds.Contains(x.AcquisitionId))
                .Select(x => new { x.Id, x.AcquisitionId }).SingleOrDefaultAsync(ct)
                ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var query = db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == accountId && x.ItemId == item.Id &&
                x.AcquisitionId == item.AcquisitionId);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.Description).ThenBy(x => x.SerialNumber).ThenBy(x => x.Id)
                .Skip(offset).Take(pageSize).ToListAsync(ct);
            return Page(rows.Select(x => new ReportTreeNode(Equipment(x.Id), x.Description,
                x.SerialNumber ?? x.InternalId, false, parentId)).ToList(), offset, total, pageSize);
        }
        throw new ReportTreeException("ReportTreeNodeUnavailable");
    }

    internal async Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId,
        ReportCardDefinition definition, ReportModuleCatalog catalog, ReportDateRange period,
        string query, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, ct);
        var term = query.ToLowerInvariant();
        var accountName = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.Name).SingleAsync(ct);
        var root = new ReportTreePathNode(Account(accountId), accountName);
        var relevant = scope.Acquisitions;
        var parentIds = relevant.Where(x => x.FrameworkId.HasValue).Select(x => x.FrameworkId!.Value).Distinct();
        var frameworks = await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == accountId &&
                parentIds.Contains(x.Id) && (x.Name.ToLower().Contains(term) || x.Number.ToLower().Contains(term)))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(limit)
            .Select(x => new { x.Id, x.Name, x.Number }).ToListAsync(ct);
        var acquisitions = await relevant.Where(x => x.Name.ToLower().Contains(term) || x.Reference.ToLower().Contains(term))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(limit)
            .Select(x => new { x.Id, x.Name, x.Reference, x.FrameworkId }).ToListAsync(ct);
        var acquisitionIds = relevant.Select(x => x.Id);
        var items = await db.LeasingItems.AsNoTracking().Where(x => x.AccountId == accountId &&
                acquisitionIds.Contains(x.AcquisitionId) && (x.Description.ToLower().Contains(term) ||
                    (x.ItemNumber ?? "").ToLower().Contains(term)))
            .OrderBy(x => x.Description).ThenBy(x => x.Id).Take(limit)
            .Select(x => new { x.Id, x.Description, x.ItemNumber, x.AcquisitionId }).ToListAsync(ct);
        var equipment = await db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == accountId &&
                acquisitionIds.Contains(x.AcquisitionId) && (x.Description.ToLower().Contains(term) ||
                    (x.SerialNumber ?? "").ToLower().Contains(term) || (x.InternalId ?? "").ToLower().Contains(term)))
            .OrderBy(x => x.Description).ThenBy(x => x.Id).Take(limit)
            .Select(x => new { x.Id, x.Description, x.SerialNumber, x.InternalId, x.ItemId, x.AcquisitionId }).ToListAsync(ct);

        var neededAcquisitions = acquisitions.Select(x => x.Id).Concat(items.Select(x => x.AcquisitionId))
            .Concat(equipment.Select(x => x.AcquisitionId)).Distinct().ToArray();
        var acquisitionMap = await relevant.Where(x => neededAcquisitions.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.FrameworkId }).ToDictionaryAsync(x => x.Id, ct);
        var neededFrameworks = acquisitionMap.Values.Where(x => x.FrameworkId.HasValue)
            .Select(x => x.FrameworkId!.Value).Distinct().ToArray();
        var frameworkMap = await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == accountId &&
                neededFrameworks.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var itemIds = equipment.Select(x => x.ItemId).Distinct().ToArray();
        var itemMap = await db.LeasingItems.AsNoTracking().Where(x => x.AccountId == accountId && itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Description, ct);

        IReadOnlyList<ReportTreePathNode> AcquisitionPath(Guid acquisitionId)
        {
            var acquisition = acquisitionMap[acquisitionId];
            return acquisition.FrameworkId.HasValue
                ? [root, new(Framework(acquisition.FrameworkId.Value), frameworkMap[acquisition.FrameworkId.Value])]
                : [root, new(Standalone(), localizer["ReportTreeWithoutFramework"].Value)];
        }
        var hits = new List<ReportTreeSearchHit>();
        hits.AddRange(frameworks.Select(x => new ReportTreeSearchHit(Framework(x.Id),
            ReportTreeNodeType.LeasingFramework, x.Name, x.Number, [root])));
        hits.AddRange(acquisitions.Select(x => new ReportTreeSearchHit(Acquisition(x.Id),
            ReportTreeNodeType.LeasingAcquisition, x.Name, x.Reference, AcquisitionPath(x.Id))));
        hits.AddRange(items.Select(x => new ReportTreeSearchHit(Item(x.Id), ReportTreeNodeType.LeasingItem,
            x.Description, acquisitionMap[x.AcquisitionId].Name,
            AcquisitionPath(x.AcquisitionId).Append(new(Acquisition(x.AcquisitionId), acquisitionMap[x.AcquisitionId].Name)).ToList())));
        hits.AddRange(equipment.Select(x => new ReportTreeSearchHit(Equipment(x.Id), ReportTreeNodeType.LeasingEquipment,
            x.Description, x.SerialNumber ?? x.InternalId,
            AcquisitionPath(x.AcquisitionId)
                .Append(new(Acquisition(x.AcquisitionId), acquisitionMap[x.AcquisitionId].Name))
                .Append(new(Item(x.ItemId), itemMap[x.ItemId])).ToList())));
        return hits.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Id.Key, StringComparer.Ordinal).Take(limit).ToList();
    }

    internal async Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, ReportTreeNodeId nodeId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, ct);
        var accountName = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.Name).SingleAsync(ct);
        var root = new ReportTreePathNode(Account(accountId), accountName);
        if (nodeId == Account(accountId))
            return new(new(nodeId, accountName, null, await scope.Acquisitions.AnyAsync(ct), null), [],
                [new("ReportTreeFieldPeriod", $"{period.From:d}–{period.To:d}")], null);
        if (nodeId.Type == ReportTreeNodeType.Synthetic && nodeId == Standalone())
        {
            if (!await scope.Acquisitions.AnyAsync(x => x.FrameworkId == null, ct))
                throw new ReportTreeException("ReportTreeNodeUnavailable");
            return new(new(nodeId, localizer["ReportTreeWithoutFramework"], null, true, Account(accountId)), [root], [], null);
        }
        var relevant = scope.Acquisitions;
        if (nodeId.Type == ReportTreeNodeType.LeasingFramework && nodeId.EntityId.HasValue)
        {
            var id = nodeId.EntityId.Value;
            if (!await relevant.AnyAsync(x => x.FrameworkId == id, ct)) throw new ReportTreeException("ReportTreeNodeUnavailable");
            var row = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Id == id, ct);
            var authorizedFrameworks = await AuthorizedFrameworks(db, accountId, ct);
            var canOpen = await authorizedFrameworks.AnyAsync(x => x.Id == id, ct);
            return new(new(nodeId, row.Name, row.Number, true, Account(accountId)), [root],
                [new("Reference", row.Number), new("Status", ValueResourceKey: $"LeasingStatus{row.Status}"),
                 new("PaymentFrom", row.AcquisitionFrom.ToString("d")), new("PaymentTo", row.AcquisitionTo.ToString("d"))],
                canOpen ? $"/leasing/frameworks/{id}" : null);
        }
        if (nodeId.Type == ReportTreeNodeType.LeasingAcquisition && nodeId.EntityId.HasValue)
        {
            var row = await relevant.SingleOrDefaultAsync(x => x.Id == nodeId.EntityId.Value, ct)
                ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var (parent, ancestors) = await ParentAsync(db, accountId, row.FrameworkId, root, ct);
            return new(new(nodeId, row.Name, row.Reference,
                    await db.LeasingItems.AnyAsync(x => x.AccountId == accountId && x.AcquisitionId == row.Id, ct), parent),
                ancestors, [new("Reference", row.Reference),
                    new("Status", ValueResourceKey: $"LeasingStatus{row.Status}"),
                    new("LeasingPurchaseDate", row.PurchaseDate.ToString("d")),
                    new("AgreementEndDate", row.EndDate.ToString("d")), new("LeasingCurrency", row.Currency)],
                $"/leasing/acquisitions/{row.Id}");
        }
        if (nodeId.Type == ReportTreeNodeType.LeasingItem && nodeId.EntityId.HasValue)
        {
            var acquisitionIds = relevant.Select(x => x.Id);
            var row = await db.LeasingItems.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId &&
                x.Id == nodeId.EntityId.Value && acquisitionIds.Contains(x.AcquisitionId), ct)
                ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var acquisition = await relevant.SingleAsync(x => x.Id == row.AcquisitionId, ct);
            var (_, ancestors) = await ParentAsync(db, accountId, acquisition.FrameworkId, root, ct);
            var acquisitionPath = ancestors.Append(new ReportTreePathNode(Acquisition(acquisition.Id), acquisition.Name)).ToList();
            return new(new(nodeId, row.Description, row.ItemNumber,
                    await db.LeasingEquipment.AnyAsync(x => x.AccountId == accountId && x.ItemId == row.Id, ct), Acquisition(acquisition.Id)),
                acquisitionPath, [new("Reference", row.ItemNumber ?? "—"), new("LeasingQuantity", row.Quantity.ToString("N2"))],
                $"/leasing/acquisitions/{acquisition.Id}");
        }
        if (nodeId.Type == ReportTreeNodeType.LeasingEquipment && nodeId.EntityId.HasValue)
        {
            var acquisitionIds = relevant.Select(x => x.Id);
            var row = await db.LeasingEquipment.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId &&
                x.Id == nodeId.EntityId.Value && acquisitionIds.Contains(x.AcquisitionId), ct)
                ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var acquisition = await relevant.SingleAsync(x => x.Id == row.AcquisitionId, ct);
            var itemName = await db.LeasingItems.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == row.ItemId &&
                x.AcquisitionId == acquisition.Id).Select(x => x.Description).SingleAsync(ct);
            var (_, ancestors) = await ParentAsync(db, accountId, acquisition.FrameworkId, root, ct);
            var path = ancestors.Append(new(Acquisition(acquisition.Id), acquisition.Name))
                .Append(new(Item(row.ItemId), itemName)).ToList();
            return new(new(nodeId, row.Description, row.SerialNumber ?? row.InternalId, false, Item(row.ItemId)), path,
                [new("LifeSerial", row.SerialNumber ?? "—"), new("LifeInternalId", row.InternalId ?? "—"),
                 new("Status", ValueResourceKey: $"LifeEquipment{row.Status}"),
                 new("LifeRegisteredDate", row.RegisteredDate.ToString("d"))],
                $"/leasing/equipment?acquisition={row.AcquisitionId}");
        }
        throw new ReportTreeException("ReportTreeNodeUnavailable");
    }

    private async Task<LeasingListScopes> ScopeAsync(TenantPlatformDbContext db, Guid accountId,
        ReportCardDefinition definition, ReportModuleCatalog catalog, ReportDateRange period, CancellationToken ct)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !await db.UserAccounts.AsNoTracking().AnyAsync(x => x.AccountId == accountId &&
                x.UserId == current.UserId && x.User.IsActive, ct) || !await authorization.CanUseLeasingAsync(ct))
            throw new UnauthorizedAccessException();
        var admin = await authorization.CanCreateAgreementAsync(ct);
        var frameworkReader = admin || await authorization.CanChangeLeasingLimitAsync(ct);
        var scopes = new LeasingListScopes(
            LeasingListQuery.AuthorizedFrameworks(db, accountId, current.UserId, frameworkReader).AsNoTracking(),
            LeasingListQuery.AuthorizedAcquisitions(db, accountId, current.UserId, admin).AsNoTracking());
        var filter = LeasingReportModule.Filters(definition.Filters);
        scopes = LeasingListQuery.ApplyKind(scopes, filter.Kind);
        var selections = definition.Filters.OfType<ReportDimensionFilterSelection>().Select(x =>
            new DimensionListFilterSelection { DimensionId = x.DimensionKey.DimensionId,
                ValueId = x.ValueId, IncludeUnspecified = x.IncludeUnspecified }).ToList();
        scopes = LeasingListQuery.ApplyDimensions(db, accountId, scopes,
            DimensionListFilters.Plan(catalog.DimensionFilters, selections));
        scopes = LeasingListQuery.ApplyRecordFilters(scopes, filter);
        return scopes with { Acquisitions = LeasingReportModule.AcquisitionPeriod(scopes.Acquisitions, period) };
    }

    private async Task<IQueryable<LeasingFramework>> AuthorizedFrameworks(TenantPlatformDbContext db,
        Guid accountId, CancellationToken ct)
    {
        var current = userContext.Current;
        var admin = await authorization.CanCreateAgreementAsync(ct);
        var reader = admin || await authorization.CanChangeLeasingLimitAsync(ct);
        return LeasingListQuery.AuthorizedFrameworks(db, accountId, current.UserId, reader).AsNoTracking();
    }

    private async Task<ReportTreeNodePage> RootChildrenAsync(TenantPlatformDbContext db, Guid accountId,
        IQueryable<LeasingAcquisition> relevant, ReportTreeNodeId parent, int offset, int pageSize, CancellationToken ct)
    {
        var parentIds = relevant.Where(x => x.FrameworkId.HasValue).Select(x => x.FrameworkId!.Value).Distinct();
        var frameworks = db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == accountId && parentIds.Contains(x.Id));
        var frameworkCount = await frameworks.CountAsync(ct);
        var standalone = await relevant.AnyAsync(x => x.FrameworkId == null, ct);
        var total = frameworkCount + (standalone ? 1 : 0);
        var items = await frameworks.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
            .Select(x => new ReportTreeNode(Framework(x.Id), x.Name, x.Number, true, parent)).ToListAsync(ct);
        if (standalone && offset <= frameworkCount && offset + pageSize > frameworkCount)
            items.Add(new(Standalone(), localizer["ReportTreeWithoutFramework"], null, true, parent));
        return Page(items, offset, total, pageSize);
    }

    private static async Task<ReportTreeNodePage> AcquisitionsAsync(TenantPlatformDbContext db, Guid accountId,
        IQueryable<LeasingAcquisition> query, ReportTreeNodeId parent, int offset, int pageSize, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
            .Select(x => new { x.Id, x.Name, x.Reference,
                HasChildren = db.LeasingItems.Any(i => i.AccountId == accountId && i.AcquisitionId == x.Id) })
            .ToListAsync(ct);
        return Page(rows.Select(x => new ReportTreeNode(Acquisition(x.Id), x.Name, x.Reference,
            x.HasChildren, parent)).ToList(), offset, total, pageSize);
    }

    private async Task<(ReportTreeNodeId Parent, IReadOnlyList<ReportTreePathNode> Ancestors)> ParentAsync(
        TenantPlatformDbContext db, Guid accountId, Guid? frameworkId, ReportTreePathNode root, CancellationToken ct)
    {
        if (!frameworkId.HasValue)
        {
            var synthetic = Standalone();
            return (synthetic, [root, new(synthetic, localizer["ReportTreeWithoutFramework"])]);
        }
        var name = await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == frameworkId.Value)
            .Select(x => x.Name).SingleAsync(ct);
        var framework = Framework(frameworkId.Value);
        return (framework, [root, new(framework, name)]);
    }

    private static ReportTreeNodePage Page(IReadOnlyList<ReportTreeNode> items, int offset, int total, int size) =>
        new(items, offset, total, offset + items.Count < total ? offset + items.Count : null);
    private static ReportTreeNodeId Account(Guid id) => new(ReportModule.Leasing, ReportTreeNodeType.Account, id);
    private static ReportTreeNodeId Framework(Guid id) => new(ReportModule.Leasing, ReportTreeNodeType.LeasingFramework, id);
    private static ReportTreeNodeId Acquisition(Guid id) => new(ReportModule.Leasing, ReportTreeNodeType.LeasingAcquisition, id);
    private static ReportTreeNodeId Item(Guid id) => new(ReportModule.Leasing, ReportTreeNodeType.LeasingItem, id);
    private static ReportTreeNodeId Equipment(Guid id) => new(ReportModule.Leasing, ReportTreeNodeType.LeasingEquipment, id);
    private static ReportTreeNodeId Standalone() => new(ReportModule.Leasing, ReportTreeNodeType.Synthetic,
        Synthetic: ReportTreeSyntheticNode.LeasingWithoutFramework);
}
