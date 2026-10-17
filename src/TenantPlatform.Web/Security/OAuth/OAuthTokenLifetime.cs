using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.OAuth;

public static class OAuthTokenLifetime
{
    public const string EndClaim = "tp_delegation_end";
    public static void Configure(OpenIddictServerBuilder server) => server.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(handler => handler
        .SetOrder(OpenIddictServerHandlers.GenerateAccessToken.Descriptor.Order - 500)
        .UseInlineHandler(context =>
        {
            if (context.AccessTokenPrincipal is null && context.RefreshTokenPrincipal is null) return ValueTask.CompletedTask;
            if (!long.TryParse(context.Principal?.GetClaim(EndClaim), out var seconds))
            {
                context.Reject(Errors.InvalidGrant);
                return ValueTask.CompletedTask;
            }
            var end = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (end <= context.Options.TimeProvider!.GetUtcNow())
            {
                context.Reject(Errors.InvalidGrant);
                return ValueTask.CompletedTask;
            }
            // Clamp after the SDK has prepared principals and before serialization/storage. A relative
            // lifetime computed in the HTTP handler alone can drift beyond the end during later awaits.
            foreach (var principal in new[] { context.AccessTokenPrincipal, context.RefreshTokenPrincipal })
                if (principal is not null && (principal.GetExpirationDate() is not { } expiry || expiry > end)) principal.SetExpirationDate(end);
            context.AccessTokenPrincipal?.SetClaim(EndClaim, (string?)null);
            return ValueTask.CompletedTask;
        }));
}
