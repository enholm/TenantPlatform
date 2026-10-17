using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public Task<AgreementIndexRegister> GetIndicesAsync(Guid accountId, CancellationToken ct = default) => indexService.GetIndicesAsync(accountId, ct);
    public Task<Guid> CreateIndexAsync(Guid accountId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default) =>
        indexService.CreateIndexAsync(accountId, code, name, description, source, resolution, ct);
    public Task UpdateIndexAsync(Guid accountId, Guid indexId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default) =>
        indexService.UpdateIndexAsync(accountId, indexId, code, name, description, source, resolution, ct);
    public Task RecordIndexValueAsync(Guid accountId, Guid indexId, DateOnly period, decimal value, DateOnly? published, string reason, Guid? originalValueId = null, CancellationToken ct = default) =>
        indexService.RecordIndexValueAsync(accountId, indexId, period, value, published, reason, originalValueId, ct);
}
