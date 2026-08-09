namespace TenantPlatform.Core.Identity;

public class UserAccountRole
{
    public Guid Id { get; set; }

    public Guid UserAccountId { get; set; }

    public UserRole Role { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? BuildingId { get; set; }

    public UserAccount UserAccount { get; set; } = null!;
}