using System.Security.Claims;

namespace TenantPlatform.Web.Security;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    private CurrentUser? _currentUser;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser CurrentUser
    {
        get
        {
            _currentUser ??= BuildCurrentUser();

            return _currentUser;
        }
    }

    private CurrentUser BuildCurrentUser()
    {
        var principal =
            _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new CurrentUser();
        }

        var userIdValue = principal.FindFirstValue(
            TenantPlatformClaimTypes.UserId);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return new CurrentUser();
        }

        return new CurrentUser
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
