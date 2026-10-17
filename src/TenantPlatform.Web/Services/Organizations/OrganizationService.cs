using TenantPlatform.Application.Organizations;

namespace TenantPlatform.Web.Services.Organizations;

public sealed class OrganizationService(TenantPlatform.Application.Organizations.OrganizationService service) : IOrganizationService
{
    public Task<List<OrganizationListItemDto>> GetOrganizationsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetOrganizationsAsync(accountId, cancellationToken);
    public Task<OrganizationDetailsDto?> GetOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default) => service.GetOrganizationAsync(accountId, organizationId, cancellationToken);
    public Task<Guid> CreateOrganizationAsync(Guid accountId, CreateOrganizationRequest request, CancellationToken cancellationToken = default) => service.CreateOrganizationAsync(accountId, request, cancellationToken);
    public Task UpdateOrganizationAsync(Guid accountId, Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default) => service.UpdateOrganizationAsync(accountId, organizationId, request, cancellationToken);
    public Task DeleteOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default) => service.DeleteOrganizationAsync(accountId, organizationId, cancellationToken);
    public Task<OrganizationDeleteCheckResult> CanDeleteOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default) => service.CanDeleteOrganizationAsync(accountId, organizationId, cancellationToken);
}
