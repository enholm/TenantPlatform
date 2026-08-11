namespace TenantPlatform.Infrastructure.Authentication;

public interface ILocalAuthenticationService
{
    Task<LoginResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}