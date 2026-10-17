using TenantPlatform.Core.Dimensions;
using TenantPlatform.Application.Dimensions;

namespace TenantPlatform.Web.Services.Dimensions;

public sealed class DimensionService(TenantPlatform.Application.Dimensions.DimensionService service)
{
    public Task<DimensionRegister> GetAsync(CancellationToken ct = default) => service.GetAsync(ct);
    public Task<Guid> SaveDimensionAsync(Guid? id, Dimension input, CancellationToken ct = default) => service.SaveDimensionAsync(id, input, ct);
    public Task<Guid> SaveValueAsync(Guid dimensionId, Guid? id, DimensionValue input, CancellationToken ct = default) => service.SaveValueAsync(dimensionId, id, input, ct);
}
