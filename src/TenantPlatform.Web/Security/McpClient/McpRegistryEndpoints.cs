using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenIddict.Client.AspNetCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Web.Components.Shared.OAuth;

namespace TenantPlatform.Web.Security.McpClient;

public static class McpRegistryEndpoints
{
    public const string ConnectionProperty = "tenantplatform.mcp.connection";
    public static void MapMcpRegistry(this WebApplication app)
    {
        app.MapGet("/mcp-client/registry", async (HttpContext http, IAntiforgery csrf, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                if (http.RequestServices.GetService<McpRegistryService>() is not { } service) return Results.StatusCode(503);
                var page = await service.ListAsync(ct);
                var states = new Dictionary<Guid, McpClientStatus>();
                var tools = new Dictionary<Guid, IReadOnlyList<TenantPlatform.Core.Identity.McpDiscoveredTool>>();
                foreach (var row in page.Servers)
                {
                    try { states[row.Id] = await service.StatusAsync(row.Id, ct); }
                    catch (McpClientException) { }
                    tools[row.Id] = await service.ToolsAsync(row.Id, ct);
                }
                http.Response.Headers.CacheControl = "no-store";
                return new RazorComponentResult<McpRegistryPageView>(new { Page = page, States = states, Tools = tools, NowUtc = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(), VerificationToken = csrf.GetAndStoreTokens(http).RequestToken });
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Failure(http); }
        });
        app.MapPost("/mcp-client/registry/{action}", async (string action, HttpContext http, IAntiforgery csrf, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                if (http.Request.ContentLength > 16 * 1024) return Results.StatusCode(413);
                http.Request.EnableBuffering(16 * 1024, 16 * 1024);
                await csrf.ValidateRequestAsync(http);
                var form = await http.Request.ReadFormAsync(ct);
                var service = http.RequestServices.GetRequiredService<McpRegistryService>();
                Guid? id = Guid.TryParseExact(form["id"], "D", out var connection) ? connection : null;
                switch (action)
                {
                    case "save":
                        if (!string.IsNullOrEmpty(form["id"]) && id is null) return Results.BadRequest();
                        await service.SaveAsync(id, Guid.TryParseExact(form["revision"], "D", out var revision) ? revision : null,
                            form["name"].ToString(), form["profile"].ToString(), form["enabled"] == "true", ct); break;
                    case "connect" when id.HasValue:
                        var page = await service.ListAsync(ct);
                        var row = page.Servers.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException();
                        var runtime = http.RequestServices.GetRequiredService<McpAuthProfiles>().Loaded.Single(x => x.Public.Id == row.ProfileId && x.Public.Version == row.ProfileVersion);
                        var begin = await service.BeginAsync(id.Value, ct);
                        return Results.Challenge(new AuthenticationProperties(new Dictionary<string, string?> {
                            [OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = runtime.RegistrationId,
                            [InternalMcpClientOptions.UserProperty] = begin.UserId.ToString("D"),
                            [InternalMcpClientOptions.PendingProperty] = begin.PendingId.ToString("D"), [ConnectionProperty] = id.Value.ToString("D")
                        }) { RedirectUri = "/mcp-client/registry" }, [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
                    case "disconnect" when id.HasValue:
                        if (!await service.DisconnectAsync(id.Value, ct)) return Failure(http, "McpClientRevocationUnconfirmed"); break;
                    case "discover" when id.HasValue: await service.DiscoverAsync(id.Value, ct); break;
                    case "approve" when id.HasValue: await service.ApproveAsync(id.Value, form["tool"].ToString(), form["version"].ToString(), form["approved"] == "true", ct); break;
                    default: return Results.BadRequest();
                }
                return Results.LocalRedirect("/mcp-client/registry");
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (IOException) { return Results.StatusCode(413); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Failure(http); }
        });
        app.MapMethods("/mcp-client/registry/callback/{profile}", ["GET", "POST"], async (HttpContext http, CancellationToken ct) =>
        {
            try { if (!await Cookie(http)) return Results.Unauthorized(); return await Complete(http, ct); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Failure(http); }
        }).DisableAntiforgery();
    }
    internal static async Task<IResult> Complete(HttpContext http, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties?.Items.TryGetValue(ConnectionProperty, out var value) != true ||
            !Guid.TryParseExact(value, "D", out var connection)) throw new McpClientException("CONNECTION_REJECTED");
        await http.RequestServices.GetRequiredService<McpRegistryService>().CompleteAsync(connection, ct);
        return Results.LocalRedirect("/mcp-client/registry");
    }
    private static IResult Failure(HttpContext http, string key = "McpClientFailed")
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Text(http.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<TenantPlatform.Web.TenantPlatformResources>>()[key], statusCode: 400);
    }
    private static async Task<bool> Cookie(HttpContext http)
    {
        var result = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded) return false; http.User = result.Principal!; return true;
    }
}
