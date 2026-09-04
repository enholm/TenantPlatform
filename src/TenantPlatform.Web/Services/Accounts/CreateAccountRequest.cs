namespace TenantPlatform.Web.Services.Accounts;

public class CreateAccountRequest
{
    public string Name { get; init; } = string.Empty;

    public string DefaultLanguage { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

