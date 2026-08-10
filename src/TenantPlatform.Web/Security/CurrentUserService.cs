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

        return new CurrentUser
        {
            IsAuthenticated = true,

            UserId = Guid.Parse(
                principal.FindFirstValue(
                    TenantPlatformClaimTypes.UserId)!),

            Email =
                principal.FindFirstValue(
                    ClaimTypes.Email)!,

            FullName =
                principal.FindFirstValue(
                    ClaimTypes.Name)!,

            CurrentAccountId =
                Guid.TryParse(
                    principal.FindFirstValue(
                        TenantPlatformClaimTypes.CurrentAccountId),
                    out var id)
                    ? id
                    : null
        };
    }
}
