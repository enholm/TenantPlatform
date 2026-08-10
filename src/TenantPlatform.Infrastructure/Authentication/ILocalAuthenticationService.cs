namespace TenantPlatform.Infrastructure.Authentication;

public interface ILocalAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}