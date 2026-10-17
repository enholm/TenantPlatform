using TenantPlatform.Application.Security;
using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Application.Organizations;

public sealed record OrganizationReferences(bool Occupancies, bool Networks, bool Requests, bool UserRoles, bool Agreements);
public interface IOrganizationData
{
    Task<List<OrganizationListItemDto>> ReadListAsync(Guid accountId, CancellationToken cancellationToken);
    Task<OrganizationDetailsDto?> ReadDetailsAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken);
    Task<IOrganizationTransaction> BeginAsync(Guid accountId, Guid? id, CancellationToken ct);
}
public interface IOrganizationTransaction : IAsyncDisposable
{
    Organization Organization { get; }
    Task<OrganizationReferences> ReadReferencesAsync(CancellationToken ct);
    Task CommitAsync(bool delete, CancellationToken ct);
}

public sealed class OrganizationService(IAuthorizationService authorization, IOrganizationData data) : IOrganizationService
{
    private async Task Require(Guid account, string permission, Guid? id, CancellationToken ct)
    {
        if (!await authorization.CanAsync(permission, new(id.HasValue ? ResourceKind.Organization : ResourceKind.Account, id, account), ct))
            throw new UnauthorizedAccessException();
    }

    public async Task<List<OrganizationListItemDto>> GetOrganizationsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        // PropertyAdmin's existing broad organization-read exception also supports occupancy selectors.
        await authorization.GetOrganizationListAccessAsync(accountId, cancellationToken);
        return await data.ReadListAsync(accountId, cancellationToken);
    }

    public async Task<OrganizationDetailsDto?> GetOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Organizations.Read, organizationId, cancellationToken);
        return await data.ReadDetailsAsync(accountId, organizationId, cancellationToken);
    }

    public async Task<Guid> CreateOrganizationAsync(Guid accountId, CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var input = new Organization { Name = request.Name.Trim(), OrganizationNumber = string.IsNullOrWhiteSpace(request.OrganizationNumber) ? null : request.OrganizationNumber.Trim(),
            Type = request.Type, IsActive = request.IsActive };
        await Require(accountId, Permissions.Organizations.Create, null, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, null, cancellationToken);
        await Require(accountId, Permissions.Organizations.Create, null, cancellationToken);
        Apply(transaction.Organization, input);
        await transaction.CommitAsync(false, cancellationToken);
        return transaction.Organization.Id;
    }

    public async Task UpdateOrganizationAsync(Guid accountId, Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var input = new Organization { Name = request.Name.Trim(), OrganizationNumber = string.IsNullOrWhiteSpace(request.OrganizationNumber) ? null : request.OrganizationNumber.Trim(),
            Type = request.Type, IsActive = request.IsActive };
        await Require(accountId, Permissions.Organizations.Update, organizationId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, organizationId, cancellationToken);
        await Require(accountId, Permissions.Organizations.Update, organizationId, cancellationToken);
        Apply(transaction.Organization, input);
        await transaction.CommitAsync(false, cancellationToken);
    }

    public async Task<OrganizationDeleteCheckResult> CanDeleteOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Organizations.Delete, organizationId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, organizationId, cancellationToken);
        return CanDelete(await transaction.ReadReferencesAsync(cancellationToken));
    }

    public async Task DeleteOrganizationAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Organizations.Delete, organizationId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, organizationId, cancellationToken);
        await Require(accountId, Permissions.Organizations.Delete, organizationId, cancellationToken);
        var check = CanDelete(await transaction.ReadReferencesAsync(cancellationToken));
        if (!check.CanDelete) throw new OrganizationDeleteNotAllowedException(check.Reason ?? "OrganizationCannotBeDeleted");
        await transaction.CommitAsync(true, cancellationToken);
    }

    private static OrganizationDeleteCheckResult CanDelete(OrganizationReferences references) =>
        references.Occupancies ? OrganizationDeleteCheckResult.NotAllowed("OrganizationContainsOccupancies") :
        references.Networks ? OrganizationDeleteCheckResult.NotAllowed("OrganizationContainsNetworkSsids") :
        references.Requests ? OrganizationDeleteCheckResult.NotAllowed("OrganizationContainsServiceRequests") :
        references.UserRoles ? OrganizationDeleteCheckResult.NotAllowed("OrganizationContainsUserRoles") :
        references.Agreements ? OrganizationDeleteCheckResult.NotAllowed("AgreementReferenced") : OrganizationDeleteCheckResult.Allowed();

    private static void Apply(Organization target, Organization input)
    {
        target.Name = input.Name; target.OrganizationNumber = input.OrganizationNumber;
        target.Type = input.Type; target.IsActive = input.IsActive;
    }
}
