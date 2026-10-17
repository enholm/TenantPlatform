using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenIddict.Client.AspNetCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Web.Components.Shared.OAuth;

namespace TenantPlatform.Web.Security.McpClient;

public static class InternalMcpClientEndpoints
{
    public static void MapInternalMcpClient(this WebApplication app)
    {
        app.MapGet("/mcp-client", async (HttpContext http, InternalMcpClientOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                var status = options.Available ? await http.RequestServices.GetRequiredService<InternalMcpClientService>().StatusAsync(ct) : null;
                return Page(http, antiforgery, options, status);
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Page(http, antiforgery, options, null, "UNAVAILABLE"); }
        });
        app.MapPost("/mcp-client/connect", async (HttpContext http, InternalMcpClientOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                await antiforgery.ValidateRequestAsync(http);
                if (!options.Available) return Page(http, antiforgery, options, null, "UNAVAILABLE");
                var start = await http.RequestServices.GetRequiredService<InternalMcpClientService>().BeginAsync(ct);
                var properties = new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = InternalMcpClientOptions.Registration,
                    [InternalMcpClientOptions.UserProperty] = start.UserId.ToString("D"), [InternalMcpClientOptions.PendingProperty] = start.PendingId.ToString("D")
                }) { RedirectUri = "/mcp-client" };
                return Results.Challenge(properties, [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Page(http, antiforgery, options, null, "CONNECTION_REJECTED"); }
        });
        app.MapMethods("/mcp-client/callback", ["GET", "POST"], async (HttpContext http, InternalMcpClientOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                if (!options.Available) return Page(http, antiforgery, options, null, "UNAVAILABLE");
                await http.RequestServices.GetRequiredService<InternalMcpClientService>().CompleteAsync(ct);
                return Results.LocalRedirect("/mcp-client"); // Never sign the OAuth tokens/identity into the GUI cookie.
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Page(http, antiforgery, options, null, "CONNECTION_REJECTED"); }
        }).DisableAntiforgery(); // SDK state + correlation + single-use state token, not a form CSRF exemption for other endpoints.
        app.MapPost("/mcp-client/disconnect", async (HttpContext http, InternalMcpClientOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                await antiforgery.ValidateRequestAsync(http);
                if (!options.Available) return Page(http, antiforgery, options, null, "UNAVAILABLE");
                var confirmed = await http.RequestServices.GetRequiredService<InternalMcpClientService>().DisconnectAsync(ct);
                return Page(http, antiforgery, options, new(false, false, null), confirmed ? "DISCONNECTED" : "REVOCATION_UNCONFIRMED");
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (Exception) { return Page(http, antiforgery, options, null, "UNAVAILABLE"); }
        });
        app.MapPost("/mcp-client/probe", async (HttpContext http, InternalMcpClientOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                if (!await Cookie(http)) return Results.Unauthorized();
                await antiforgery.ValidateRequestAsync(http);
                if (!options.Available) return Page(http, antiforgery, options, null, "UNAVAILABLE");
                var service = http.RequestServices.GetRequiredService<InternalMcpClientService>();
                var form = await http.Request.ReadFormAsync(ct);
                var selected = form["accountId"].ToString();
                if (selected.Length != 0 && (!Guid.TryParseExact(selected, "D", out var id) || id == Guid.Empty)) return Results.BadRequest();
                var accounts = await service.AccountsAsync(ct);
                var attention = selected.Length == 0 ? null : await service.AttentionAsync(Guid.Parse(selected), ct);
                return Page(http, antiforgery, options, await service.StatusAsync(ct), accounts: accounts, attention: attention);
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (McpClientException ex) { return Page(http, antiforgery, options, await SafeStatus(http, options, ct), ex.Code is "RECONNECT_REQUIRED" ? "RECONNECT_REQUIRED" : "MCP_UNAVAILABLE"); }
            catch (OperationCanceledException) { return Results.StatusCode(499); }
            catch (Exception) { return Page(http, antiforgery, options, await SafeStatus(http, options, ct), "MCP_UNAVAILABLE"); }
        });
    }
    private static IResult Page(HttpContext http, IAntiforgery antiforgery, InternalMcpClientOptions options, McpClientStatus? status,
        string? message = null, McpClientAccounts? accounts = null, McpClientAttention? attention = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        return new RazorComponentResult<McpClientPage>(new { Available = options.Available, Status = status,
            Message = message, Accounts = accounts, Attention = attention, VerificationToken = antiforgery.GetAndStoreTokens(http).RequestToken });
    }
    private static async Task<McpClientStatus?> SafeStatus(HttpContext http, InternalMcpClientOptions options, CancellationToken ct)
    {
        if (!options.Available || ct.IsCancellationRequested) return null;
        try { return await http.RequestServices.GetRequiredService<InternalMcpClientService>().StatusAsync(ct); }
        catch (Exception) { return null; }
    }
    private static async Task<bool> Cookie(HttpContext http)
    {
        var cookie = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!cookie.Succeeded) return false;
        http.User = cookie.Principal!; return true;
    }
}
