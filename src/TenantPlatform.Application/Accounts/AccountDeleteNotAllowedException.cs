namespace TenantPlatform.Application.Accounts;

public class AccountDeleteNotAllowedException
    : InvalidOperationException
{
    public AccountDeleteNotAllowedException(
        string message)
        : base(message)
    {
    }
}

