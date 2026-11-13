using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Security.Authorization;

namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportExecutionService(
    ITenantAuthorizationService authorization,
    IReportCatalogService catalogs,
    IReportDefinitionCompatibilityValidator compatibility,
    IReportLocalDateService localDates,
    AgreementReportModule agreements,
    LeasingReportModule leasing) : IReportExecutionService
{
    public async Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request,
        CancellationToken ct = default)
    {
        if (request?.Definition is null)
            throw new ReportDefinitionValidationException(
                [new ReportValidationIssue("definition", "ReportDefinitionMetricInvalid")]);
        if (request.TablePage < 1 || request.TablePageSize is < 1 or > 100)
            throw new ReportExecutionException("ReportExecutionInvalidPage");
        var hasAccess = request.Definition.Module switch
        {
            ReportModule.Agreements => await authorization.CanUseAgreementsAsync(ct),
            ReportModule.Leasing => await authorization.CanUseLeasingAsync(ct),
            _ => false
        };
        if (!hasAccess) throw new UnauthorizedAccessException();

        var catalog = await catalogs.GetAsync(accountId, request.Definition.Module, ct);
        var semantics = compatibility.Validate(request.Definition, catalog);
        if (request.Definition.Visualization == ReportVisualization.RelationshipTree)
            throw new ReportExecutionException("ReportExecutionUnsupportedVisualization");
        var local = await localDates.GetAsync(accountId, ct);
        var period = ReportPeriodResolver.Resolve(request.Definition.Period, local.Today);
        return request.Definition.Module switch
        {
            ReportModule.Agreements => await agreements.ExecuteAsync(
                accountId, request, catalog, semantics, period, local, ct),
            ReportModule.Leasing => await leasing.ExecuteAsync(
                accountId, request, catalog, semantics, period, local, ct),
            _ => throw new ReportExecutionException("ReportExecutionUnsupportedModule")
        };
    }
}
