namespace TenantPlatform.Core.Identity;

public class LoginAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public DateTimeOffset? LastLoginUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = null!;
}
