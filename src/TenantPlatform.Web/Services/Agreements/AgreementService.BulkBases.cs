using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task<List<AgreementBulkBasisOption>> GetBulkBasisOptionsAsync(Guid accountId, CancellationToken ct = default) => financial.GetBulkBasisOptionsAsync(accountId, ct);
    public Task<List<AgreementBulkBasisPreview>> PreviewBulkBasisAsync(Guid accountId, AgreementBulkBasisFilter filter, CancellationToken ct = default) => financial.PreviewBulkBasisAsync(accountId, filter, ct);
    public Task<List<AgreementBulkBasisResult>> GenerateBulkBasisAsync(Guid accountId, DateOnly from, DateOnly to, IReadOnlyList<AgreementBulkBasisSelection> selection, CancellationToken ct = default) => financial.GenerateBulkBasisAsync(accountId, from, to, selection, ct);
}
