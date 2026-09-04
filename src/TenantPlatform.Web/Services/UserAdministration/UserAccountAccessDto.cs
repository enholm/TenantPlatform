using TenantPlatform.Core.Identity;

namespace TenantPlatform.Web.Services.UserAdministration;

public class UserAccountAccessDto
{
    public Guid UserAccountId { get; init; }

    public Guid AccountId { get; init; }

    public string AccountName { get; init; } = string.Empty;

    public List<UserRoleAssignmentDto> Roles { get; init; } = [];
}

public class UserRoleAssignmentDto
{
    public Guid Id { get; init; }

    public UserRole Role { get; init; }

    public Guid? OrganizationId { get; init; }

    public string? OrganizationName { get; init; }

    public Guid? BuildingId { get; init; }

    public string? BuildingName { get; init; }
}

