namespace TenantPlatform.Web.Security.Authorization;

public sealed class NavigationPermissions
{
    public bool CanSeeTenantPortal { get; init; }

    public bool CanSeeProviderPortal { get; init; }

    public bool CanSeePropertyManagement { get; init; }

    public bool CanSeeAdministration { get; init; }

    public bool CanSeePlatformAdministration { get; init; }

    public bool CanSeeMyRequests { get; init; }

    public bool CanSeeAssignedRequests { get; init; }

    public bool CanSeeAllRequests { get; init; }

    public bool CanSeeBuildings { get; init; }

    public bool CanSeeUnits { get; init; }

    public bool CanSeeOccupancies { get; init; }

    public bool CanSeeOrganizations { get; init; }

    public bool CanSeeUsers { get; init; }

    public bool CanSeeServiceDefinitions { get; init; }

    public bool CanSeeAccounts { get; init; }
}

