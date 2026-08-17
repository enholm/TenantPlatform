namespace TenantPlatform.Web.Services.Organizations;

public interface IOrganizationService
{
    Task<List<OrganizationListItemDto>> GetOrganizationsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<OrganizationDetailsDto?> GetOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateOrganizationAsync(
        Guid accountId,
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    Task<OrganizationDeleteCheckResult> CanDeleteOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task DeleteOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

