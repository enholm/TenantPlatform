using Microsoft.Extensions.Localization;

namespace TenantPlatform.Web.Security.McpClient;

public static class InternalMcpClientBoundary
{
    // Install BEFORE authentication: OpenIddict handles the callback inside that middleware.
    public static void UseInternalMcpClientBoundary(this WebApplication app) => app.Use(async (http, next) =>
    {
        if (!http.Request.Path.StartsWithSegments("/mcp-client")) { await next(http); return; }
        var original = http.RequestAborted;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(original); budget.CancelAfter(TimeSpan.FromSeconds(20));
        http.RequestAborted = budget.Token; http.Response.Headers.CacheControl = "no-store";
        try { await next(http); }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        { if (!http.Response.HasStarted) { http.Response.StatusCode = original.IsCancellationRequested ? 499 : 504; http.Response.ContentLength = 0; } }
        catch (Exception)
        {
            if (!http.Response.HasStarted)
            {
                http.Response.Clear(); http.Response.StatusCode = 503; http.Response.Headers.CacheControl = "no-store";
                http.Response.ContentType = "text/plain; charset=utf-8";
                await http.Response.WriteAsync(http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>()["McpClientUnavailable"], original);
            }
        }
        finally { http.RequestAborted = original; }
    });
}
