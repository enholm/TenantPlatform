using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.UserAdministration;

public class AddUserRoleRequest
{
    public Guid AccountId { get; init; }

    public UserRole Role { get; init; }

    public Guid? OrganizationId { get; init; }

    public Guid? BuildingId { get; init; }
}

