using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Security.Authorization;

namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportTreeService(
    ITenantAuthorizationService authorization,
    IReportCatalogService catalogs,
    IReportDefinitionCompatibilityValidator compatibility,
    IReportLocalDateService localDates,
    AgreementReportTreeModule agreements,
    LeasingReportTreeModule leasing) : IReportTreeService
{
    public async Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition,
        CancellationToken ct = default)
    {
        var context = await ValidateAsync(accountId, definition, ct);
        return definition.Module == ReportModule.Agreements
            ? await agreements.GetRootAsync(accountId, definition, context.Catalog, context.Period, context.Today, ct)
            : await leasing.GetRootAsync(accountId, definition, context.Catalog, context.Period, ct);
    }

    public async Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
        ReportTreeNodeId parentId, int offset = 0, int pageSize = 50, CancellationToken ct = default)
    {
        if (offset < 0 || pageSize is < 1 or > 100) throw new ReportTreeException("ReportTreePageInvalid");
        parentId?.Validate();
        if (parentId is null || parentId.Module != definition.Module)
            throw new ReportTreeException("ReportTreeNodeInvalid");
        var context = await ValidateAsync(accountId, definition, ct);
        return definition.Module == ReportModule.Agreements
            ? await agreements.GetChildrenAsync(accountId, definition, context.Catalog, context.Period, context.Today, parentId, offset, pageSize, ct)
            : await leasing.GetChildrenAsync(accountId, definition, context.Catalog, context.Period, parentId, offset, pageSize, ct);
    }

    public async Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId,
        ReportCardDefinition definition, string query, int limit = 25, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200 || limit is < 1 or > 50)
            throw new ReportTreeException("ReportTreeSearchInvalid");
        var context = await ValidateAsync(accountId, definition, ct);
        return definition.Module == ReportModule.Agreements
            ? await agreements.SearchAsync(accountId, definition, context.Catalog, context.Period, context.Today, query.Trim(), limit, ct)
            : await leasing.SearchAsync(accountId, definition, context.Catalog, context.Period, query.Trim(), limit, ct);
    }

    public async Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
        ReportTreeNodeId nodeId, CancellationToken ct = default)
    {
        nodeId?.Validate();
        if (nodeId is null || nodeId.Module != definition.Module)
            throw new ReportTreeException("ReportTreeNodeInvalid");
        var context = await ValidateAsync(accountId, definition, ct);
        return definition.Module == ReportModule.Agreements
            ? await agreements.GetNodeAsync(accountId, definition, context.Catalog, context.Period, context.Today, nodeId, ct)
            : await leasing.GetNodeAsync(accountId, definition, context.Catalog, context.Period, nodeId, ct);
    }

    private async Task<TreeContext> ValidateAsync(Guid accountId, ReportCardDefinition definition,
        CancellationToken ct)
    {
        if (definition is null || !Enum.IsDefined(definition.Module))
            throw new ReportTreeException("ReportTreeDefinitionInvalid");
        var hasAccess = definition.Module switch
        {
            ReportModule.Agreements => await authorization.CanUseAgreementsAsync(ct),
            ReportModule.Leasing => await authorization.CanUseLeasingAsync(ct),
            _ => false
        };
        if (!hasAccess) throw new UnauthorizedAccessException();
        var catalog = await catalogs.GetAsync(accountId, definition.Module, ct);
        compatibility.Validate(definition, catalog);
        if (definition.Visualization != ReportVisualization.RelationshipTree)
            throw new ReportTreeException("ReportTreeDefinitionInvalid");
        var local = await localDates.GetAsync(accountId, ct);
        return new(catalog, ReportPeriodResolver.Resolve(definition.Period, local.Today), local.Today);
    }

    private sealed record TreeContext(ReportModuleCatalog Catalog, ReportDateRange Period, DateOnly Today);
}
