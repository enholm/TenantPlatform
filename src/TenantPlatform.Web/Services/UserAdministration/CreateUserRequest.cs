namespace TenantPlatform.Web.Services.UserAdministration;

public class CreateUserRequest
{
    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PreferredLanguage { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    public bool CreateLocalLogin { get; init; } = true;

    public bool IsLocalLoginEnabled { get; init; } = true;

    public string? TemporaryPassword { get; init; }

    public Guid? AccountId { get; init; }
}

