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
    private sealed record Command(Guid Conversation, Guid Connection, Guid Account, bool SharingAccepted, string? Prompt, Guid Draft = default, Guid Revision = default, int Page = 1, Guid PageId = default, ReadAgentArea? Area = null, bool ResetAccepted = false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8, Converters = { new JsonStringEnumConverter<ReadAgentArea>(allowIntegerValues: false) } };
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
                if ((action is not ("report-save" or "report-update" or "report-copy" or "report-discard" or "report-preview") && !settings.Ready) || http.RequestServices.GetService<McpRegistryService>() is null)
                    return Error(http, "Disabled", 503);
                if (http.Request.ContentLength > 96 * 1024) return Results.StatusCode(413);
                using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(ct); readBudget.CancelAfter(TimeSpan.FromSeconds(5));
                using var buffer = new MemoryStream(); var bytes = new byte[4096]; int n;
                while ((n = await http.Request.Body.ReadAsync(bytes, readBudget.Token)) != 0)
                { if (buffer.Length + n > 96 * 1024) return Results.StatusCode(413); buffer.Write(bytes, 0, n); }
                var command = JsonSerializer.Deserialize<Command>(buffer.ToArray(), Json) ?? throw new JsonException();
                var agent = http.RequestServices.GetRequiredService<ReadAgentService>();
                if (action is not ("begin" or "switch-area") && command.Area.HasValue) throw new JsonException();
                switch (action)
                {
                    case "begin":
                        var begun = await agent.BeginAsync(command.Connection, command.Account, command.SharingAccepted, command.Area ?? throw new ReadAgentException("AreaInvalid"), ct);
                        return Results.Json(new { conversation = begun, area = (await agent.AreaAsync(begun, ct)).ToString(), expiresInSeconds = ReadAgentSessions.Lifetime.TotalSeconds });
                    case "switch-area":
                        if (command.Connection != Guid.Empty) throw new JsonException();
                        var switched = await agent.SwitchAreaAsync(command.Conversation, command.Area ?? throw new ReadAgentException("AreaInvalid"), command.ResetAccepted, ct);
                        return Results.Json(new { conversation = switched, area = (await agent.AreaAsync(switched, ct)).ToString(), expiresInSeconds = ReadAgentSessions.Lifetime.TotalSeconds });
                    case "report-edit":
                        var opened = await agent.BeginEditAsync(command.PageId, command.Account, command.SharingAccepted, ct);
                        return Results.Json(new { opened.Conversation, area = (await agent.AreaAsync(opened.Conversation, ct)).ToString(), report = Report(opened.Report, http), expiresInSeconds = ReadAgentSessions.Lifetime.TotalSeconds }, AgentReportDrafts.Json);
                    case "report-reload":
                        var reloaded = await agent.ReloadReportAsync(command.Conversation, command.Draft, command.Revision, ct);
                        return Results.Json(new { reloaded.Conversation, area = (await agent.AreaAsync(reloaded.Conversation, ct)).ToString(), report = Report(reloaded.Report, http), expiresInSeconds = ReadAgentSessions.Lifetime.TotalSeconds }, AgentReportDrafts.Json);
                    case "report-discard": await agent.DiscardReportAsync(command.Conversation, command.Draft, command.Revision, ct); return Results.NoContent();
                    case "ask":
                        var answer = await agent.AskAsync(command.Conversation, command.Prompt ?? "", ct);
                        var l = http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
                        return Results.Json(new { answer.Text, report = answer.Report is null ? null : Report(answer.Report, http), answer.RetainedTurns, answer.CurrentTurnRetained, answer.ContextInvalidated, sources = answer.Sources.Select(s => new { s.Number, s.ConnectionId, s.ConnectionName, s.Tool, s.Version, s.Page, s.Attention, s.Aggregate, s.Finance, s.Revalidated, s.EvidenceId, summary = Summary(s, l) }), notices = answer.Notices.Select(k => l["AgentNotice" + k].Value) }, options: AgentReportDrafts.Json);
                    case "report-preview": return Results.Json(new { report = Report(await agent.PreviewReportAsync(command.Conversation, command.Draft, command.Revision, command.Page, ct), http) }, AgentReportDrafts.Json);
                    case "report-save":
                    case "report-update":
                    case "report-copy":
                        var savedPage = await agent.SaveReportAsync(command.Conversation, command.Draft, command.Revision, ct, copy: action == "report-copy", editing: action != "report-save");
                        return Results.Json(new { pageId = savedPage, url = "/my-pages/" + savedPage });
                    case "delete": await agent.DeleteAsync(command.Conversation, ct); return Results.NoContent();
                    case "cancel": await agent.CancelAsync(command.Conversation, ct); return Results.NoContent();
                    default: return Results.NotFound();
                }
            }
            catch (UnauthorizedAccessException) { return Error(http, "AccessChanged", 403); }
            catch (ReadAgentException e) { return Error(http, e.Code, e.Code == "Busy" ? 429 : e.Code == "DraftConflict" ? 409 : 400); }
            catch (TenantPlatform.Application.PersonalPages.PersonalPageException e) { return Error(http, "Draft" + e.Code, e.Code == "Conflict" ? 409 : 400); }
            catch (McpClientException) { return Error(http, "SourceUnavailable", 400); }
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
    private static object Report(AgentReportPreview report, HttpContext http)
    {
        var l = http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
        var d = report.Draft;
        string EnumLabel(Enum e, string prefix = "") => l[prefix + e].Value;
        string Period(TenantPlatform.Application.PersonalPages.PagePeriod p) => d.Definition.Area == TenantPlatform.Application.PersonalPages.ReportArea.ServiceRequests ? l["RequestReportAll"].Value : p.Kind == TenantPlatform.Application.PersonalPages.PagePeriodKind.NextMonths
            ? l["DraftMonths", p.Months!].Value : p.Kind == TenantPlatform.Application.PersonalPages.PagePeriodKind.All ? l["PagesAllDates"].Value : $"{p.From:yyyy-MM-dd} – {p.To:yyyy-MM-dd}";
        string ChangeValue(string field, JsonElement value) => value.ValueKind == JsonValueKind.Null ? l["PagesAll"].Value : field switch {
            "Period" => Period(value.Deserialize<TenantPlatform.Application.PersonalPages.PagePeriod>(AgentReportDrafts.Json)!),
            "Components" => string.Join(" → ", value.EnumerateArray().Select(x => l[TenantPlatform.Web.PersonalPages.ReportLabels.ComponentKey(d.Definition.Area, x.GetString()!)].Value)),
            "Columns" => string.Join(" → ", value.EnumerateArray().Select(x => l["PagesColumn" + x.GetString()].Value)),
            "GroupBy" => l[value.GetString()!].Value,
            "RequestStatus" => l["ServiceRequestStatus_" + value.GetString()].Value,
            "Status" => l["AgreementStatus" + value.GetString()].Value, "Type" => l["AgreementType" + value.GetString()].Value,
            "Archive" => l["PagesArchive" + value.GetString()].Value, "MissingBuilding" => l[value.GetBoolean() ? "Yes" : "No"].Value,
            _ => value.GetString() ?? "" };
        var value = new { d.Version, d.Id, d.Revision, d.SavedPageId, d.SourceName, d.Definition.Title, d.Definition.Components,
            editingPageId = d.Edit?.PageId, originalTitle = d.Edit?.Original.Title,
            changes = AgentReportDrafts.Changes(d).Select(c => new { field = l["DraftChange" + c.Field].Value, before = ChangeValue(c.Field, c.Before), after = ChangeValue(c.Field, c.After) }),
            intent = d.Intent, preview = report.Preview,
            summary = new {
                period = Period(d.Definition.Period),
                status = d.Definition.RequestFilter?.Status is { } requestStatus ? EnumLabel(requestStatus, "ServiceRequestStatus_") : d.Definition.Filter.Status is { } status ? EnumLabel(status, "AgreementStatus") : l["PagesAll"].Value,
                type = d.Definition.Area == TenantPlatform.Application.PersonalPages.ReportArea.ServiceRequests ? "" : d.Definition.Filter.Type is { } type ? EnumLabel(type, "AgreementType") : l["PagesAll"].Value,
                archive = d.Definition.Area == TenantPlatform.Application.PersonalPages.ReportArea.ServiceRequests ? "" : EnumLabel(d.Definition.Filter.Archive, "PagesArchive"),
                building = d.Intent.Filter.BuildingName ?? ((d.Definition.RequestFilter?.MissingBuilding ?? d.Definition.Filter.MissingBuilding) ? l["PagesMissingOnly"].Value : (d.Definition.RequestFilter?.BuildingId ?? d.Definition.Filter.BuildingId).HasValue ? l["DraftStoredBuilding"].Value : l["PagesAll"].Value),
                search = d.Definition.RequestFilter?.Search ?? d.Definition.Filter.Search ?? "" } };
        if (JsonSerializer.SerializeToUtf8Bytes(value, AgentReportDrafts.Json).Length > 131072) throw new ReadAgentException("DraftLimit");
        return value;
    }
    private static string? Summary(AgentSource source, IStringLocalizer<TenantPlatformResources> l)
    {
        string Flag(bool value) => l[value ? "Yes" : "No"].Value;
        if (source.Page is { } p) return l["AgentPageFacts", p.Page, p.RequestedPage, p.ReturnedCount, p.TotalCount, p.PageSize,
            Flag(p.HasMore), p.NextPage?.ToString() ?? "—", Flag(p.Complete), Flag(p.SnapshotConsistent), Flag(p.LimitReached)].Value;
        if (source.Finance is { } f) return l["AgentFinanceFacts", l[f.From is null ? "AgentFinanceHistory" : "AgentFinanceForecast"].Value, f.Currency ?? "", f.Direction is { } direction ? l["FinanceDirection" + direction].Value : "", f.From ?? "", f.To ?? "", Flag(f.Complete), f.VersionsTotal?.ToString() ?? "", f.PricesTotal?.ToString() ?? "", f.PeriodCount?.ToString() ?? ""].Value;
        if (source.Aggregate is { } g) return l[source.Tool == "buildings_aggregate" ? "StructureAggregateFacts" : source.Tool == "service_requests_aggregate" ? "RequestReportAggregateFacts" : "AgentAggregateFacts", l[source.Tool == "agreements_insights" ? "DashboardDimension" + g.GroupBy : g.GroupBy].Value, g.TotalCount, g.GroupCount, Flag(g.Complete), Flag(g.GroupsOverlap), Flag(g.SnapshotConsistent)].Value;
        if (source.Attention is { } a) return l["AgentAttentionFacts", a.Today, a.Notice, a.Renewal, a.Expired, a.HorizonDays].Value;
        return null;
    }
    private static IResult Error(HttpContext http, string code, int status)
    {
        var localizer = http.RequestServices.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
        var message = localizer["AgentError" + code];
        return Results.Json(new { invalidateDraft = code is "HistoryUnavailable" or "ConnectionUnavailable" or "AccessChanged" or "DraftSourceChanged" or "DraftAuthorization", error = message.ResourceNotFound ? localizer["AgentErrorUnavailable"].Value : message.Value }, statusCode: status);
    }
}
