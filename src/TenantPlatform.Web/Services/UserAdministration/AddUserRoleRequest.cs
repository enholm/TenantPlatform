using TenantPlatform.Core.Identity;

namespace TenantPlatform.Web.Services.UserAdministration;

public class AddUserRoleRequest
{
    public Guid AccountId { get; init; }

    public UserRole Role { get; init; }

    public Guid? OrganizationId { get; init; }

    public Guid? BuildingId { get; init; }
}

