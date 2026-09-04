namespace TenantPlatform.Web.Services.Accounts;

public class AccountDetailsDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DefaultLanguage { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}

