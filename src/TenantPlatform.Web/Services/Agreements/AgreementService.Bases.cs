using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task<AgreementForecastDto> PreviewBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default) => financial.PreviewBasisAsync(accountId, agreementId, direction, from, to, ct);
    public Task<AgreementBasisRun> GenerateBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default) => financial.GenerateBasisAsync(accountId, agreementId, direction, from, to, ct);
    public Task<List<AgreementBasisSummary>> ListBasisAsync(Guid accountId, AgreementDirection direction, CancellationToken ct = default) => financial.ListBasisAsync(accountId, direction, ct);
    public Task<List<AgreementBasisDetails>> GetAffectedBasesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default) => financial.GetAffectedBasesAsync(accountId, agreementId, ct);
    public Task<AgreementBasisDetails> GetBasisAsync(Guid accountId, Guid basisId, CancellationToken ct = default) => financial.GetBasisAsync(accountId, basisId, ct);
    public Task RegenerateBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default) => financial.RegenerateBasisAsync(accountId, basisId, revision, reason, ct);
    public Task ApproveBasisAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct = default) => financial.ApproveBasisAsync(accountId, basisId, revision, ct);
    public Task CancelBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default) => financial.CancelBasisAsync(accountId, basisId, revision, reason, ct);
    public Task<Guid> CreateCorrectionAsync(Guid accountId, Guid originalId, string reason, CancellationToken ct = default) => financial.CreateCorrectionAsync(accountId, originalId, reason, ct);
}
