using Microsoft.AspNetCore.Authentication;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.OAuth;

namespace TenantPlatform.Web.Security;

public static class AuthenticationEndpoints
{
    public static void MapTenantLogin(this WebApplication app, OAuthIssuerOptions oauthOptions)
    {
        app.MapPost("/auth/login", async (HttpContext httpContext, AuthenticationSessionService sessions,
            AuthenticationCookieService cookies, ITenantAuthorizationService navigation, IFormCollection form, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(form["oauth"]))
            {
                if (!oauthOptions.Enabled) return Results.BadRequest();
                var interactions = httpContext.RequestServices.GetRequiredService<OAuthInteractions>();
                var returnPath = interactions.LoginReturn(form["oauth"]);
                if (returnPath is null) return Results.BadRequest();
                var user = await httpContext.RequestServices.GetRequiredService<OAuthAccessService>()
                    .LoginAsync(form["email"].ToString(), form["password"].ToString(), ct);
                if (user is null) return Results.LocalRedirect("/login?error=invalid-login&oauth=" + Uri.EscapeDataString(form["oauth"].ToString()));
                await cookies.SignInAsync(httpContext, user, null, string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase));
                cookies.SetCulture(httpContext, user.PreferredLanguage);
                return Results.LocalRedirect(returnPath);
            }
            AuthenticationSession session;
            try { session = await sessions.LoginAsync(form["email"].ToString(), form["password"].ToString(), ct); }
            catch (UnauthorizedAccessException) { return Results.Redirect("/login?error=invalid-login"); }
            if (session.User is null) return Results.Redirect(session.Error == "no-account-access" ? "/login?error=no-account-access" : "/login?error=invalid-login");
            var remember = string.Equals(form["rememberMe"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            await cookies.SignInAsync(httpContext, session.User, session.AccountId, remember);
            cookies.SetCulture(httpContext, session.User.PreferredLanguage);
            if (!session.AccountId.HasValue) return Results.Redirect(session.SelectAccountRequired ? "/select-account" : "/accounts");
            return Results.Redirect(navigation.GetStartPage(session.User.IsPlatformAdmin, session.Roles));
        });
    }
}
