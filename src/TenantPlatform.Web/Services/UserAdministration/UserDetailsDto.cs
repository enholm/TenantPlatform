namespace TenantPlatform.Web.Services.UserAdministration;

public class UserDetailsDto
{
    public Guid Id { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PreferredLanguage { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsPlatformAdmin { get; init; }

    public LocalLoginDto? LocalLogin { get; init; }

    public List<UserAccountAccessDto> Accounts { get; init; } = [];
}

public class LocalLoginDto
{
    public string Email { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public int FailedLoginCount { get; init; }

    public DateTimeOffset? LockedUntilUtc { get; init; }

    public DateTimeOffset? LastLoginUtc { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
}

