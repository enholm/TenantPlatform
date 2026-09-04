namespace TenantPlatform.Web.Services.UserAdministration;

public class UserAccountOptionDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public class UserOrganizationOptionDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public class UserBuildingOptionDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

