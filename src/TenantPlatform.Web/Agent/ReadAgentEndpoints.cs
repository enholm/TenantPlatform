using TenantPlatform.Application.Mcp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Web.Security.McpClient;

namespace TenantPlatform.Web.Agent;

public static class ReadAgentEndpoints
{
    private sealed record Command(Guid Conversation, Guid Connection, Guid Account, bool SharingAccepted, string? Prompt);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public static void MapReadAgent(this WebApplication app)
    {
        app.MapGet("/agent/context", async (HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try { return await Identity(http, ct) is { } identity ? Results.Json(new { context = identity.Key }) : Results.Unauthorized(); }
            catch (Exception) { return Results.Unauthorized(); }
        });
        app.MapPost("/agent/{action}", async (string action, HttpContext http, IAntiforgery csrf, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var settings = http.RequestServices.GetRequiredService<IOptions<ReadAgentOptions>>().Value;
            operation.CancelAfter(TimeSpan.FromSeconds(settings.Limits.Valid ? settings.Limits.Seconds : 5));
            ct = operation.Token;
            try
            {
                if (await Identity(http, ct) is null) return Results.Unauthorized();
                await csrf.ValidateRequestAsync(http);
                if (!http.RequestServices.GetRequiredService<IOptions<ReadAgentOptions>>().Value.Ready || http.RequestServices.GetService<McpRegistryService>() is null)
                    return Error(http, "Disabled", 503);
                if (http.Request.ContentLength > 96 * 1024) return Results.StatusCode(413);
                using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(ct); readBudget.CancelAfter(TimeSpan.FromSeconds(5));
                using var buffer = new MemoryStream(); var bytes = new byte[4096]; int n;
                while ((n = await http.Request.Body.ReadAsync(bytes, readBudget.Token)) != 0)
                { if (buffer.Length + n > 96 * 1024) return Results.StatusCode(413); buffer.Write(bytes, 0, n); }
                var command = JsonSerializer.Deserialize<Command>(buffer.ToArray(), Json) ?? throw new JsonException();
                var agent = http.RequestServices.GetRequiredService<ReadAgentService>();
                switch (action)
                {
                    case "begin": return Results.Json(new { conversation = await agent.BeginAsync(command.Connection, command.Account, command.SharingAccepted, ct), expiresInSeconds = ReadAgentSessions.Lifetime.TotalSeconds });
                    case "ask":
                        var answer = await agent.AskAsync(command.Conversation, command.Prompt ?? "", ct);
                        var l = http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
                        return Results.Json(new { answer.Text, answer.RetainedTurns, answer.CurrentTurnRetained, answer.ContextInvalidated, sources = answer.Sources.Select(s => new { s.Number, s.ConnectionId, s.ConnectionName, s.Tool, s.Version, s.Page, s.Attention, s.Revalidated, s.EvidenceId, summary = Summary(s, l) }), notices = answer.Notices.Select(k => l["AgentNotice" + k].Value) }, options: Json);
                    case "delete": await agent.DeleteAsync(command.Conversation, ct); return Results.NoContent();
                    case "cancel": await agent.CancelAsync(command.Conversation, ct); return Results.NoContent();
                    default: return Results.NotFound();
                }
            }
            catch (UnauthorizedAccessException) { return Error(http, "AccessChanged", 403); }
            catch (ReadAgentException e) { return Error(http, e.Code, e.Code == "Busy" ? 429 : 400); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (JsonException) { return Results.BadRequest(); }
            catch (OperationCanceledException) { return Error(http, "CancelledOrTimedOut", 408); }
            catch (Exception) { return Error(http, "Unavailable", 503); }
        });
    }
    public static async Task<(Guid Account, string Key)?> Identity(HttpContext http, CancellationToken ct)
    {
        var cookie = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!cookie.Succeeded || cookie.Principal is null) return null;
        http.User = cookie.Principal;
        var actor = new RegistryCookieActor(http.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var who = actor.GetSnapshot();
        if (who.AccountId is not Guid account) return null;
        var auth = new AuthorizationService(actor, http.RequestServices.GetRequiredService<IAuthorizationData>(), http.RequestServices.GetRequiredService<TimeProvider>());
        if (!await auth.CanAsync(Permissions.McpConnections.Use, new(ResourceKind.Account, AccountId: account), ct))
        {
            http.RequestServices.GetService<ReadAgentSessions>()?.ClearUser(who.UserId);
            return null;
        }
        return (account, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(who.UserId + ":" + account))));
    }
    private static string? Summary(AgentSource source, IStringLocalizer<TenantPlatformResources> l)
    {
        string Flag(bool value) => l[value ? "Yes" : "No"].Value;
        if (source.Page is { } p) return l["AgentPageFacts", p.Page, p.RequestedPage, p.ReturnedCount, p.TotalCount, p.PageSize,
            Flag(p.HasMore), p.NextPage?.ToString() ?? "—", Flag(p.Complete), Flag(p.SnapshotConsistent), Flag(p.LimitReached)].Value;
        if (source.Attention is { } a) return l["AgentAttentionFacts", a.Today, a.Notice, a.Renewal, a.Expired, a.HorizonDays].Value;
        return null;
    }
    private static IResult Error(HttpContext http, string code, int status)
    {
        var localizer = http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
        var message = localizer["AgentError" + code];
        return Results.Json(new { error = message.ResourceNotFound ? localizer["AgentErrorUnavailable"].Value : message.Value }, statusCode: status);
    }
}
