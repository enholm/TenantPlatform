using System.Security.Claims;

namespace TenantPlatform.Web.Security.CurrentUserContext;

public class CurrentUserContextService : ICurrentUserContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    private CurrentUserContext? _currentUser;

    public CurrentUserContextService(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUserContext Current
    {
        get
        {
            _currentUser ??= BuildCurrentUserContext();

            return _currentUser;
        }
    }

    private CurrentUserContext BuildCurrentUserContext()
    {
        var principal =
            _httpContextAccessor.HttpContext?.User;

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
