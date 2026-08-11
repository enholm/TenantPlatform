namespace TenantPlatform.Web.Security;

public class CurrentUser
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public bool IsAuthenticated { get; init; }

    public Guid? CurrentAccountId { get; set; }

    public string? CurrentAccountName { get; init; }
}
