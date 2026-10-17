using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task<AgreementBasisApprovalList> ListPendingBasisApprovalsAsync(Guid accountId, AgreementBasisApprovalFilter filter, CancellationToken ct = default) => financial.ListPendingBasisApprovalsAsync(accountId, filter, ct);
    public Task<List<AgreementBasisApprovalResult>> ApproveBasesAsync(Guid accountId, IReadOnlyList<AgreementBasisApprovalSelection> selection, CancellationToken ct = default) => financial.ApproveBasesAsync(accountId, selection, ct);
}
