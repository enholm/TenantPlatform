using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using OpenIddict.Abstractions;

namespace TenantPlatform.Mcp;

// Return only standard bearer errors; never copy token-validation diagnostics into the response.
internal sealed class McpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    internal const string ErrorItem = "TenantPlatform.Mcp.AuthError";
    public Task HandleAsync(RequestDelegate next, HttpContext http, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded) return next(http);
        var missingScope = result.Forbidden && !http.User.HasScope(McpOptions.ReadScope);
        http.Response.StatusCode = missingScope ? 403 : 401;
        if (missingScope) http.Items[ErrorItem] = "insufficient_scope";
        else if (http.Request.Headers.ContainsKey("Authorization")) http.Items[ErrorItem] = "invalid_token";
        return Task.CompletedTask;
    }
}
