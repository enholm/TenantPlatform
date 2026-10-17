using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class GeographicAreaService(AccountRegisterService registers)
{
    public Task<List<GeographicArea>> GetAllAsync(CancellationToken cancellationToken = default) =>
        registers.ReadGeographicAreasAsync(cancellationToken);
    public Task SaveAsync(Guid? id, AccountRegisterInput input, CancellationToken cancellationToken = default) =>
        registers.SaveAsync(AccountRegisterKind.GeographicAreas, id, input, cancellationToken);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        registers.DeleteAsync(AccountRegisterKind.GeographicAreas, id, cancellationToken);
}
