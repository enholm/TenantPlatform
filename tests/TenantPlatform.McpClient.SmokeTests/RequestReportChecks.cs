using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunRequestReports(WebApplication web, Factory factory, Guid admin, Guid account, Guid foreignAccount, Guid source, Browser browser, AgentProtocolFixture model)
    {
        var memory = web.Services.GetRequiredService<ReadAgentSessions>(); memory.ClearUser(admin);
        Check(!(await Pages(p => p.SourcesAsync(area: ReportArea.ServiceRequests))).Any(), "9E newly discovered tools are not report sources before explicit policy/approval");
        foreach (var tool in (await Tools(source)).Where(t => t.Name is "service_requests_query" or "service_requests_aggregate"))
        {
            Check(!tool.Approved, "9E new tool was not automatically approved");
            Check((await Post(browser, "tool-policy", new() { ["id"] = source.ToString(), ["tool"] = tool.Name, ["version"] = tool.Version, ["audience"] = "1", ["useClass"] = "1" })).StatusCode == System.Net.HttpStatusCode.Redirect, "9E explicit classification");
            await Post(browser, "approve", new() { ["id"] = source.ToString(), ["tool"] = tool.Name, ["version"] = tool.Version, ["approved"] = "true" });
        }
        var agreementQuery = (await Tools(source)).Single(t => t.Name == "agreements_query");
        if (!agreementQuery.Approved) {
            await Post(browser, "tool-policy", new() { ["id"] = source.ToString(), ["tool"] = agreementQuery.Name, ["version"] = agreementQuery.Version, ["audience"] = "1", ["useClass"] = "1" });
            await Post(browser, "approve", new() { ["id"] = source.ToString(), ["tool"] = agreementQuery.Name, ["version"] = agreementQuery.Version, ["approved"] = "true" });
        }
        var binding = (await Pages(p => p.SourcesAsync(area: ReportArea.ServiceRequests))).Single(s => s.ConnectionId == source);
        var definition = Guid.NewGuid(); var building = Guid.NewGuid(); var rows = new List<Guid>();
        await using (var db = factory.CreateDbContext())
        {
            var org = await db.Organizations.Where(o => o.AccountId == account).Select(o => o.Id).FirstAsync();
            db.Buildings.Add(new Building { Id = building, AccountId = account, Name = "9E building" });
            db.ServiceDefinitions.Add(new ServiceDefinition { Id = definition, AccountId = account, Code = "9E-report" });
            for (var i = 0; i < 28; i++)
            {
                var id = Guid.NewGuid(); rows.Add(id);
                db.ServiceRequests.Add(new ServiceRequest { Id = id, AccountId = i == 27 ? foreignAccount : account, ServiceDefinitionId = definition,
                    RequesterUserId = admin, RequesterOrganizationId = org, BuildingId = building, Title = i == 0 ? null : "9E request " + i,
                    Status = i % 2 == 0 ? ServiceRequestStatus.Submitted : ServiceRequestStatus.Completed,
                    CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i), ReplyToken = Guid.NewGuid().ToString("N") });
            }
            await db.SaveChangesAsync();
        }
        var proposal = new ReportPageProposal("Mine forespørsler", new(PagePeriodKind.All, null), new(),
            [PageComponent.Metric, PageComponent.BuildingChart, PageComponent.AgreementTable], GroupBy: AgreementReportGrouping.Status,
            Columns: [PageColumn.Status, PageColumn.Title, PageColumn.Building, PageColumn.CreatedAt], Area: ReportArea.ServiceRequests, RequestFilter: new());
        Check((await Agent(a => a.ConnectionsAsync())).Single(c => c.Id == source).Areas.Select(x => x.Area).Order().SequenceEqual(new[] { ReadAgentArea.Agreements, ReadAgentArea.ServiceRequests, ReadAgentArea.AgreementDetails, ReadAgentArea.AgreementFinance }), "area choices intersect actual available approved tools");
        var areaConversation = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.Agreements));
        model.OtherTool = ReadAgentService.ModelName((await Scope(sp => sp.GetRequiredService<McpRegistryService>().AvailableToolsAsync())).Single(t => t.ConnectionId == source && t.Name == "service_requests_query"));
        try { await Agent(a => a.AskAsync(areaConversation, "cross")); throw new Exception("cross-area tool accepted"); }
        catch (ReadAgentException e) when (e.Code == "InvalidTool") { Check(true, "model alias for a personally approved tool from another area is still rejected"); }
        var switched = await Agent(a => a.SwitchAreaAsync(areaConversation, ReadAgentArea.ServiceRequests, true));
        Check(await Agent(a => a.AreaAsync(switched)) == ReadAgentArea.ServiceRequests, "switch binds new area in Application");
        try { await Agent(a => a.AskAsync(areaConversation, "history-only")); throw new Exception("old area conversation survived"); }
        catch (UnauthorizedAccessException) { Check(true, "switch deletes old context"); }
        await Agent(async a => { await a.DeleteAsync(switched); return true; });
        var ordinary = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.ServiceRequests));
        var answer = await Agent(a => a.AskAsync(ordinary, "request-aggregate"));
        Check(answer.Sources.Any(s => s.Tool == "service_requests_aggregate" && s.Aggregate is { TotalCount: 27, Complete: true, GroupsOverlap: false }), "9E ordinary agent has fresh request aggregate provenance");
        var modelBeforeHistory = model.Payloads.Count;
        await using (var db = factory.CreateDbContext()) { (await db.ServiceRequests.SingleAsync(r => r.Id == rows[0])).Status = ServiceRequestStatus.Completed; await db.SaveChangesAsync(); }
        var freshAnswer = await Agent(a => a.AskAsync(ordinary, "history-only"));
        Check(freshAnswer.ContextInvalidated && model.Payloads.Count == modelBeforeHistory + 1 && model.Payloads.Skip(modelBeforeHistory).All(p => !p.Contains("untrustedPreviousAnswer")), "9E changed request evidence discards retained conclusions before another model call");
        await using (var db = factory.CreateDbContext()) { (await db.ServiceRequests.SingleAsync(r => r.Id == rows[0])).Status = ServiceRequestStatus.Submitted; await db.SaveChangesAsync(); }
        memory.ClearUser(admin);
        var before = await Pages(p => p.ListAsync());
        var conversation = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.ServiceRequests));
        Check(await Agent(a => a.AreaAsync(conversation)) == ReadAgentArea.ServiceRequests, "report conversation is request-bound");
        var beforeLimit = model.Payloads.Count;
        try {
            await Scope(sp => {
                var who = new FixedActor(new(true, admin, account));
                var auth = new TenantPlatform.Application.Security.AuthorizationService(who, sp.GetRequiredService<TenantPlatform.Application.Security.IAuthorizationData>(), sp.GetRequiredService<TimeProvider>());
                var limited = new ReadAgentService(who, auth, sp.GetRequiredService<McpRegistryService>(), sp.GetRequiredService<IReadAgentModel>(), sp.GetRequiredService<IAgentSchemaValidator>(), memory,
                    new ReadAgentLimits { Tools = 2 }, sp.GetRequiredService<AgentReportDrafts>());
                return limited.AskAsync(conversation, "Lag en rapportside");
            });
            throw new Exception("proposal tool omitted from catalogue limit");
        } catch (ReadAgentException e) when (e.Code == "ToolLimit") { Check(model.Payloads.Count == beforeLimit, "catalogue limit includes proposal tool after area selection, before any model call"); }
        var preview = await Ask("Lag en side Mine forespørsler med antall tjenesteforespørsler, statusdiagram og paginert tabell. Ta med alle datoer.");
        Check(preview.Draft.Definition is { Version: 3, Area: ReportArea.ServiceRequests } && preview.Preview.Selection is null && preview.Preview.Table is null && preview.Preview.Requests is { Aggregate.TotalCount: 27, Table.ReturnedCount: 25, Table.HasMore: true }, "9E synthetic prompt -> actual MCP preview with typed request domain");
        using (var declaration = JsonDocument.Parse(model.Payloads.Last())) {
            var tool = declaration.RootElement.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == AgentReportDrafts.ToolName);
            Check(tool.GetProperty("parameters").GetProperty("properties").GetProperty("area").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "ServiceRequests" }), "proposal schema is restricted to the server-bound report area");
        }
        Check((await Pages(p => p.ListAsync())).Revision == before.Revision, "9E proposal/preview never saves implicitly");
        var saved = await Agent(a => a.SaveReportAsync(conversation, preview.Draft.Id, preview.Draft.Revision));
        var stored = await Pages(p => p.ReadAsync(saved));
        Check(stored.Definition.RequestFilter == new TenantPlatform.Application.ServiceRequests.ServiceRequestReportFilter() && stored.Definition.Filter == new AgreementReportFilter(), "9E saved definition keeps domain filters separate");
        var html = await (await browser.Get("/my-pages/" + saved)).Content.ReadAsStringAsync();
        Check(html.Contains("data-area=\"ServiceRequests\"") && html.Contains("data-request-status-submitted") && html.Contains("name=\"showColumnCreatedAt\"") && !html.Contains("name=\"showColumnEndDate\""), "9E real SSR uses request area, localized statuses and request-only columns");
        var manualRequests = await (await browser.Get("/my-pages?area=ServiceRequests")).Content.ReadAsStringAsync();
        Check(manualRequests.Contains("data-area=\"ServiceRequests\"") && manualRequests.Contains("data-report-area") && !manualRequests.Contains("name=\"showColumnEndDate\""), "manual configurator chooses a supported report area independently of chat");
        var options = web.Services.GetRequiredService<IOptions<ReadAgentOptions>>().Value; var enabled = options.Enabled; options.Enabled = false;
        var modelCount = model.Payloads.Count; var calls = PagesProtocol.Calls.Count;
        var run = await Run(stored.Definition, 2, saved);
        Check(run.Requests is { Aggregate.TotalCount: 27, Table.Page: 2, Table.ReturnedCount: 2 } && modelCount == model.Payloads.Count, "9E saved page deterministic paging with AI disabled");
        Check(PagesProtocol.Calls.Skip(calls).Select(c => c.Name).SequenceEqual(new[] { "service_requests_aggregate", "service_requests_query" }), "9E only version-bound personal registry calls, no fallback");
        options.Enabled = enabled;
        memory.ClearUser(admin);
        var opened = await Agent(a => a.BeginEditAsync(saved, account, true)); conversation = opened.Conversation;
        Check(await Agent(a => a.AreaAsync(opened.Conversation)) == ReadAgentArea.ServiceRequests, "stored page backend determines edit area");
        proposal = opened.Report.Draft.Intent with { Title = "Fullførte forespørsler", RequestFilter = new(Status: ServiceRequestStatus.Completed), GroupBy = AgreementReportGrouping.Building };
        preview = await Ask("Endre siden til fullførte tjenesteforespørsler og grupper etter bygg.");
        Check(preview.Preview.Requests!.Aggregate!.TotalCount == 13 && AgentReportDrafts.Changes(preview.Draft).Any(c => c.Field == "RequestStatus"), "9E edit preview and typed change summary");
        Check((await Pages(p => p.ReadAsync(saved))).Definition.Title == "Mine forespørsler", "9E original unchanged before explicit update");
        Check(await Agent(a => a.SaveReportAsync(conversation, preview.Draft.Id, preview.Draft.Revision)) == saved, "9E explicit update preserves saved identity");
        var updated = (await Pages(p => p.ReadAsync(saved))).Definition;
        Check((await Run(updated, 1, saved)).Requests!.Table!.Items.All(r => r.Status == "Completed"), "9E stored status filter survives reopen and run");
        foreach (var bad in new[] { updated with { Area = ReportArea.Agreements }, updated with { Filter = new(Status: TenantPlatform.Core.Agreements.AgreementStatus.Active) }, updated with { Period = new(PagePeriodKind.NextMonths) }, updated with { Columns = [PageColumn.EndDate] }, updated with { RequestFilter = null } })
            await PageFailure(() => Run(bad), null);
        var safeCollection = await Pages(p => p.ListAsync()); var invalidRevision = Guid.NewGuid();
        await Scope(async sp => { await sp.GetRequiredService<IPersonalPageData>().SaveAsync(account, admin, safeCollection.Revision,
            safeCollection with { Revision = invalidRevision, Pages = safeCollection.Pages.Select(p => p.Id == saved ? p with { Definition = p.Definition with { Area = (ReportArea)999 } } : p).ToArray() }, default); return true; });
        await PageFailure(() => Pages(p => p.ListAsync()), "Version");
        await PageFailure(() => Pages(p => p.SaveAsync(null, invalidRevision, updated)), "Version");
        await Scope(async sp => { await sp.GetRequiredService<IPersonalPageData>().SaveAsync(account, admin, invalidRevision, safeCollection, default); return true; });
        // Required tool approval revokes access before data; no AI/direct-data fallback.
        await Approval("service_requests_aggregate", false);
        calls = PagesProtocol.Calls.Count; await PageFailure(() => Run(updated), "SourceChanged");
        Check(PagesProtocol.Calls.Count == calls, "9E revoked approval prevents report data call");
        await Approval("service_requests_aggregate", true);
        await using (var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()))
            await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == "service_requests_query").ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, "changed-9E"));
        await PageFailure(() => Run(updated), "SourceChanged");
        Check((await Pages(p => p.ReadAsync(saved))).Definition.QueryVersion == binding.QueryVersion, "9E no automatic upgrade of saved request tool version");
        await using (var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()))
            await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == "service_requests_query").ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, binding.QueryVersion));
        // Backend edit binding forbids switching domains; model cannot choose a target/source/owner.
        memory.ClearUser(admin); opened = await Agent(a => a.BeginEditAsync(saved, account, true)); conversation = opened.Conversation;
        proposal = opened.Report.Draft.Intent with { Area = ReportArea.Agreements, RequestFilter = null };
        try { await Ask("Endre rapporten til avtaler."); throw new Exception("Expected domain switch rejection"); } catch (ReadAgentException e) when (e.Code == "DraftInvalid") { }
        var owner = new TenantPlatform.Application.Security.ActorSnapshot(true, admin, account);
        Guid resetConversation;
        using (var active = memory.Start(conversation, owner, new ReadAgentLimits(), default)) {
            Check(active.Draft is not null, "switch fixture retains an actual unsaved edit draft");
            try { await Agent(a => a.SwitchAreaAsync(conversation, ReadAgentArea.Agreements, false)); throw new Exception("unconfirmed reset accepted"); }
            catch (ReadAgentException e) when (e.Code == "AreaInvalid") { Check(!active.Token.IsCancellationRequested, "unconfirmed reset preserves old work"); }
            resetConversation = await Agent(a => a.SwitchAreaAsync(conversation, ReadAgentArea.Agreements, true));
            Check(active.Token.IsCancellationRequested, "server area switch cancels active lease");
            try { active.Commit(new("late", new("late private answer", [], []), []), active.Draft); throw new Exception("late area result committed"); }
            catch (ReadAgentException e) when (e.Code == "CancelledOrTimedOut") { Check(true, "late completion cannot resurrect switched conversation or draft"); }
        }
        using (var clean = memory.Start(resetConversation, owner, new ReadAgentLimits(), default))
            Check(clean.Draft is null && clean.History.Count == 0 && memory.Connection(resetConversation, owner) == source && memory.Area(resetConversation, owner) == ReadAgentArea.Agreements, "replacement has same connection, new area, no history, source context or unsaved draft");
        model.ReportArguments = null; memory.ClearUser(admin);
        var collection = await Pages(p => p.ListAsync()); await Pages(p => p.ChangeAsync(saved, collection.Revision, null));
        await using (var db = factory.CreateDbContext()) { await db.ServiceRequests.Where(r => rows.Contains(r.Id)).ExecuteDeleteAsync(); await db.ServiceDefinitions.Where(d => d.Id == definition).ExecuteDeleteAsync(); await db.Buildings.Where(b => b.Id == building).ExecuteDeleteAsync(); }
        Console.WriteLine("PASS 9E report vertical: synthetic prompt, actual personal OAuth/MCP, preview/save/edit, AI-off and revocation");

        async Task<List<McpDiscoveredTool>> Tools(Guid id) { await using var scope = web.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Set<McpDiscoveredTool>().AsNoTracking().Where(t => t.ConnectionId == id).ToListAsync(); }
        async Task<HttpResponseMessage> Post(Browser current, string action, Dictionary<string, string> fields) {
            var page = Fields(await (await current.Get("/mcp-client/registry")).Content.ReadAsStringAsync()); fields["__RequestVerificationToken"] = page["__RequestVerificationToken"];
            return await current.Post("/mcp-client/registry/" + action, fields);
        }
        async Task<AgentReportPreview> Ask(string prompt) { model.ReportArguments = JsonSerializer.Serialize(proposal, AgentReportDrafts.Json); return (await Agent(a => a.AskAsync(conversation, prompt))).Report!; }
        async Task Approval(string name, bool approved) { await using var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()); await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == name).ExecuteUpdateAsync(s => s.SetProperty(t => t.Approved, approved)); }
        Task<PersonalPageResult> Run(PageDefinition d, int page = 1, Guid? savedId = null) => Scope(sp => sp.GetRequiredService<PersonalPageRunner>().RunAsync(d, page, savedId));
        Task<T> Pages<T>(Func<PersonalPageService, Task<T>> action) => Scope(sp => action(sp.GetRequiredService<PersonalPageService>()));
        Task<T> Agent<T>(Func<ReadAgentService, Task<T>> action) => Scope(sp => action(sp.GetRequiredService<ReadAgentService>()));
        async Task<T> Scope<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = sp, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(TenantPlatformClaimTypes.UserId, admin.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, account.ToString()) }, "trusted-fixture")) };
            try { return await action(sp); } finally { accessor.HttpContext = previous; }
        }
        static async Task PageFailure(Func<Task> action, string? code) { try { await action(); } catch (PersonalPageException e) when (code is null || e.Code == code) { return; } throw new Exception("Expected page error " + code); }
    }
}
