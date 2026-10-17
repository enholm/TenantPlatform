using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunAgent(WebApplication web, Factory factory, Guid admin, Guid member, Guid account, Guid otherAccount,
        Guid own, Guid external, Guid second, AdjustableClock clock, AgentProtocolFixture protocol, Func<int> remoteCalls, Browser browser)
    {
        var defaults = new ReadAgentLimits();
        var options = web.Services.GetRequiredService<Microsoft.EntityFrameworkCore.DbContextOptions<OAuthDbContext>>();
        var schema = new AgentSchemaValidator();
        Check(schema.Supported("{\"type\":\"object\",\"additionalProperties\":false}") && !schema.Valid("{\"type\":\"object\",\"additionalProperties\":false}", "{\"extra\":1}"), "agent validates approved input schema using established validator");
        foreach (var forbidden in new[] { "{\"type\":\"object\",\"$ref\":\"https://evil.test/schema\"}", "{\"type\":\"object\",\"patternProperties\":{}}", "{\"type\":\"object\",\"unknownAssertion\":true}" })
            Check(!schema.Supported(forbidden), "agent rejects unsupported/ref schemas without network or rewriting");
        Check(!schema.Valid("{\"type\":\"object\"}", "{\"a\":1,\"a\":2}"), "duplicate argument keys rejected");
        var available = await Registry(s => s.AvailableToolsAsync());
        var otherTool = available.Single(t => t.ConnectionId == second);
        protocol.OtherTool = ReadAgentService.ModelName(otherTool);
        Check(protocol.OtherTool != ReadAgentService.ModelName(available.Single(t => t.ConnectionId == external)), "same original tool name gets stable distinct model aliases per connection/version");
        Check((await Agent(s => s.ConnectionsAsync())).Select(c => c.Id).Contains(external), "agent catalog uses registered personally authorized automatic tools");
        var before = remoteCalls();
        var conversation = await Begin(external);
        var answer = await Agent(s => s.AskAsync(conversation, "multi"));
        Check(answer.Sources.Count == 2 && answer.Sources.All(s => s.ConnectionId == external && s.Tool == "accounts_list") && remoteCalls() == before + 2 && answer.Notices.Contains("ExternalCompleteness"), "real OAuth/MCP/PostgreSQL agent loop executes two model/tool rounds with backend provenance");
        Check(protocol.ExternalAccountSeen == otherAccount, "external accountId follows its own approved contract and is not clamped to the local selected account");
        Check(answer.Text.Contains("<script>"), "model output remains plain text data for safe rendering");
        var last = protocol.Payloads.Last();
        Check(last.Contains("UNTRUSTED INJECTION") && !last.Contains("tenantplatform.agreements.read") && !last.Contains("synthetic-agent-key") && !last.Contains("ProtectedTokens") && !last.Contains(protocol.OtherTool), "untrusted result cannot add tools or expose credentials/model key to payload");
        var reasoning = await Agent(s => s.AskAsync(conversation, "reasoning"));
        Check(protocol.Payloads.Last().Contains("synthetic-encrypted-reasoning") && !reasoning.Text.Contains("synthetic-encrypted-reasoning"), "stateless reasoning continuity stays backend-only and bounded");
        var start = protocol.Payloads.Count;
        await Agent(s => s.AskAsync(conversation, "normal"));
        using (var fresh = JsonDocument.Parse(protocol.Payloads[start])) Check(fresh.RootElement.GetProperty("input").GetArrayLength() == 1, "next message sends neither old tool data nor previous answer/prompt");
        foreach (var (prompt, code) in new[] { ("builtin", "InvalidTool"), ("unknown", "InvalidTool"), ("cross", "InvalidTool"), ("invalid", "InvalidArguments"), ("model-error", "ModelUnavailable"), ("incomplete", "ModelIncomplete"), ("oversize-response", "ContextLimit"), ("long-answer", "AnswerLimit") })
        {
            var id = await Begin(external); var prior = remoteCalls();
            Check(await Fails(() => Agent(s => s.AskAsync(id, prompt)), code), "agent rejects " + prompt + " without fallback/partial answer");
            if (prompt is "unknown" or "cross" or "invalid" or "model-error") Check(remoteCalls() == prior, "rejected run never calls MCP: " + prompt);
        }
        var roundConversation = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(roundConversation, "round-limit", default), limits: defaults with { Rounds = 2 }), "RoundLimit"), "model round budget enforced");
        var resultConversation = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(resultConversation, "normal"), limits: defaults with { ResultBytes = 1 }), "ResultLimit"), "oversize result rejected whole before model reuse");
        var contextConversation = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(contextConversation, "normal"), limits: defaults with { ContextBytes = 1024 }), "ContextLimit"), "context budget includes metadata and results");
        var callConversation = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(callConversation, "multi"), limits: defaults with { Calls = 1 }), "CallLimit"), "tool call budget enforced across rounds");
        var promptConversation = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(promptConversation, new string('p', 4001))), "PromptLimit"), "prompt limit enforced server-side");
        var dataBudget = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(dataBudget, "normal"), limits: defaults with { TotalModelBytes = 1024 }), "ContextLimit"), "cumulative model data budget enforced including retransmitted context");
        var deactivated = await Begin(external);
        protocol.BeforeReply = async () => { await using var db = factory.CreateDbContext(); await db.Users.Where(u => u.Id == admin).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)); };
        Check(await DeniedAgent(() => Agent(s => s.AskAsync(deactivated, "normal"))), "local deactivation after catalog blocks agent before tool execution");
        await using (var db = factory.CreateDbContext()) { await db.Users.Where(u => u.Id == admin).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true)); }
        var revoked = await Begin(external);
        protocol.BeforeReply = () => ToolUpdate(external, t => t.SetProperty(x => x.Approved, false));
        before = remoteCalls();
        Check(await Fails(() => Agent(s => s.AskAsync(revoked, "normal")), "SourceUnavailable") && remoteCalls() == before, "approval removal between catalog/model response and call stops actual MCP transport");
        await ToolUpdate(external, t => t.SetProperty(x => x.Approved, true));
        var revokedResult = await Begin(external); var priorPayloadCount = protocol.Payloads.Count;
        protocol.AfterMcp = () => ToolUpdate(external, t => t.SetProperty(x => x.Approved, false));
        Check(await DeniedAgent(() => Agent(s => s.AskAsync(revokedResult, "normal"))) && protocol.Payloads.Count == priorPayloadCount + 1,
            "result is not forwarded to another model round when source access is revoked during MCP retrieval");
        await ToolUpdate(external, t => t.SetProperty(x => x.Approved, true));
        var versioned = await Begin(external); var version = available.Single(t => t.ConnectionId == external).Version;
        protocol.BeforeReply = () => ToolUpdate(external, t => t.SetProperty(x => x.Version, "new-version"));
        Check(await Fails(() => Agent(s => s.AskAsync(versioned, "normal")), "SourceUnavailable"), "metadata version change cannot run cached model binding");
        await ToolUpdate(external, t => t.SetProperty(x => x.Version, version));
        await ToolUpdate(external, t => t.SetProperty(x => x.UseClass, McpToolUse.ConfirmationRequired));
        Check(!(await Agent(s => s.ConnectionsAsync())).Any(c => c.Id == external) && await Fails(() => Begin(external), "ConnectionUnavailable"), "confirmation-required tools cannot enter an agent run");
        await ToolUpdate(external, t => t.SetProperty(x => x.UseClass, McpToolUse.AutomaticRead));
        var outage = await Begin(external); protocol.McpUnavailable = true;
        Check(await Fails(() => Agent(s => s.AskAsync(outage, "normal")), "SourceUnavailable"), "MCP errors produce no guessed zero-results answer or direct-data fallback");
        protocol.McpUnavailable = false;
        var credentialEnd = default(DateTimeOffset?);
        await using (var db = new OAuthDbContext(options)) {
            var credential = await db.Set<McpClientCredential>().SingleAsync(x => x.UserId == admin && x.ConnectionId == external);
            credentialEnd = credential.GrantExpiresUtc; credential.GrantExpiresUtc = clock.GetUtcNow().AddMinutes(-1); await db.SaveChangesAsync();
        }
        Check(await Fails(() => Begin(external), "ConnectionUnavailable"), "expired personal grant cannot start an agent conversation");
        await using (var db = new OAuthDbContext(options)) { await db.Set<McpClientCredential>().Where(x => x.UserId == admin && x.ConnectionId == external).ExecuteUpdateAsync(s => s.SetProperty(x => x.GrantExpiresUtc, credentialEnd)); }
        var foreignConversation = await Begin(external);
        Check(await DeniedAgent(() => Agent(s => s.AskAsync(foreignConversation, "normal"), user: member)) && await DeniedAgent(() => Agent(s => s.AskAsync(foreignConversation, "normal"), selected: otherAccount)), "conversation cannot move to another user or selected account");
        var sessions = web.Services.GetRequiredService<ReadAgentSessions>();
        var concurrent = await Begin(external);
        var pending = Agent(s => s.AskAsync(concurrent, "cancel"));
        await protocol.Blocking.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(await Fails(() => Agent(s => s.AskAsync(concurrent, "normal")), "Busy"), "same user cannot run overlapping messages");
        var concurrentOther = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(concurrentOther, "normal")), "Busy"), "per-user limit spans separate conversations and connections");
        var memberConversation = await Begin(external, member);
        Check((await Agent(s => s.AskAsync(memberConversation, "normal"), user: member)).Sources.Single().ConnectionId == external, "another user runs independently with own credentials");
        await Agent(async s => { await s.CancelAsync(concurrent); return true; });
        Check(await Fails(() => pending, "CancelledOrTimedOut"), "cancel reaches an in-flight production model HTTP request");
        var switchedConversation = await Begin(external);
        protocol.WhenBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var switching = Agent(s => s.AskAsync(switchedConversation, "cancel"));
        await protocol.WhenBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sessions.ClearUser(admin); // Same hook used by production successful login/account switch/logout.
        Check(await Fails(() => switching, "CancelledOrTimedOut") && await DeniedAgent(() => Agent(s => s.AskAsync(switchedConversation, "normal"))), "identity switch clears session metadata and cancels active run");
        foreach (var changeUser in new[] { true, false })
        {
            var cookieConversation = await Begin(external);
            protocol.WhenBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var cookiePending = Agent(s => s.AskAsync(cookieConversation, "cancel"));
            await protocol.WhenBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Scope(admin, account, async sp => {
                var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                http.Request.Scheme = "https"; http.Request.Host = new("tenant.test");
                await using var db = factory.CreateDbContext();
                var verifiedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == (changeUser ? member : admin));
                await sp.GetRequiredService<AuthenticationCookieService>().SignInAsync(http, verifiedUser, changeUser ? account : null, false);
                return true;
            });
            Check(await Fails(() => cookiePending, "CancelledOrTimedOut"), changeUser ? "production cookie user-change hook cancels active conversation" : "production cookie account-change hook cancels active conversation");
        }
        var timeout = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(timeout, "timeout"), limits: defaults with { Seconds = 1 }), "CancelledOrTimedOut"), "total agent time budget interrupts model work");
        var mcpConversation = await Begin(external); protocol.BlockMcp = true;
        var mcpPending = Agent(s => s.AskAsync(mcpConversation, "normal"));
        await protocol.McpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Agent(async s => { await s.CancelAsync(mcpConversation); return true; });
        Check(await Fails(() => mcpPending, "CancelledOrTimedOut"), "agent cancellation reaches actual MCP HTTP transport");
        await protocol.McpCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); protocol.BlockMcp = false;
        // Approve our two account-bound tools through the actual Application administration entrypoints.
        foreach (var tool in (await Registry(s => s.ToolsAsync(own))).Where(t => t.Name != "accounts_list"))
        {
            await Registry(async s => { await s.SetToolPolicyAsync(own, tool.Name, tool.Version, McpToolAudience.Inherit, [], McpToolUse.AutomaticRead); await s.ApproveAsync(own, tool.Name, tool.Version, true); return true; });
        }
        var ownConversation = await Begin(own);
        Check(await Fails(() => Agent(s => s.AskAsync(ownConversation, "override")), "AccountOverride"), "own tool account override rejected before MCP");
        var ownAnswer = await Agent(s => s.AskAsync(ownConversation, "own-list"));
        Check(ownAnswer.Sources.Single().Tool == "agreements_list" && ownAnswer.Notices.Contains("Paging") && ownAnswer.Sources.Single().Page is { PageSize: 25, SnapshotConsistent: false }, "own account data actually comes through MCP with visible paging caveat");
        var attention = await Agent(s => s.AskAsync(ownConversation, "normal"));
        Check(attention.Notices.Contains("Overlapping") && attention.Sources.Single().Tool == "agreements_attention", "attention overlap warning established by backend, not model");
        var page = await (await browser.Get("/agent")).Content.ReadAsStringAsync();
        Check(page.Contains("data-read-agent") && page.Contains("OpenAI") && page.Contains("data-consent") && !page.Contains("synthetic-agent-key") && !page.Contains("<script>alert"), "SSR chat panel discloses provider, requires selection/acceptance, contains no key or model HTML");
        var csrf = WebUtility.HtmlDecode(Regex.Match(page, "data-csrf=\"([^\"]+)\"").Groups[1].Value);
        var response = await Http("begin", new { connection = external, account, sharingAccepted = true });
        Check(response.IsSuccessStatusCode, "real cookie/antiforgery HTTP begins conversation");
        using var begun = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var conversationHttp = begun.RootElement.GetProperty("conversation").GetGuid();
        response = await Http("ask", new { conversation = conversationHttp, prompt = "normal" });
        var body = await response.Content.ReadAsStringAsync();
        Check(response.IsSuccessStatusCode && !body.Contains("<script>") && body.Contains("sources") && !body.Contains("private-provider-error"), "HTTP answer safely JSON-encodes model HTML and sends actual sources");
        Check((await Http("ask", new { conversation = conversationHttp, prompt = "normal", userId = member })).StatusCode == HttpStatusCode.BadRequest, "HTTP rejects client identity injection");
        Check((await Http("ask", new { conversation = conversationHttp, prompt = "normal" }, token: false)).StatusCode == HttpStatusCode.BadRequest, "chat POST requires antiforgery");
        Check(protocol.Payloads.All(p => !p.Contains("synthetic-agent-key") && !p.Contains("ProtectedTokens") && !p.Contains("private-provider-error")), "all captured model inputs exclude backend credentials and provider errors");
        Console.WriteLine("Agent synthetic-model / real OAuth-MCP-PostgreSQL checks passed.");

        async Task<HttpResponseMessage> Http(string action, object body, bool token = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://tenant.test/agent/" + action) { Content = JsonContent.Create(body) };
            request.Headers.Add("Cookie", string.Join("; ", browser.Cookies.Select(p => p.Key + "=" + p.Value)));
            if (token) request.Headers.Add("RequestVerificationToken", csrf);
            return await web.GetTestClient().SendAsync(request);
        }
        async Task ToolUpdate(Guid id, Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<McpDiscoveredTool>> update)
        { await using var db = new OAuthDbContext(options); await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == id).ExecuteUpdateAsync(update); }
        Task<Guid> Begin(Guid id, Guid? user = null) => Agent(s => s.BeginAsync(id, account, true), user: user);
        async Task<T> Registry<T>(Func<McpRegistryService, Task<T>> action) => await Scope(admin, account, sp => action(sp.GetRequiredService<McpRegistryService>()));
        async Task<T> Agent<T>(Func<ReadAgentService, Task<T>> action, Guid? user = null, Guid? selected = null, ReadAgentLimits? limits = null) =>
            await Scope(user ?? admin, selected ?? account, sp => {
                var actor = new FixedActor(new(true, user ?? admin, selected ?? account));
                var auth = new TenantPlatform.Application.Security.AuthorizationService(actor, new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), clock);
                return action(new ReadAgentService(actor, auth, sp.GetRequiredService<McpRegistryService>(), sp.GetRequiredService<IReadAgentModel>(), schema, sessions: web.Services.GetRequiredService<ReadAgentSessions>(), limits ?? defaults));
            });
        async Task<T> Scope<T>(Guid user, Guid selected, Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = web.Services.CreateAsyncScope(); var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(TenantPlatformClaimTypes.UserId, user.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, selected.ToString()) }, "trusted-agent-fixture")) };
            try { return await action(scope.ServiceProvider); } finally { accessor.HttpContext = previous; }
        }
        static async Task<bool> Fails<T>(Func<Task<T>> action, string code) { try { await action(); return false; } catch (ReadAgentException ex) { return ex.Code == code; } }
        static async Task<bool> DeniedAgent<T>(Func<Task<T>> action) { try { await action(); return false; } catch (UnauthorizedAccessException) { return true; } }
    }
}
