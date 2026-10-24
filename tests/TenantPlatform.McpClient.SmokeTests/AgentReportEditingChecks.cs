using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunReportEditing(WebApplication web, Factory factory, Guid admin, Guid member, Guid account, Guid foreign,
        Guid source, Browser browser, AgentProtocolFixture model)
    {
        var memory = web.Services.GetRequiredService<ReadAgentSessions>(); memory.ClearUser(admin);
        var binding = (await Pages(p => p.SourcesAsync())).Single(s => s.ConnectionId == source);
        var building = Guid.NewGuid();
        await using (var db = factory.CreateDbContext()) { db.Buildings.Add(new Building { Id = building, AccountId = account, Name = "9C stored filter" }); await db.SaveChangesAsync(); }
        var original = new PageDefinition(1, "9C original <script>personal_pages_save</script>", source, binding.QueryVersion, binding.AggregateVersion,
            new(PagePeriodKind.NextMonths, 3), new(Search: "9C report", Archive: AgreementArchiveSelection.Exclude, BuildingId: building),
            [PageComponent.Metric, PageComponent.BuildingChart, PageComponent.AgreementTable], ProfileVersion: binding.ProfileVersion);
        var before = await Pages(p => p.ListAsync());
        Check(before.Pages.Length == 0, "9C fixture starts without other personal pages");
        var saved = await Pages(p => p.SaveAsync(null, before.Revision, original)); var id = saved.Pages.Single().Id; original = saved.Pages.Single().Definition;
        var pageHtml = await (await browser.Get("/my-pages/" + id)).Content.ReadAsStringAsync();
        Check(pageHtml.Contains("/agent?editPage=" + id), "9C saved page offers an explicit AI edit link");
        var html = await (await browser.Get("/agent?editPage=" + id)).Content.ReadAsStringAsync();
        var csrf = WebUtility.HtmlDecode(Regex.Match(html, "data-csrf=\"([^\"]+)\"").Groups[1].Value);
        Check(html.Contains("data-edit-page=\"" + id) && !html.Contains(original.Title), "9C SSR binds page selection and HTML-encodes untrusted title");
        using (var noCsrf = await Http("report-edit", new { pageId = id, account, sharingAccepted = true }, false))
            Check(noCsrf.StatusCode == HttpStatusCode.BadRequest, "9C opening edit requires antiforgery");
        await Denied(() => Scope(sp => sp.GetRequiredService<ReadAgentService>().BeginEditAsync(id, account, true), member));
        await Denied(() => Scope(sp => sp.GetRequiredService<ReadAgentService>().BeginEditAsync(id, foreign, true), selected: foreign));
        await using (var db = factory.CreateDbContext()) {
            var otherMembership = new UserAccount { Id = Guid.NewGuid(), UserId = admin, AccountId = foreign };
            var role = new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = otherMembership.Id, Role = UserRole.AccountAdmin };
            db.Add(otherMembership); db.Add(role); await db.SaveChangesAsync();
            Check((await Scope(sp => sp.GetRequiredService<PersonalPageService>().ListAsync(), selected: foreign)).Pages.Length == 0,
                "9C valid administration in another account cannot read this account's personal pages");
            await Denied(() => Scope(sp => sp.GetRequiredService<ReadAgentService>().BeginEditAsync(id, foreign, true), selected: foreign));
            db.Remove(role); db.Remove(otherMembership); await db.SaveChangesAsync();
        }
        await Denied(() => Agent(a => a.BeginEditAsync(Guid.NewGuid(), account, true)));
        var count = model.Payloads.Count;
        var opened = await Agent(a => a.BeginEditAsync(id, account, true)); var conversation = opened.Conversation;
        Check(model.Payloads.Count == count && opened.Report.Draft.Edit?.PageId == id && opened.Report.Draft.CollectionRevision == saved.Revision &&
            AgentReportDrafts.Changes(opened.Report.Draft).Length == 0, "9C opening uses fresh authorized backend original and actual MCP preview, without a model");
        using (var begin = await Http("report-edit", new { pageId = id, account, sharingAccepted = true })) {
            var body = await begin.Content.ReadFromJsonAsync<JsonElement>(); var report = body.GetProperty("report");
            Check(begin.IsSuccessStatusCode && report.GetProperty("editingPageId").GetGuid() == id && report.GetProperty("changes").GetArrayLength() == 0,
                "9C real cookie/CSRF opening returns backend-bound edit context and deterministic preview");
            using var discard = await Http("report-discard", new { conversation = body.GetProperty("conversation").GetGuid(), draft = report.GetProperty("id").GetGuid(), revision = report.GetProperty("revision").GetGuid() });
            Check(discard.IsSuccessStatusCode, "9C explicit HTTP discard removes the edit session");
        }
        var intent = opened.Report.Draft.Intent with { Period = new(PagePeriodKind.NextMonths, 6) };
        var changed = await Ask("Vis de neste seks månedene i stedet for tre.");
        intent = intent with { Title = "Kommende avtaleutløp", Filter = intent.Filter with { Status = AgreementStatus.Active } };
        changed = await Ask("Ta bare med aktive avtaler og endre tittelen til Kommende avtaleutløp.");
        intent = intent with { Components = [PageComponent.Metric, PageComponent.AgreementTable, PageComponent.BuildingChart] };
        changed = await Ask("Flytt avtaletabellen over diagrammet."); var draft = changed.Draft;
        var diff = AgentReportDrafts.Changes(draft);
        Check(diff.Select(x => x.Field).SequenceEqual(new[] { "Title", "Period", "Status", "Components" }) &&
            diff.Single(x => x.Field == "Period").Before.GetProperty("months").GetInt32() == 3 &&
            diff.Single(x => x.Field == "Period").After.GetProperty("months").GetInt32() == 6,
            "9C change overview is computed from original and proposed definitions, including component order");
        Check(draft.Definition.Filter.BuildingId == building && JsonSerializer.Serialize((await Pages(p => p.ReadAsync(id))).Definition) == JsonSerializer.Serialize(original) &&
            (await Pages(p => p.ListAsync())).Revision == saved.Revision, "9C proposals preserve stored references, original and list revision before explicit saving");
        Check(!model.Payloads.Last().Contains(id.ToString()) && !model.Payloads.Last().Contains(draft.TargetPageId.ToString()) &&
            model.Payloads.Last().Contains("editingExistingPage"), "9C model sees editable intent without target identity or commit authority");
        var injected = JsonNode.Parse(JsonSerializer.Serialize(intent, AgentReportDrafts.Json))!.AsObject();
        injected["pageId"] = Guid.NewGuid().ToString(); injected["ownerUserId"] = member.ToString(); model.ReportArguments = injected.ToJsonString();
        await Failure(() => Agent(a => a.AskAsync(conversation, "Endre tittelen")), "Invalid");
        var oldTool = model.OtherTool; model.OtherTool = "personal_pages_save"; model.ReportArguments = null;
        await Failure(() => Agent(a => a.AskAsync(conversation, "cross")), "InvalidTool");
        model.OtherTool = oldTool; model.ReportArguments = JsonSerializer.Serialize(intent, AgentReportDrafts.Json);
        Check((await Pages(p => p.ListAsync())).Revision == saved.Revision, "9C injected target/owner fields and a model administration call have no persistence effect");
        using (var noCsrf = await Http("report-update", new { conversation, draft = draft.Id, revision = draft.Revision }, false))
            Check(noCsrf.StatusCode == HttpStatusCode.BadRequest, "9C update requires explicit antiforgery-protected action");
        // Simulate a committed update whose response never reached the conversation/browser.
        await Pages(p => p.UpdateFromDraftAsync(id, draft.CollectionRevision, draft.Revision, draft.Edit!.Original, draft.Definition));
        using (var retry = await Http("report-update", new { conversation, draft = draft.Id, revision = draft.Revision })) {
            var reply = await retry.Content.ReadFromJsonAsync<JsonElement>();
            Check(retry.IsSuccessStatusCode && reply.GetProperty("pageId").GetGuid() == id, "9C lost update response is recovered using the exact server commit revision");
        }
        var committed = await Pages(p => p.ListAsync());
        var retries = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { using var r = await Http("report-update", new { conversation, draft = draft.Id, revision = draft.Revision }); return r.StatusCode; }));
        Check(retries.All(x => x is HttpStatusCode.OK or HttpStatusCode.TooManyRequests) && (await Pages(p => p.ListAsync())).Revision == committed.Revision && committed.Pages.Length == 1,
            "9C duplicate update clicks are idempotent and do not change revision again");
        var options = web.Services.GetRequiredService<IOptions<ReadAgentOptions>>().Value; options.Enabled = false;
        var calls = model.Payloads.Count;
        await Scope(sp => sp.GetRequiredService<PersonalPageRunner>().RunAsync(draft.Definition)); options.Enabled = true;
        Check(model.Payloads.Count == calls && (await Pages(p => p.ReadAsync(id))).Definition.Period.Months == 6, "9C updated page runs deterministically without AI");

        await Window("copy, discard and conflict");
        opened = await Agent(a => a.BeginEditAsync(id, account, true)); conversation = opened.Conversation;
        intent = opened.Report.Draft.Intent with { Title = "9C copied report" }; draft = (await Ask("Endre tittelen til 9C copied report.")).Draft;
        await Pages(p => p.CreateFromDraftAsync(draft.TargetPageId, draft.CollectionRevision, draft.Definition)); // Lost copy response.
        var copied = await Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision, copy: true));
        Check(copied == draft.TargetPageId && copied != id && await Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision, copy: true)) == copied &&
            (await Pages(p => p.ListAsync())).Pages.Length == 2 && (await Pages(p => p.ReadAsync(id))).Definition.Title == "Kommende avtaleutløp",
            "9C copy has one backend identity, survives lost response and never updates original");
        using (var copyReply = await Http("report-copy", new { conversation, draft = draft.Id, revision = draft.Revision }))
            Check(copyReply.IsSuccessStatusCode && (await copyReply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pageId").GetGuid() == copied,
                "9C copy HTTP action confirms the canonical backend page identity");
        await Failure(() => Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision)), "Conflict");
        opened = await Agent(a => a.BeginEditAsync(id, account, true)); var unchanged = await Pages(p => p.ListAsync());
        await Agent(async a => { await a.DiscardReportAsync(opened.Conversation, opened.Report.Draft.Id, opened.Report.Draft.Revision); return true; });
        Check((await Pages(p => p.ListAsync())).Revision == unchanged.Revision, "9C discard deletes edit context without a database write");
        await Denied(() => Agent(a => a.PreviewReportAsync(opened.Conversation, opened.Report.Draft.Id, opened.Report.Draft.Revision)));
        opened = await Agent(a => a.BeginEditAsync(id, account, true)); conversation = opened.Conversation; draft = opened.Report.Draft;
        var current = await Pages(p => p.ListAsync());
        await Pages(p => p.SaveAsync(id, current.Revision, draft.Definition with { Title = "Manual concurrent edit" }));
        await Failure(() => Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision)), "Conflict");
        Check((await Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision))).Draft.Revision == draft.Revision,
            "9C conflict preserves old draft without rebasing it onto a new original");
        var reloaded = await Agent(a => a.ReloadReportAsync(conversation, draft.Id, draft.Revision));
        Check(reloaded.Conversation != conversation && reloaded.Report.Draft.Edit!.Original.Title == "Manual concurrent edit" &&
            AgentReportDrafts.Changes(reloaded.Report.Draft).Length == 0, "9C explicit reload creates isolated context from current original and never reapplies old intent");
        await Denied(() => Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision)));

        await Window("revocation, cancellation and lifecycle");
        memory.ClearUser(admin);
        opened = await Agent(a => a.BeginEditAsync(id, account, true)); conversation = opened.Conversation; draft = opened.Report.Draft;
        await using (var scope = web.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<OAuthDbContext>(); var tool = await db.Set<McpDiscoveredTool>().SingleAsync(t => t.ConnectionId == source && t.Name == "agreements_query");
            tool.Approved = false; await db.SaveChangesAsync();
            await Failure(() => Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision)), "SourceChanged");
            await Failure(() => Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision)), "SourceChanged");
            await Failure(() => Agent(a => a.ReloadReportAsync(conversation, draft.Id, draft.Revision)), "SourceChanged");
            tool.Approved = true; var version = tool.Version; tool.Version += "-changed"; await db.SaveChangesAsync();
            await Failure(() => Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision)), "SourceChanged");
            tool.Version = version; await db.SaveChangesAsync();
        }
        Check((await Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision))).Draft.Revision == draft.Revision,
            "9C failed reload and revoked/version-changed tools preserve intent but never upgrade bindings");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.ReportArguments = JsonSerializer.Serialize(draft.Intent with { Title = "late unwanted edit" }, AgentReportDrafts.Json);
        model.BeforeReport = async () => { entered.SetResult(); await release.Task; };
        var late = Agent(a => a.AskAsync(conversation, "Endre tittelen")); await entered.Task;
        await Agent(async a => { await a.CancelAsync(conversation); return true; }); release.SetResult();
        await Failure(() => late, "CancelledOrTimedOut");
        Check((await Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision))).Draft.Revision == draft.Revision, "9C cancelled late change cannot replace edit baseline or last valid revision");
        var timer = new AdjustableClock(); using (var store = new ReadAgentSessions(timer)) {
            var actor = new ActorSnapshot(true, admin, account); var key = store.Create(actor, source);
            using (var lease = store.Start(key, actor, new(), default)) lease.CompleteDraft(draft);
            using var active = store.Start(key, actor, new(), default); timer.Advance(TimeSpan.FromMinutes(31));
            await Denied(() => Task.FromResult(store.Connection(key, actor)));
            Check(active.Token.IsCancellationRequested, "9C expiry removes original/edit snapshot and cancels active work");
        }
        await using (var db = factory.CreateDbContext()) {
            var membership = await db.UserAccounts.AsNoTracking().SingleAsync(x => x.UserId == admin && x.AccountId == account);
            var roles = await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccountId == membership.Id).ToArrayAsync();
            await db.UserAccountRoles.Where(x => x.UserAccountId == membership.Id).ExecuteDeleteAsync();
            await db.UserAccounts.Where(x => x.Id == membership.Id).ExecuteDeleteAsync();
            await Denied(() => Agent(a => a.SaveReportAsync(conversation, draft.Id, draft.Revision)));
            db.UserAccounts.Add(membership); db.UserAccountRoles.AddRange(roles); await db.SaveChangesAsync();
        }
        await Denied(() => Agent(a => a.PreviewReportAsync(conversation, draft.Id, draft.Revision)));
        model.ReportArguments = null; memory.ClearUser(admin);
        opened = await Agent(a => a.BeginEditAsync(id, account, true));
        current = await Pages(p => p.ListAsync()); await Pages(p => p.ChangeAsync(id, current.Revision, null));
        await Denied(() => Agent(a => a.PreviewReportAsync(opened.Conversation, opened.Report.Draft.Id, opened.Report.Draft.Revision)));
        current = await Pages(p => p.ListAsync()); await Pages(p => p.ChangeAsync(copied, current.Revision, null));
        await using (var db = factory.CreateDbContext()) { await db.Buildings.Where(b => b.Id == building).ExecuteDeleteAsync(); }
        memory.ClearUser(admin);
        Console.WriteLine("Saved report editing real PostgreSQL/OAuth/MCP checks passed.");

        async Task<AgentReportPreview> Ask(string prompt) { model.ReportArguments = JsonSerializer.Serialize(intent, AgentReportDrafts.Json); return (await Agent(a => a.AskAsync(conversation, prompt))).Report!; }
        async Task<T> Agent<T>(Func<ReadAgentService, Task<T>> action) => await Scope(sp => action(sp.GetRequiredService<ReadAgentService>()));
        async Task<T> Pages<T>(Func<PersonalPageService, Task<T>> action) => await Scope(sp => action(sp.GetRequiredService<PersonalPageService>()));
        async Task<T> Scope<T>(Func<IServiceProvider, Task<T>> action, Guid? user = null, Guid? selected = null) {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = sp, User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(TenantPlatformClaimTypes.UserId, (user ?? admin).ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, (selected ?? account).ToString()) }, "trusted-fixture")) };
            try { return await action(sp); } finally { accessor.HttpContext = previous; }
        }
        async Task<HttpResponseMessage> Http(string action, object body, bool token = true) {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://tenant.test/agent/" + action) { Content = JsonContent.Create(body) };
            request.Headers.Add("Cookie", string.Join("; ", browser.Cookies.Select(p => p.Key + "=" + p.Value))); if (token) request.Headers.Add("RequestVerificationToken", csrf);
            return await web.GetTestClient().SendAsync(request);
        }
        static async Task Window(string name) { Console.WriteLine("Isolating 9C " + name + " quota window."); await Task.Delay(TimeSpan.FromSeconds(61)); }
        static async Task Denied(Func<Task> f) { try { await f(); } catch (UnauthorizedAccessException) { return; } throw new Exception("Expected denied editing operation"); }
        static async Task Failure(Func<Task> f, string code) { try { await f(); } catch (PersonalPageException e) when (e.Code == code) { return; } catch (ReadAgentException e) when (e.Code == "Draft" + code || e.Code == code) { return; } throw new Exception("Expected editing error " + code); }
    }
}
