namespace TenantPlatform.Web.Services.Accounts;

public class AccountDeleteNotAllowedException
    : InvalidOperationException
{
    public AccountDeleteNotAllowedException(
        string message)
        : base(message)
    {
    }
}

