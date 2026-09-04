namespace TenantPlatform.Web.Services.Accounts;

public class AccountDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static AccountDeleteCheckResult Allowed()
    {
        return new AccountDeleteCheckResult
        {
            CanDelete = true
        };
    }

    public static AccountDeleteCheckResult NotAllowed(
        string reason)
    {
        return new AccountDeleteCheckResult
        {
            CanDelete = false,
            Reason = reason
        };
    }
}

