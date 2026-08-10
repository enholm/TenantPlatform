using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authentication;

public class LocalAuthenticationService : ILocalAuthenticationService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration =
        TimeSpan.FromMinutes(15);

    private readonly TenantPlatformDbContext _dbContext;
    private readonly PasswordService _passwordService;

    public LocalAuthenticationService(
        TenantPlatformDbContext dbContext,
        PasswordService passwordService)
    {
        _dbContext = dbContext;
        _passwordService = passwordService;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var loginAccount = await _dbContext.LoginAccounts
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Email.ToLower() == normalizedEmail,
                cancellationToken);

        if (loginAccount is null)
        {
            return AuthenticationResult.Failure(
                "Invalid email or password.");
        }

        if (!loginAccount.IsEnabled)
        {
            return AuthenticationResult.Failure(
                "This account is disabled.");
        }

        var now = DateTimeOffset.UtcNow;

        if (loginAccount.LockedUntilUtc.HasValue &&
            loginAccount.LockedUntilUtc.Value > now)
        {
            return AuthenticationResult.Failure(
                "This account is temporarily locked.");
        }

        var passwordValid =
            _passwordService.VerifyPassword(
                loginAccount,
                password);

        if (!passwordValid)
        {
            loginAccount.FailedLoginCount++;

            if (loginAccount.FailedLoginCount >= MaxFailedLoginAttempts)
            {
                loginAccount.LockedUntilUtc =
                    now.Add(LockoutDuration);

                loginAccount.FailedLoginCount = 0;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return AuthenticationResult.Failure(
                "Invalid email or password.");
        }

        loginAccount.FailedLoginCount = 0;
        loginAccount.LockedUntilUtc = null;
        loginAccount.LastLoginUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AuthenticationResult.Success(loginAccount.User);
    }
}