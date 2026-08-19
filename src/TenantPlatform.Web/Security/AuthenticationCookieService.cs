using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TenantPlatform.Core.Identity;
using Microsoft.AspNetCore.Localization;
using TenantPlatform.Core.Localization;


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

    public void SetCulture(
    HttpContext httpContext,
    string? cultureName)
    {
        var culture =
            cultureName is SupportedLanguages.EnGb or SupportedLanguages.NbNo or SupportedLanguages.SvSe
                ? cultureName
                : SupportedLanguages.NbNo;

        httpContext.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });
    }
}
