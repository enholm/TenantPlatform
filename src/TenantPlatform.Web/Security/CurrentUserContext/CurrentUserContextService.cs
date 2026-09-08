using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace TenantPlatform.Web.Security.CurrentUserContext;

public class CurrentUserContextService : CircuitHandler, ICurrentUserContextService, IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private Task<AuthenticationState>? _authenticationState;

    public CurrentUserContextService(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public override Task OnCircuitOpenedAsync(
        Circuit circuit,
        CancellationToken cancellationToken)
    {
        _authenticationStateProvider.AuthenticationStateChanged += AuthenticationStateChanged;
        _authenticationState = _authenticationStateProvider.GetAuthenticationStateAsync();
        return Task.CompletedTask;
    }

    private void AuthenticationStateChanged(Task<AuthenticationState> authenticationState)
    {
        _authenticationState = authenticationState;
    }

    public void Dispose()
    {
        _authenticationStateProvider.AuthenticationStateChanged -= AuthenticationStateChanged;
    }

    public CurrentUserContext Current => BuildCurrentUserContext();

    private CurrentUserContext BuildCurrentUserContext()
    {
        // A circuit must use its own authentication state, not the opening HTTP request.
        // Fail closed while a replacement authentication state is pending or faulted.
        var authenticationState = _authenticationState;
        var principal = authenticationState is null
            ? _httpContextAccessor.HttpContext?.User
            : authenticationState.IsCompletedSuccessfully
                ? authenticationState.Result.User
                : null;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new CurrentUserContext();
        }

        var userIdValue = principal.FindFirstValue(
            TenantPlatformClaimTypes.UserId);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return new CurrentUserContext();
        }

        var isPlatformAdmin =
            bool.TryParse(
                principal.FindFirstValue(
                    TenantPlatformClaimTypes.IsPlatformAdmin),
                out var parsedIsPlatformAdmin)
            && parsedIsPlatformAdmin;

        return new CurrentUserContext
        {
            IsAuthenticated = true,

            UserId = userId,

            Email =
                principal.FindFirstValue(
                    ClaimTypes.Email) ?? string.Empty,

            FullName =
                principal.FindFirstValue(
                    ClaimTypes.Name) ?? string.Empty,

            IsPlatformAdmin =
                isPlatformAdmin,

            CurrentAccountId =
                Guid.TryParse(
                    principal.FindFirstValue(
                        TenantPlatformClaimTypes.CurrentAccountId),
                    out var accountId)
                        ? accountId
                        : null
        };
    }
}
