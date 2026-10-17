using TenantPlatform.Application.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public Task<AgreementAttentionDto> GetAttentionAsync(Guid accountId, CancellationToken ct = default) =>
        agreementAttention.GetAttentionAsync(accountId, ct);

    public Task<AgreementDashboardList> GetDashboardContractsAsync(Guid accountId, CancellationToken ct = default) =>
        agreementDashboard.GetDashboardContractsAsync(accountId, ct);

    public Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightDimension dimension,
        Guid? selected = null, CancellationToken ct = default) =>
        agreementInsights.GetInsightsAsync(accountId, dimension, selected, ct);
}
