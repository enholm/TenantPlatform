using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Web.Components.Shared.OAuth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.OAuth;

public static class OAuthEndpoints
{
    private const string Scheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;
    private static readonly HashSet<string> AuthorizationParameters = new(StringComparer.Ordinal)
    {
        "client_id", "redirect_uri", "response_type", "response_mode", "scope", "state", "resource",
        "code_challenge", "code_challenge_method", "interaction", "decision", "__RequestVerificationToken"
    };
    public static void MapTenantOAuth(this WebApplication app, OAuthIssuerOptions options)
    {
        if (!options.Enabled) return;
        app.MapMethods("/connect/authorize", ["GET", "POST"], Authorize);
        app.MapPost("/connect/token", Exchange);
        app.MapGet("/oauth/connections", async (HttpContext http, OAuthAccessService access, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                return (IResult)new RazorComponentResult<OAuthConnections>(new { Connections = await access.ListAsync(ct),
                    VerificationToken = antiforgery.GetAndStoreTokens(http).RequestToken });
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme });
        app.MapPost("/oauth/connections/revoke", async (HttpContext http, OAuthAccessService access, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                await antiforgery.ValidateRequestAsync(http);
                var form = await http.Request.ReadFormAsync(ct);
                await access.RevokeAsync(form["connection"].ToString(), ct);
                return Results.LocalRedirect("/oauth/connections");
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme });
    }

    private static bool ValidResource(OpenIddictRequest request, OAuthIssuerOptions options) =>
        request.GetResources().SequenceEqual([options.Resource], StringComparer.Ordinal);
    private static IResult Error(string error) => Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error }), [Scheme]);

    private static async Task<IResult> Authorize(HttpContext http, OAuthIssuerOptions options, OAuthInteractions interactions,
        OAuthAccessService access, IOpenIddictApplicationManager clients, IAntiforgery antiforgery, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var request = http.GetOpenIddictServerRequest()!;
        if (request.GetParameters().Any(x => !AuthorizationParameters.Contains(x.Key)) ||
            !ValidResource(request, options) || !request.HasScope(OAuthIssuerOptions.ReadScope) ||
            request.GetScopes().Any(x => x != OAuthIssuerOptions.ReadScope && x != Scopes.OfflineAccess && x != TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage)) return Error(Errors.InvalidRequest);
        var cookie = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!cookie.Succeeded)
        {
            if (http.Request.Method != "GET") return Results.Unauthorized();
            return Results.LocalRedirect("/login?oauth=" + Uri.EscapeDataString(interactions.Create(request, null)));
        }
        http.User = cookie.Principal!;
        Guid user;
        try { user = await access.RequireUserAsync(ct); }
        catch (UnauthorizedAccessException) { return Error(Errors.AccessDenied); }
        if (HttpMethods.IsGet(http.Request.Method))
        {
            var client = await clients.FindByClientIdAsync(request.ClientId!, ct);
            return new RazorComponentResult<OAuthConsent>(new
            {
                ClientName = (await clients.GetDisplayNameAsync(client!, ct)) ?? request.ClientId!,
                Interaction = interactions.Create(request, user), Parameters = request.GetParameters().ToDictionary(x => x.Key, x => x.Value.ToString()),
                PagesAllowed = request.HasScope(TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage),
                RefreshAllowed = request.HasScope(Scopes.OfflineAccess), VerificationToken = antiforgery.GetAndStoreTokens(http).RequestToken
            });
        }
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(); }
        var form = await http.Request.ReadFormAsync(ct);
        var interaction = interactions.Read(form["interaction"]);
        if (interaction?.UserId != user || !interactions.Matches(interaction, request)) return Results.BadRequest();
        if (form["decision"] != "approve") return Error(Errors.AccessDenied);
        OAuthConnection grant;
        try { grant = await access.ApproveAsync(request.ClientId!, request.HasScope(Scopes.OfflineAccess), ct, request.HasScope(TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage)); }
        catch (UnauthorizedAccessException) { return Error(Errors.AccessDenied); }
        var principal = new ClaimsPrincipal(new ClaimsIdentity("TenantPlatform.OAuth"));
        principal.SetClaim(Claims.Subject, user.ToString("D"));
        principal.SetScopes(request.GetScopes());
        principal.SetResources(options.Resource);
        principal.SetAuthorizationId(grant.Id);
        return Results.SignIn(principal, authenticationScheme: Scheme);
    }

    private static async Task<IResult> Exchange(HttpContext http, OAuthIssuerOptions options, OAuthAccessService access,
        TimeProvider clock, CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest()!;
        if ((!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType()) || !ValidResource(request, options)) return Error(Errors.InvalidGrant);
        var authentication = await http.AuthenticateAsync(Scheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded || principal is null || !Guid.TryParse(principal.GetClaim(Claims.Subject), out var user)) return Error(Errors.InvalidGrant);
        // The only source of this item is an identity verified by OpenIddict's code/refresh handler.
        http.Items[OAuthActor.SubjectItem] = user;
        OAuthConnection grant;
        try { grant = await access.ValidateAsync(principal.GetAuthorizationId() ?? "", ct); }
        catch (UnauthorizedAccessException) { return Error(Errors.InvalidGrant); }
        if (grant.ClientId != request.ClientId || !principal.GetResources().SequenceEqual([options.Resource]) ||
            request.GetScopes().Any(x => !principal.HasScope(x)) || request.IsRefreshTokenGrantType() && !grant.RefreshAllowed)
            return Error(Errors.InvalidGrant);
        var remaining = grant.ExpiresUtc - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) return Error(Errors.InvalidGrant);
        principal.SetClaim(OAuthTokenLifetime.EndClaim, grant.ExpiresUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request.GetScopes().Length > 0) principal.SetScopes(request.GetScopes());
        principal.SetAccessTokenLifetime(remaining < TimeSpan.FromMinutes(5) ? remaining : TimeSpan.FromMinutes(5));
        principal.SetRefreshTokenLifetime(remaining);
        return Results.SignIn(principal, authenticationScheme: Scheme);
    }
}
