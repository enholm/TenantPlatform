using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunBuildingAgent(WebApplication web, Factory factory, Guid admin, Guid account, Guid source, Browser browser, AgentProtocolFixture model)
    {
        var memory = web.Services.GetRequiredService<ReadAgentSessions>(); memory.ClearUser(admin);
        model.ReportArguments = null;
        foreach (var tool in (await Tools(source)).Where(t => t.Name is "buildings_query" or "buildings_aggregate"))
        {
            Check(!tool.Approved, "structure tools remain unapproved until explicit exact-version approval");
            await Post(browser, "tool-policy", new() { ["id"] = source.ToString(), ["tool"] = tool.Name, ["version"] = tool.Version, ["audience"] = "1", ["useClass"] = "1" });
            await Post(browser, "approve", new() { ["id"] = source.ToString(), ["tool"] = tool.Name, ["version"] = tool.Version, ["approved"] = "true" });
        }
        var building = Guid.NewGuid();
        await using (var db = factory.CreateDbContext()) { db.Buildings.Add(new Building { Id = building, AccountId = account, Name = "STR agent building", City = "City A" }); await db.SaveChangesAsync(); }
        var connections = await Agent(a => a.ConnectionsAsync());
        Check(connections.Single(c => c.Id == source).Areas.Single(a => a.Area == ReadAgentArea.Structure).ToolCount == 2, "structure has exactly the two approved read tools");
        Check(!ReadAgentAreas.Reports(ReadAgentArea.Structure) && !ReadAgentAreas.Includes(ReadAgentArea.Structure, false, "agreements_query") &&
            !ReadAgentAreas.Includes(ReadAgentArea.Agreements, false, "buildings_query") && !ReadAgentAreas.Includes(ReadAgentArea.Structure, true, "buildings_query"), "exact mapping and external area preserved, no report adapter");
        var conversation = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.Structure));
        var answer = await Agent(a => a.AskAsync(conversation, "building-query"));
        Check(answer.Sources.Single().Tool == "buildings_query" && answer.Sources.Single().Page is { PageSize: 25, SnapshotConsistent: false } && answer.Report == null, "actual registered OAuth/MCP building list source facts");
        var payload = model.Payloads.Last();
        Check(payload.Contains("building LIST") && !payload.Contains("Read authorized agreements") && !payload.Contains("propose_report_page\",\"parameters"), "model only receives structure catalogue and no proposal tool");
        await using (var db = factory.CreateDbContext()) { await db.Buildings.Where(b => b.Id == building).ExecuteUpdateAsync(b => b.SetProperty(x => x.Name, "Changed STR name")); }
        answer = await Agent(a => a.AskAsync(conversation, "building-aggregate"));
        Check(answer.ContextInvalidated && answer.Sources.Any(s => s.Tool == "buildings_aggregate" && s.Aggregate is { GroupBy: "City", Complete: true, GroupsOverlap: false }), "changed building data invalidates old derived history; new aggregate has real provenance");
        var reportConversation = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.Structure));
        answer = await Agent(a => a.AskAsync(reportConversation, "Lag en side med bygg"));
        Check(answer.Report == null && answer.Notices.Contains("StructureReportsUnavailable"), "building report wishes explained without draft capability");
        var agreementTool = (await Tools(source)).Single(t => t.Name == "agreements_query");
        if (!agreementTool.Approved) {
            await Post(browser, "tool-policy", new() { ["id"] = source.ToString(), ["tool"] = agreementTool.Name, ["version"] = agreementTool.Version, ["audience"] = "1", ["useClass"] = "1" });
            await Post(browser, "approve", new() { ["id"] = source.ToString(), ["tool"] = agreementTool.Name, ["version"] = agreementTool.Version, ["approved"] = "true" });
        }
        var tools = await Scope(sp => sp.GetRequiredService<McpRegistryService>().AvailableToolsAsync());
        model.OtherTool = ReadAgentService.ModelName(tools.Single(t => t.ConnectionId == source && t.Name == "agreements_query"));
        var cross = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.Structure));
        await Failure(() => Agent(a => a.AskAsync(cross, "cross")), "InvalidTool");
        var revoked = await Agent(a => a.BeginAsync(source, account, true, ReadAgentArea.Structure));
        await Agent(a => a.AskAsync(revoked, "building-query"));
        await using (var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()))
            await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == "buildings_query").ExecuteUpdateAsync(t => t.SetProperty(x => x.Approved, false));
        answer = await Agent(a => a.AskAsync(revoked, "history-only"));
        Check(answer.ContextInvalidated && answer.Sources.Count == 0 && !model.Payloads.Last().Contains("untrustedMcpResult"), "revoked approval invalidates history and removes old data before model reuse");
        memory.ClearUser(admin);
        Console.WriteLine("PASS STR-001 agent: exact Structure catalogue, actual OAuth/MCP, provenance, invalidated history, no reports and revoked approval");
        async Task<List<McpDiscoveredTool>> Tools(Guid id) { await using var scope = web.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Set<McpDiscoveredTool>().AsNoTracking().Where(t => t.ConnectionId == id).ToListAsync(); }
        async Task<HttpResponseMessage> Post(Browser current, string action, Dictionary<string, string> fields) {
            var page = Fields(await (await current.Get("/mcp-client/registry")).Content.ReadAsStringAsync()); fields["__RequestVerificationToken"] = page["__RequestVerificationToken"];
            return await current.Post("/mcp-client/registry/" + action, fields);
        }
        Task<T> Agent<T>(Func<ReadAgentService, Task<T>> action) => Scope(sp => action(sp.GetRequiredService<ReadAgentService>()));
        async Task<T> Scope<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = sp, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(TenantPlatformClaimTypes.UserId, admin.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, account.ToString()) }, "trusted-fixture")) };
            try { return await action(sp); } finally { accessor.HttpContext = previous; }
        }
        static async Task Failure(Func<Task> action, string code) { try { await action(); } catch (ReadAgentException e) when (e.Code == code) { return; } throw new Exception("Expected agent " + code); }
    }
}
