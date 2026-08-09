namespace TenantPlatform.Core.Identity;

public class UserRoleAssignment
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? BuildingId { get; set; }

    public UserRole Role { get; set; }
}
