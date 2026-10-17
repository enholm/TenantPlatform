using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class OrganizationElementService(AccountRegisterService registers)
{
    public Task<List<OrganizationElement>> GetAllAsync(CancellationToken cancellationToken = default) =>
        registers.ReadOrganizationElementsAsync(cancellationToken);
    public Task SaveAsync(Guid? id, AccountRegisterInput input, CancellationToken cancellationToken = default) =>
        registers.SaveAsync(AccountRegisterKind.OrganizationElements, id, input, cancellationToken);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        registers.DeleteAsync(AccountRegisterKind.OrganizationElements, id, cancellationToken);
}
