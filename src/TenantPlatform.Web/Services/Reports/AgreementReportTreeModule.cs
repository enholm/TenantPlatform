using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Reports;

public sealed class AgreementReportTreeModule(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization)
{
    internal async Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, DateOnly today, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, today, ct);
        var name = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => x.Name).SingleAsync(ct);
        var id = Account(accountId);
        return new(id, name, null, await scope.AnyAsync(ct), null);
    }

    internal async Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, DateOnly today, ReportTreeNodeId parentId,
        int offset, int pageSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, today, ct);
        if (parentId == Account(accountId))
        {
            var total = await scope.CountAsync(ct);
            var rows = await scope.OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id)
                .Skip(offset).Take(pageSize)
                .Select(x => new
                {
                    x.Agreement.Id, x.Agreement.Title, x.Counterparty,
                    HasChildren = db.AgreementLines.Any(line => line.AccountId == accountId && line.AgreementId == x.Agreement.Id)
                }).ToListAsync(ct);
            return Page(rows.Select(x => new ReportTreeNode(Agreement(x.Id), x.Title, x.Counterparty,
                x.HasChildren, parentId)).ToList(), offset, total, pageSize);
        }
        if (parentId.Type != ReportTreeNodeType.Agreement || !parentId.EntityId.HasValue ||
            !await scope.AnyAsync(x => x.Agreement.Id == parentId.EntityId.Value, ct))
            throw new ReportTreeException("ReportTreeNodeUnavailable");
        var agreementId = parentId.EntityId.Value;
        var latest = LatestLines(db, accountId).Where(x => x.AgreementId == agreementId);
        var count = await latest.CountAsync(ct);
        var lines = await latest.OrderBy(x => x.Name).ThenBy(x => x.LineId).Skip(offset).Take(pageSize)
            .Select(x => new { x.LineId, x.Name, x.Status }).ToListAsync(ct);
        return Page(lines.Select(x => new ReportTreeNode(Line(x.LineId), x.Name,
            null, false, parentId)).ToList(), offset, count, pageSize);
    }

    internal async Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId,
        ReportCardDefinition definition, ReportModuleCatalog catalog, ReportDateRange period,
        DateOnly today, string query, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, today, ct);
        var term = query.ToLowerInvariant();
        var account = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => x.Name).SingleAsync(ct);
        var agreements = await scope.Where(x => x.Agreement.Title.ToLower().Contains(term) ||
                x.Counterparty.ToLower().Contains(term))
            .OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id).Take(limit)
            .Select(x => new { x.Agreement.Id, x.Agreement.Title, x.Counterparty }).ToListAsync(ct);
        var visibleIds = scope.Select(x => x.Agreement.Id);
        var lines = await LatestLines(db, accountId)
            .Where(x => visibleIds.Contains(x.AgreementId) &&
                (x.Name.ToLower().Contains(term) || (x.Description ?? "").ToLower().Contains(term)))
            .OrderBy(x => x.Name).ThenBy(x => x.LineId).Take(limit)
            .Select(x => new { x.LineId, x.Name, x.AgreementId }).ToListAsync(ct);
        var agreementNames = await scope.Where(x => lines.Select(y => y.AgreementId).Contains(x.Agreement.Id))
            .Select(x => new { x.Agreement.Id, x.Agreement.Title }).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        var rootPath = new ReportTreePathNode(Account(accountId), account);
        return agreements.Select(x => new ReportTreeSearchHit(Agreement(x.Id), ReportTreeNodeType.Agreement,
                x.Title, x.Counterparty, [rootPath]))
            .Concat(lines.Select(x => new ReportTreeSearchHit(Line(x.LineId), ReportTreeNodeType.AgreementLine,
                x.Name, agreementNames[x.AgreementId],
                [rootPath, new(Agreement(x.AgreementId), agreementNames[x.AgreementId])])))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id.Key, StringComparer.Ordinal)
            .Take(limit).ToList();
    }

    internal async Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
        ReportModuleCatalog catalog, ReportDateRange period, DateOnly today, ReportTreeNodeId nodeId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await ScopeAsync(db, accountId, definition, catalog, period, today, ct);
        var accountName = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => x.Name).SingleAsync(ct);
        var root = new ReportTreePathNode(Account(accountId), accountName);
        if (nodeId == Account(accountId))
            return new(new(nodeId, accountName, null, await scope.AnyAsync(ct), null), [],
                [new("ReportTreeFieldPeriod", $"{period.From:d}–{period.To:d}")], null);
        if (nodeId.Type == ReportTreeNodeType.Agreement && nodeId.EntityId.HasValue)
        {
            var row = await scope.Where(x => x.Agreement.Id == nodeId.EntityId.Value)
                .Select(x => new { x.Agreement, x.Counterparty, x.Owner }).SingleOrDefaultAsync(ct)
                ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var hasChildren = await db.AgreementLines.AnyAsync(x => x.AccountId == accountId && x.AgreementId == row.Agreement.Id, ct);
            return new(new(nodeId, row.Agreement.Title, row.Counterparty, hasChildren, Account(accountId)), [root],
                [new("AgreementType", ValueResourceKey: $"AgreementType{row.Agreement.Type}"),
                 new("Status", ValueResourceKey: $"AgreementStatus{row.Agreement.Status}"),
                 new("AgreementOwner", row.Owner), new("AgreementStartDate", row.Agreement.StartDate.ToString("d")),
                 new("AgreementEndDate", row.Agreement.EndDate?.ToString("d") ?? "—")],
                $"/agreements/{row.Agreement.Id}");
        }
        if (nodeId.Type == ReportTreeNodeType.AgreementLine && nodeId.EntityId.HasValue)
        {
            var visibleIds = scope.Select(x => x.Agreement.Id);
            var row = await LatestLines(db, accountId).Where(x => x.LineId == nodeId.EntityId.Value &&
                    visibleIds.Contains(x.AgreementId))
                .Select(x => new { Line = x, Agreement = db.Agreements.Where(a => a.AccountId == accountId && a.Id == x.AgreementId).Select(a => a.Title).Single() })
                .SingleOrDefaultAsync(ct) ?? throw new ReportTreeException("ReportTreeNodeUnavailable");
            var agreementId = row.Line.AgreementId;
            return new(new(nodeId, row.Line.Name, row.Agreement, false, Agreement(agreementId)),
                [root, new(Agreement(agreementId), row.Agreement)],
                [new("Status", ValueResourceKey: $"ReportTreeAgreementLineStatus{row.Line.Status}"),
                 new("AgreementStartDate", row.Line.StartDate.ToString("d")),
                 new("AgreementEndDate", row.Line.EndDate?.ToString("d") ?? "—")],
                $"/agreements/{agreementId}/lines");
        }
        throw new ReportTreeException("ReportTreeNodeUnavailable");
    }

    private async Task<IQueryable<AgreementListQueryRow>> ScopeAsync(TenantPlatformDbContext db,
        Guid accountId, ReportCardDefinition definition, ReportModuleCatalog catalog, ReportDateRange period,
        DateOnly today, CancellationToken ct)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !await db.UserAccounts.AsNoTracking().AnyAsync(x => x.AccountId == accountId &&
                x.UserId == current.UserId && x.User.IsActive, ct) ||
            !await authorization.CanUseAgreementsAsync(ct)) throw new UnauthorizedAccessException();
        var admin = await authorization.CanCreateAgreementAsync(ct);
        var filter = AgreementReportModule.Filters(definition.Filters);
        var source = AgreementListQuery.Authorized(db, accountId, current.UserId, admin).AsNoTracking();
        source = AgreementListQuery.ApplyAttention(source, filter.Attention, filter.Attention.HasValue ? today : null);
        var selections = definition.Filters.OfType<ReportDimensionFilterSelection>().Select(x =>
            new DimensionListFilterSelection { DimensionId = x.DimensionKey.DimensionId,
                ValueId = x.ValueId, IncludeUnspecified = x.IncludeUnspecified }).ToList();
        source = AgreementListQuery.ApplyDimensions(db, accountId, source,
            DimensionListFilters.Plan(catalog.DimensionFilters, selections));
        return AgreementReportModule.Overlap(AgreementListQuery.ApplyRecordFilters(
            AgreementListQuery.Project(db, accountId, source), filter), period);
    }

    private static IQueryable<AgreementLineVersion> LatestLines(TenantPlatformDbContext db, Guid accountId) =>
        db.AgreementLineVersions.AsNoTracking().Where(x => x.AccountId == accountId &&
            !db.AgreementLineVersions.Any(newer => newer.AccountId == accountId &&
                newer.LineId == x.LineId && newer.Sequence > x.Sequence));

    private static ReportTreeNodePage Page(IReadOnlyList<ReportTreeNode> items, int offset, int total, int size) =>
        new(items, offset, total, offset + items.Count < total ? offset + items.Count : null);
    private static ReportTreeNodeId Account(Guid id) => new(ReportModule.Agreements, ReportTreeNodeType.Account, id);
    private static ReportTreeNodeId Agreement(Guid id) => new(ReportModule.Agreements, ReportTreeNodeType.Agreement, id);
    private static ReportTreeNodeId Line(Guid id) => new(ReportModule.Agreements, ReportTreeNodeType.AgreementLine, id);
}
