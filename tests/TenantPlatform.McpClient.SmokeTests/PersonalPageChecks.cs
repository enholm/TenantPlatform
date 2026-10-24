using Microsoft.IdentityModel.Tokens;
using TenantPlatform.Application.Security;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using OpenIddict.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.PersonalPages;
using TenantPlatform.Infrastructure.PersonalPages;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.PersonalPages;
using TenantPlatform.Web.Security;
using TenantPlatform.Web.Security.McpClient;

static partial class RegistryChecks
{
    private static async Task RunPersonalPages(WebApplication web, Factory factory, string writer, Guid admin, Guid member, Guid account, Guid foreign, Guid source, Browser browser, InternalMcpClientOptions own, AgentProtocolFixture model)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var modelCalls = model.Payloads.Count;
        var modelOptions = web.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TenantPlatform.Infrastructure.Agent.ReadAgentOptions>>().Value;
        var previousKey = modelOptions.ApiKey; modelOptions.ApiKey = "";
        modelOptions.Enabled = false;
        await using (var db = factory.CreateDbContext())
        {
            var first = await db.Agreements.FirstAsync(a => a.AccountId == account);
            for (var i = 0; i < 27; i++) db.Agreements.Add(new Agreement { Id = Guid.NewGuid(), AccountId = account, CounterpartyOrganizationId = first.CounterpartyOrganizationId, OwnerUserId = admin,
                Title = "Personal report " + i.ToString("D2"), Type = AgreementType.Supplier, Status = AgreementStatus.Active, EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
                StartDate = new(2025,1,1), CurrentPeriodStartDate = new(2025,1,1), CreatedByUserId = admin, UpdatedByUserId = admin, Revision = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var binding = (await Scope(s => s.SourcesAsync())).Single(s => s.ConnectionId == source);
        var spec = new PageDefinition(1, "Expiring agreements <script>", source, binding.QueryVersion, binding.AggregateVersion, new(PagePeriodKind.NextMonths),
            new(Search: "Personal report", Archive: AgreementArchiveSelection.Exclude), [PageComponent.Metric, PageComponent.BuildingChart, PageComponent.AgreementTable], ProfileVersion: binding.ProfileVersion);
        var empty = await Scope(s => s.ListAsync());
        var preview = await Run(spec);
        Check(preview.Aggregate!.TotalCount == 27 && preview.Table!.ReturnedCount == 25 && preview.Table!.HasMore && preview.Aggregate!.Groups.Single().Key == "missing", "personal report total/chart use full SQL aggregate while table is paged");
        Check((await Scope(s => s.ListAsync())).Pages.Length == 0, "preview persists no result or definition");
        var actualCalls = PagesProtocol.Calls.TakeLast(2).ToArray();
        Check(actualCalls[0].Name == "agreements_aggregate" && actualCalls[1].Name == "agreements_query" && actualCalls[1].Args.GetProperty("endDateFrom").GetString() == preview.Selection.Filter.EndDateFrom!.Value.ToString("yyyy-MM-dd"), "business data actually crosses MCP and query reuses server-normalized dates");
        var saved = await Scope(s => s.SaveAsync(null, empty.Revision, spec)); var id = saved.Pages.Single().Id;
        Check(saved.Pages.Single().OwnerUserId == admin && saved.Pages.Single().AccountId == account && saved.Pages.Single().Definition.Period.Kind == PagePeriodKind.NextMonths, "backend binds owner/account and persists relative intention");
        Check((await Run(spec, 2, id)).Table!.ReturnedCount == 2, "saved report reopens page two without scanning list pages");
        Check((await Scope(s => s.ListAsync(), member)).Pages.Length == 0, "personal lists exclude another user's definitions");
        await Denied(() => Scope(s => s.ReadAsync(id), member)); await Denied(() => Scope(s => s.ReadAsync(id), selected: foreign));
        await FailsPage(() => Scope(s => s.SaveAsync(id, empty.Revision, spec)), "Conflict");
        var update = await Scope(s => s.SaveAsync(id, saved.Revision, spec with { Title = "Renamed", Period = new(PagePeriodKind.Fixed, null, preview.Selection.Filter.EndDateFrom, preview.Selection.Filter.EndDateTo) }));
        var two = await Scope(s => s.SaveAsync(null, update.Revision, spec with { Title = "Second" }));
        var moved = await Scope(s => s.ChangeAsync(id, two.Revision, 1)); Check(moved.Pages.Last().Id == id, "move preserves stable identity and saved ordering");
        await FailsPage(() => Scope(s => s.ChangeAsync(id, two.Revision, null)), "Conflict");
        var deleted = await Scope(s => s.ChangeAsync(id, moved.Revision, null)); Check(!deleted.Pages.Any(p => p.Id == id), "owner deletes explicit definition");
        var kept = deleted.Pages.Single();
        var writes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async n => { try { await Scope(p => p.SaveAsync(kept.Id, deleted.Revision, kept.Definition with { Title = "Concurrent " + n })); return true; } catch (PersonalPageException e) when(e.Code == "Conflict") { return false; } }));
        Check(writes.Count(x => x) == 1, "concurrent edits have one winner and an explicit conflict");
        kept = (await Scope(p => p.ReadAsync(kept.Id)));
        var settingsWereMissing = false; string? previousZone;
        await using (var db = factory.CreateDbContext())
        {
            var settings = await db.AgreementReminderSettings.SingleOrDefaultAsync(x => x.AccountId == account);
            settingsWereMissing = settings is null; previousZone = settings?.TimeZoneId;
            if (settings is null) { settings = new() { AccountId = account }; db.AgreementReminderSettings.Add(settings); }
            settings.TimeZoneId = "Pacific/Kiritimati"; await db.SaveChangesAsync();
        }
        var nextDay = await Run(kept.Definition);
        await using (var db = factory.CreateDbContext()) { var settings = await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == account); settings.TimeZoneId = "Pacific/Pago_Pago"; await db.SaveChangesAsync(); }
        var priorDay = await Run(kept.Definition);
        Check(nextDay.Selection.Today > priorDay.Selection.Today && nextDay.Selection.Filter.EndDateFrom != priorDay.Selection.Filter.EndDateFrom && (await Scope(p => p.ReadAsync(kept.Id))).Definition.Period.Kind == PagePeriodKind.NextMonths,
            "relative intent normalizes afresh across account-date boundary using server time zone");
        await using (var db = factory.CreateDbContext()) { var settings = await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == account); if (settingsWereMissing) db.Remove(settings); else settings.TimeZoneId = previousZone!; await db.SaveChangesAsync(); }
        var noMatches = await Run(spec with { Filter = spec.Filter with { Search = "no-personal-matches" } });
        Check(noMatches.Aggregate!.TotalCount == 0 && noMatches.Table!.Items.Length == 0 && noMatches.Aggregate!.Complete, "authorized empty report differs from a failed retrieval");
        foreach (var bad in new[] { spec with { Version = 99 }, spec with { Components = [PageComponent.Metric, PageComponent.Metric, PageComponent.AgreementTable] }, spec with { Layout = "<script>" }, spec with { Period = new(PagePeriodKind.NextMonths, 13) } })
            await FailsPage(() => Scope(s => s.SaveAsync(null, deleted.Revision, bad)), bad.Version == 99 ? "Version" : "Invalid");
        var grantId = Guid.NewGuid(); Guid visibleAgreement;
        await using (var db = factory.CreateDbContext())
        {
            visibleAgreement = (await db.Agreements.FirstAsync(a => a.AccountId == account && a.Title.StartsWith("Personal report"))).Id;
            db.AgreementAccess.Add(new AgreementAccess { Id = grantId, AccountId = account, AgreementId = visibleAgreement, UserId = member, Level = AgreementAccessLevel.Read }); await db.SaveChangesAsync();
        }
        var memberCollection = await Scope(p => p.ListAsync(), member);
        var memberPage = (await Scope(p => p.SaveAsync(null, memberCollection.Revision, spec), member)).Pages.Single();
        Check((await Run(spec, user: member)).Aggregate!.TotalCount == 1, "report respects member's single agreement grant");
        await using (var db = factory.CreateDbContext()) { db.Remove(await db.AgreementAccess.SingleAsync(g => g.Id == grantId)); await db.SaveChangesAsync(); }
        Check((await Run(spec, user: member)).Aggregate!.TotalCount == 0, "revoked agreement grant changes fresh report without losing tool access");
        await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => { var t = await db.Set<TenantPlatform.Core.Identity.McpDiscoveredTool>().SingleAsync(t => t.ConnectionId == source && t.Name == "agreements_query"); t.Approved = false; await db.SaveChangesAsync(); return true; });
        await FailsPage(() => Run(spec), "SourceChanged");
        await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => { var t = await db.Set<TenantPlatform.Core.Identity.McpDiscoveredTool>().SingleAsync(t => t.ConnectionId == source && t.Name == "agreements_query"); t.Approved = true; await db.SaveChangesAsync(); return true; });
        Check((await Scope(p => p.ReadAsync(memberPage.Id), member)).OwnerUserId == member, "ordinary member owns page without administrator inheritance");
        var protectedPayload = await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => { var credential = await db.Set<TenantPlatform.Core.Identity.McpClientCredential>().SingleAsync(c => c.ConnectionId == source && c.UserId == member); var payload = credential.ProtectedTokens; credential.ProtectedTokens = null; await db.SaveChangesAsync(); return payload; });
        await FailsPage(() => Run(spec, user: member), "Authorization");
        await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => { var credential = await db.Set<TenantPlatform.Core.Identity.McpClientCredential>().SingleAsync(c => c.ConnectionId == source && c.UserId == member); credential.ProtectedTokens = protectedPayload; await db.SaveChangesAsync(); return true; });
        var changed = await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => {
            var tool = await db.Set<TenantPlatform.Core.Identity.McpDiscoveredTool>().SingleAsync(t => t.ConnectionId == source && t.Name == "agreements_query"); var original = tool.Version; tool.Version = "changed-contract"; await db.SaveChangesAsync(); return original; });
        await FailsPage(() => Run(kept.Definition, savedId: kept.Id), "SourceChanged");
        await web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>().WithDb(async db => { var t = await db.Set<TenantPlatform.Core.Identity.McpDiscoveredTool>().SingleAsync(t => t.ConnectionId == source && t.Name == "agreements_query"); t.Version = changed; await db.SaveChangesAsync(); return true; });
        Check((await Run(kept.Definition, savedId: kept.Id)).Aggregate!.TotalCount == 27, "explicit unchanged binding recovers without stale result cache");
        PagesProtocol.Block = true;
        var pending = Run(kept.Definition); await PagesProtocol.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await FailsPage(() => Run(kept.Definition), "Busy");
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(TenantPlatformClaimTypes.UserId, admin.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, account.ToString()) }, "Cookies")) };
            await using var db = factory.CreateDbContext();
            await scope.ServiceProvider.GetRequiredService<AuthenticationCookieService>().SignInAsync(httpContext, await db.Users.SingleAsync(u => u.Id == admin), null, false);
        }
        await FailsPage(() => pending, "Cancelled"); PagesProtocol.Block = false;
        Check(model.Payloads.Count == modelCalls && !modelOptions.Enabled, "pages work with agent disabled and never call model adapter");
        var html = await (await browser.Get("/my-pages")).Content.ReadAsStringAsync();
        Check(html.Contains("data-personal-pages") && html.Contains("data-editor") && !html.Contains("<script>Expiring"), "SSR personal page editor renders without AI or unescaped titles");
        var csrf = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "data-csrf=\"([^\"]+)\"").Groups[1].Value);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://tenant.test/my-pages/action/run") { Content = new StringContent(JsonSerializer.Serialize(new { id = kept.Id, revision = deleted.Revision, definition = (object?)null }, json), Encoding.UTF8, "application/json") };
        req.Headers.Add("Cookie", string.Join("; ", browser.Cookies.Select(p => p.Key + "=" + p.Value))); req.Headers.Add("RequestVerificationToken", csrf);
        Check((await web.GetTestClient().SendAsync(req)).IsSuccessStatusCode, "real cookie/antiforgery endpoint runs saved report");
        await PersonalPagesPrivileges.VerifyAsync(writer, default);
        var full = factory.CreateDbContext().Database.GetConnectionString()!;
        var broadRejected = false; try { await PersonalPagesPrivileges.VerifyAsync(full, default); } catch (InvalidOperationException) { broadRejected = true; }
        Check(broadRejected, "administration rejects a broad database identity");
        await using (var db = new NpgsqlConnection(writer)) { await db.OpenAsync(); await using var cmd = new NpgsqlCommand("UPDATE agreements SET \"Title\"='forbidden'", db); try { await cmd.ExecuteNonQueryAsync(); throw new Exception("writer modified agreements"); } catch (PostgresException e) when(e.SqlState == "42501") { } }
        // Standard authorization code + PKCE, same established issuer/client; explicit extra write scope.
        var adminToken = await Token(true); var readToken = await Token(false);
        using (var unauth = new HttpRequestMessage(HttpMethod.Post, "https://tenant.test/mcp/pages") { Content = new StringContent("{}", Encoding.UTF8, "application/json") }) { unauth.Headers.Authorization = new("Bearer", readToken); Check((await web.GetTestClient().SendAsync(unauth)).StatusCode == HttpStatusCode.Forbidden, "read-only OAuth token cannot enter administration MCP surface"); }
        var http = web.GetTestClient(); http.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);
        await using (var client = await ModelContextProtocol.Client.McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new("https://tenant.test/mcp/pages"), TransportMode = HttpTransportMode.StreamableHttp }, http)))
        {
            var tools = await client.ListToolsAsync(); Check(tools.Count == 5 && tools.Any(t => t.Name == "personal_pages_save" && t.ProtocolTool.Annotations?.ReadOnlyHint == false), "separate MCP administration exposes explicit writes, never AutomaticRead");
            var result = await client.CallToolAsync("personal_pages_list", new Dictionary<string, object?> { ["accountId"] = account });
            Check(result.IsError != true && result.StructuredContent!.Value.GetProperty("pages").GetArrayLength() == 1, "real MCP admin token lists only personal pages");
            var created = await client.CallToolAsync("personal_pages_save", new Dictionary<string, object?> { ["accountId"] = account, ["revision"] = (await Scope(p => p.ListAsync())).Revision, ["definition"] = JsonSerializer.SerializeToElement(spec, json) });
            Check(created.IsError != true, "real MCP administration creates only an authorized page declaration");
            var state = created.StructuredContent!.Value;
            var createdId = state.GetProperty("pages").EnumerateArray().Last().GetProperty("id").GetGuid();
            var createdRevision = state.GetProperty("revision").GetGuid();
            Check((await client.CallToolAsync("personal_pages_read", new Dictionary<string, object?> { ["accountId"] = account, ["id"] = createdId })).IsError != true, "MCP reads exact personal declaration");
            var moveResult = await client.CallToolAsync("personal_pages_move", new Dictionary<string, object?> { ["accountId"] = account, ["id"] = createdId, ["revision"] = createdRevision, ["move"] = -1 });
            Check(moveResult.IsError != true, "MCP moves using collection concurrency revision");
            Check((await client.CallToolAsync("personal_pages_delete", new Dictionary<string, object?> { ["accountId"] = account, ["id"] = createdId, ["revision"] = moveResult.StructuredContent!.Value.GetProperty("revision").GetGuid() })).IsError != true, "MCP deletes an authorized declaration");
            Check((await client.CallToolAsync("personal_pages_read", new Dictionary<string, object?> { ["accountId"] = account, ["id"] = memberPage.Id })).IsError == true, "OAuth write scope does not reveal another member's page");
            var deny = await client.CallToolAsync("personal_pages_read", new Dictionary<string, object?> { ["accountId"] = foreign, ["id"] = kept.Id }); Check(deny.IsError == true, "admin OAuth scope does not bypass local account isolation");
        }
        var limited = await Scope(p => p.ListAsync());
        while (limited.Pages.Length < PersonalPageService.MaxPages) limited = await Scope(p => p.SaveAsync(null, limited.Revision, spec with { Title = "Limit " + limited.Pages.Length }));
        await FailsPage(() => Scope(p => p.SaveAsync(null, limited.Revision, spec)), "Limit");
        await using (var db = factory.CreateDbContext())
        {
            var template = await db.Agreements.FirstAsync(a => a.AccountId == account);
            for (var n = 0; n < 101; n++)
            {
                var building = new TenantPlatform.Core.Properties.Building { Id = Guid.NewGuid(), AccountId = account, Name = "Personal overflow " + n }; db.Buildings.Add(building);
                db.Agreements.Add(new Agreement { Id = Guid.NewGuid(), AccountId = account, BuildingId = building.Id, Title = "Personal overflow " + n, OwnerUserId = admin, CounterpartyOrganizationId = template.CounterpartyOrganizationId,
                    Type = AgreementType.Supplier, Status = AgreementStatus.Active, StartDate = new(2025,1,1), CurrentPeriodStartDate = new(2025,1,1), EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), CreatedByUserId = admin, UpdatedByUserId = admin, Revision = Guid.NewGuid() });
            }
            await db.SaveChangesAsync();
        }
        await FailsPage(() => Run(spec with { Filter = spec.Filter with { Search = "Personal overflow" } }), "Limit");
        Check((await Scope(p => p.ListAsync())).Pages.Length == 20, "page quota and upstream group limit fail explicitly without deleting saved definitions");
        modelOptions.Enabled = true; modelOptions.ApiKey = previousKey;
        Console.WriteLine("Personal pages real PostgreSQL/OAuth/MCP checks passed.");

        async Task<T> Scope<T>(Func<PersonalPageService, Task<T>> action, Guid? user = null, Guid? selected = null)
        {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var actor = new FixedActor(new(true, user ?? admin, selected ?? account));
            return await action(PersonalPagesComposition.Create(sp, actor));
        }
        async Task<PersonalPageResult> Run(PageDefinition d, int page = 1, Guid? savedId = null, Guid? user = null)
        {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var actor = new FixedActor(new(true, user ?? admin, account));
            var auth = new TenantPlatform.Application.Security.AuthorizationService(actor, sp.GetRequiredService<IAuthorizationData>(), sp.GetRequiredService<TimeProvider>());
            var registry = new McpRegistryService(actor, auth, sp.GetRequiredService<IMcpRegistryData>(), sp.GetRequiredService<IMcpAuthProfiles>(), sp.GetRequiredService<IMcpRegisteredClientFactory>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IMcpAccessDirectory>());
            return await new PersonalPageRunner(new(actor, auth, sp.GetRequiredService<IPersonalPageData>(), registry), registry, sp.GetRequiredService<PersonalPageRuns>(), sp.GetRequiredService<TimeProvider>()).RunAsync(d, page, savedId);
        }
        async Task<string> Token(bool write)
        {
            var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
            var query = QueryString.Create(new Dictionary<string,string?> { ["client_id"] = own.ClientId, ["redirect_uri"] = own.Callback, ["response_type"] = "code", ["scope"] = "tenantplatform.agreements.read" + (write ? " " + PersonalPageScopes.Manage : ""), ["resource"] = own.Resource, ["code_challenge_method"] = "S256", ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) });
            var consent = await browser.Get("/connect/authorize" + query); var fields = Fields(await consent.Content.ReadAsStringAsync()); fields["decision"] = "approve";
            var approved = await browser.Post("/connect/authorize", fields); var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(approved.Headers.Location!.Query)["code"].ToString();
            var response = await browser.Post("/connect/token", new() { ["grant_type"]="authorization_code", ["client_id"]=own.ClientId, ["client_secret"]=own.ClientSecret, ["code_verifier"]=verifier, ["code"]=code, ["redirect_uri"]=own.Callback, ["resource"]=own.Resource });
            Check(response.IsSuccessStatusCode, "actual issuer grants explicitly consented pages scope"); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString()!;
        }
        static async Task FailsPage(Func<Task> f, string code) { try { await f(); } catch (PersonalPageException e) when(e.Code == code) { return; } throw new Exception("Expected pages error " + code); }
        static async Task Denied(Func<Task> f) { try { await f(); } catch (UnauthorizedAccessException) { return; } throw new Exception("Expected page isolation denial"); }
    }
}
static class PagesDbExtensions
{
    public static async Task<T> WithDb<T>(this DbContextOptions<OAuthDbContext> options, Func<OAuthDbContext,Task<T>> action) { await using var db = new OAuthDbContext(options); return await action(db); }
}
static class PagesProtocol
{
    public static bool Block;
    public static TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static List<(string Name, JsonElement Args)> Calls = [];
    public static async Task Observe(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null) return;
        var raw = await request.Content.ReadAsStringAsync(ct);
        try { using var doc = JsonDocument.Parse(raw); if (doc.RootElement.TryGetProperty("method", out var method) && method.GetString() == "tools/call") { var p = doc.RootElement.GetProperty("params"); Calls.Add((p.GetProperty("name").GetString()!,p.GetProperty("arguments").Clone())); if (Block) { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan,ct); } } } catch(JsonException) { }
    }
}
