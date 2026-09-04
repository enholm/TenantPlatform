namespace TenantPlatform.Web.Services.UserAdministration;

public class UpdateUserRequest
{
    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PreferredLanguage { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool? IsPlatformAdmin { get; init; }

    public bool? IsLocalLoginEnabled { get; init; }
}

