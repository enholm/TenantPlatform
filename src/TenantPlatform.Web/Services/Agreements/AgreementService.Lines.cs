using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task<AgreementLinesDto> GetLinesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default) => financial.GetLinesAsync(accountId, agreementId, ct);
    public Task<Guid> SaveLineAsync(Guid accountId, Guid agreementId, Guid? lineId, SaveAgreementLineRequest r, CancellationToken ct = default) => financial.SaveLineAsync(accountId, agreementId, lineId, r, ct);
    public Task CloseLineAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, bool deactivate, string reason, CancellationToken ct = default) => financial.CloseLineAsync(accountId, agreementId, lineId, revision, date, deactivate, reason, ct);
    public Task<AgreementForecastDto> ForecastAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default) => financial.ForecastAsync(accountId, agreementId, from, to, ct);
}
