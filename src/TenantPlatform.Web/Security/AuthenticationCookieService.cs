using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Web.Security;

public class AuthenticationCookieService
{
    public async Task SignInAsync(
        HttpContext httpContext,
        User user,
        Guid? currentAccountId,
        bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(
                TenantPlatformClaimTypes.UserId,
                user.Id.ToString()),

            new(
                ClaimTypes.Email,
                user.Email),

            new(
                ClaimTypes.Name,
                $"{user.FirstName} {user.LastName}")
        };

        if (currentAccountId.HasValue)
        {
            claims.Add(
                new Claim(
                    TenantPlatformClaimTypes.CurrentAccountId,
                    currentAccountId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            AllowRefresh = true
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }
}
