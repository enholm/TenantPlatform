using Microsoft.AspNetCore.Identity;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Authentication;

public class PasswordService
{
    private readonly PasswordHasher<LoginAccount> _passwordHasher = new();

    public string HashPassword(
        LoginAccount loginAccount,
        string password)
    {
        return _passwordHasher.HashPassword(
            loginAccount,
            password);
    }

    public bool VerifyPassword(
        LoginAccount loginAccount,
        string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(
            loginAccount,
            loginAccount.PasswordHash,
            password);

        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}