using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task RecordTerminationAsync(Guid accountId, Guid agreementId, AgreementTerminationRequest request, CancellationToken cancellationToken = default) =>
        followupWriter.RecordTerminationAsync(accountId, agreementId, request, cancellationToken);
}
