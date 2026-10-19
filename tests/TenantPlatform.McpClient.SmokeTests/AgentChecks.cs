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
        using (var fresh = JsonDocument.Parse(protocol.Payloads[start])) Check(fresh.RootElement.GetProperty("input").GetArrayLength() > 1 && fresh.RootElement.GetProperty("input").ToString().Contains("untrustedPreviousAnswer"), "follow-up reuses previous intent and answer only with fresh MCP evidence");
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
        // Approve the account-bound tools through the actual Application administration entrypoints.
        foreach (var tool in (await Registry(s => s.ToolsAsync(own))).Where(t => t.Name != "accounts_list"))
        {
            await Registry(async s => { await s.SetToolPolicyAsync(own, tool.Name, tool.Version, McpToolAudience.Inherit, [], McpToolUse.AutomaticRead); await s.ApproveAsync(own, tool.Name, tool.Version, true); return true; });
        }
        var ownConversation = await Begin(own);
        Check(await Fails(() => Agent(s => s.AskAsync(ownConversation, "override")), "AccountOverride"), "own tool account override rejected before MCP");
        var ownAnswer = await Agent(s => s.AskAsync(ownConversation, "own-list"));
        Check(ownAnswer.Sources.Single().Tool == "agreements_list" && ownAnswer.Notices.Contains("Paging") && ownAnswer.Sources.Single().Page is { PageSize: 25, SnapshotConsistent: false }, "own account data actually comes through MCP with visible paging caveat");
        var attention = await Agent(s => s.AskAsync(ownConversation, "normal"));
        Check(attention.Notices.Contains("Overlapping") && attention.Sources.Last().Tool == "agreements_attention", "attention overlap warning established by backend, not model");
        var reportBuilding = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            protocol.ReportAgreement = (await db.Agreements.SingleAsync(a => a.AccountId == account)).Id;
            db.Buildings.Add(new TenantPlatform.Core.Properties.Building { Id = reportBuilding, AccountId = account, Name = "Report history building" });
            await db.SaveChangesAsync();
        }
        foreach (var (prompt, expected) in new[] { ("own-query", "agreements_query"), ("own-aggregate", "agreements_aggregate"), ("own-details", "agreements_details") })
        {
            var reportConversation = await Begin(own);
            var reportAnswer = await Agent(s => s.AskAsync(reportConversation, prompt));
            Check(reportAnswer.Sources.Single().Tool == expected, "agent invokes approved new tool through actual MCP: " + expected);
            var followup = await Agent(s => s.AskAsync(reportConversation, "history-only"));
            Check(followup.Sources.Single().Revalidated && followup.RetainedTurns == 2, "new source participates in 8B history: " + expected);
            if (expected == "agreements_aggregate") Check(reportAnswer.Sources.Single().Aggregate is { Complete: true, GroupsOverlap: false, SnapshotConsistent: false }, "aggregate provenance preserves completeness and grouping semantics");
            await using (var db = factory.CreateDbContext()) { await db.Agreements.Where(a => a.Id == protocol.ReportAgreement).ExecuteUpdateAsync(s => s.SetProperty(a => a.Title, "Changed reporting source " + expected).SetProperty(a => a.BuildingId, expected == "agreements_aggregate" ? (Guid?)null : reportBuilding)); }
            var firstReportPayload = protocol.Payloads.Count;
            var changedReport = await Agent(s => s.AskAsync(reportConversation, "history-only"));
            Check(changedReport.ContextInvalidated && protocol.Payloads.Skip(firstReportPayload).All(p => !p.Contains("untrustedPreviousAnswer")), "changed new source invalidates derived history: " + expected);
        }
        // The tool remains authorized while an individual agreement's Read grant is revoked.
        var businessGrant = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var agreement = await db.Agreements.SingleAsync(a => a.AccountId == account);
            db.AgreementAccess.Add(new TenantPlatform.Core.Agreements.AgreementAccess { Id = businessGrant, AccountId = account,
                AgreementId = agreement.Id, UserId = member, Level = TenantPlatform.Core.Agreements.AgreementAccessLevel.Read });
            await db.SaveChangesAsync();
        }
        foreach (var prompt in new[] { "own-query", "own-aggregate", "own-details" })
        {
            var scopedReport = await Begin(own, member);
            await Agent(s => s.AskAsync(scopedReport, prompt), user: member);
            await using (var db = factory.CreateDbContext()) { db.Remove(await db.AgreementAccess.SingleAsync(a => a.Id == businessGrant)); await db.SaveChangesAsync(); }
            if (prompt == "own-details") Check(await Fails(() => Agent(s => s.AskAsync(scopedReport, "history-only"), user: member), "HistoryUnavailable"), "revoked detail cannot re-enter conversation history");
            else Check((await Agent(s => s.AskAsync(scopedReport, "history-only"), user: member)).ContextInvalidated, "revoked agreement changes new list/aggregate and invalidates derived history: " + prompt);
            await using (var db = factory.CreateDbContext())
            {
                db.AgreementAccess.Add(new TenantPlatform.Core.Agreements.AgreementAccess { Id = businessGrant, AccountId = account, AgreementId = protocol.ReportAgreement, UserId = member, Level = TenantPlatform.Core.Agreements.AgreementAccessLevel.Read });
                await db.SaveChangesAsync();
            }
        }
        var protectedConversation = await Begin(own, member);
        var protectedAnswer = await Agent(s => s.AskAsync(protectedConversation, "protected"), user: member);
        Check(protectedAnswer.Text.Contains("PROTECTED_DERIVED_MARKER"), "resource-scoped history starts with authorized MCP agreement data");
        await using (var db = factory.CreateDbContext()) { db.Remove(await db.AgreementAccess.SingleAsync(a => a.Id == businessGrant)); await db.SaveChangesAsync(); }
        var firstPayload = protocol.Payloads.Count;
        var afterRevocation = await Agent(s => s.AskAsync(protectedConversation, "followup"), user: member);
        Check(afterRevocation.ContextInvalidated && afterRevocation.Notices.Contains("HistoryInvalidated") &&
            protocol.Payloads.Skip(firstPayload).All(p => !p.Contains("PROTECTED_DERIVED_MARKER") && !p.Contains("Delegated agreement")),
            "fresh resource-scoped MCP re-fetch removes both old agreement data and derived answer before follow-up model call");
        await using (var db = factory.CreateDbContext())
        {
            var agreement = await db.Agreements.SingleAsync(a => a.AccountId == account);
            db.AgreementAccess.Add(new TenantPlatform.Core.Agreements.AgreementAccess { Id = businessGrant, AccountId = account,
                AgreementId = agreement.Id, UserId = member, Level = TenantPlatform.Core.Agreements.AgreementAccessLevel.Read });
            await db.SaveChangesAsync();
        }
        var finalRevocation = await Begin(own, member);
        await Agent(s => s.AskAsync(finalRevocation, "protected"), user: member);
        protocol.BeforeReply = async () => {
            await using var db = factory.CreateDbContext(); db.Remove(await db.AgreementAccess.SingleAsync(a => a.Id == businessGrant)); await db.SaveChangesAsync();
        };
        Check(await Fails(() => Agent(s => s.AskAsync(finalRevocation, "history-only"), user: member), "HistoryUnavailable"),
            "resource grant removed during final model response prevents answer delivery and commit");
        var afterFinalFailure = protocol.Payloads.Count;
        Check((await Agent(s => s.AskAsync(finalRevocation, "followup"), user: member)).ContextInvalidated &&
            protocol.Payloads.Skip(afterFinalFailure).All(p => !p.Contains("PROTECTED_DERIVED_MARKER") && !p.Contains("history-only")),
            "failed final response and invalidated derived content never become later context");
        var continued = await Begin(external);
        await Agent(s => s.AskAsync(continued, "normal"));
        await Agent(async s => { await s.CancelAsync(continued); return true; });
        var afterCancel = await Agent(s => s.AskAsync(continued, "followup"));
        Check(afterCancel.Sources.Any(s => s.Revalidated) && afterCancel.RetainedTurns == 2, "cancel preserves completed turns and follow-up provenance");
        await Agent(async s => { await s.DeleteAsync(continued); return true; });
        Check(await DeniedAgent(() => Agent(s => s.AskAsync(continued, "followup"))), "new conversation deletion removes old context and rejects its ID");
        var changed = await Begin(external);
        await Agent(s => s.AskAsync(changed, "normal"));
        var savedVersion = (await Registry(s => s.ToolsAsync(external))).Single().Version;
        await ToolUpdate(external, t => t.SetProperty(x => x.Version, "changed-historical-contract"));
        var changedAnswer = await Agent(s => s.AskAsync(changed, "followup"));
        Check(changedAnswer.ContextInvalidated && !changedAnswer.Sources.Any(s => s.Revalidated), "changed approved version invalidates prior answers instead of replaying stale calls");
        await ToolUpdate(external, t => t.SetProperty(x => x.Version, savedVersion));
        var failed = await Begin(external);
        Check(await Fails(() => Agent(s => s.AskAsync(failed, "model-error")), "ModelUnavailable"), "failed run cannot become a completed context turn");
        var recovered = await Agent(s => s.AskAsync(failed, "normal"));
        Check(recovered.RetainedTurns == 1, "only successful answer is committed after failure");
        var interruptedSource = await Begin(external);
        await Agent(s => s.AskAsync(interruptedSource, "normal"));
        var payloadCount = protocol.Payloads.Count; protocol.McpUnavailable = true;
        Check(await Fails(() => Agent(s => s.AskAsync(interruptedSource, "followup")), "HistoryUnavailable") && protocol.Payloads.Count == payloadCount,
            "unavailable old source removes derived context without calling the model");
        protocol.McpUnavailable = false;
        Check((await Agent(s => s.AskAsync(interruptedSource, "followup"))).ContextInvalidated, "invalidation survives failure until next successful response informs GUI");
        var approvalHistory = await Begin(external);
        await Agent(s => s.AskAsync(approvalHistory, "normal"));
        await ToolUpdate(external, t => t.SetProperty(x => x.Approved, false));
        payloadCount = protocol.Payloads.Count;
        Check(await Fails(() => Agent(s => s.AskAsync(approvalHistory, "followup")), "ConnectionUnavailable") && protocol.Payloads.Count == payloadCount,
            "revoked historical tool approval prevents all old context use");
        await ToolUpdate(external, t => t.SetProperty(x => x.Approved, true));
        Check((await Agent(s => s.AskAsync(approvalHistory, "followup"))).ContextInvalidated, "restoring tool approval does not resurrect removed answers");
        var boundedHistory = await Begin(external);
        await Agent(s => s.AskAsync(boundedHistory, "first"));
        var bounded = await Agent(s => s.AskAsync(boundedHistory, "followup"), limits: defaults with { HistoryTurns = 1 });
        Check(bounded.RetainedTurns == 1 && bounded.Notices.Contains("HistoryTrimmed"), "Application reports whole-turn history eviction");
        payloadCount = protocol.Payloads.Count;
        Check(await Fails(() => Agent(s => s.AskAsync(boundedHistory, "another"), limits: defaults with { TotalModelBytes = 8192 }), "ContextLimit") && protocol.Payloads.Count == payloadCount,
            "retransmitted historical context counts against model-input budget before network");
        var tiny = await Begin(external);
        var notRetained = await Agent(s => s.AskAsync(tiny, "normal"), limits: defaults with { HistoryBytes = 1024 });
        Check(!notRetained.CurrentTurnRetained && notRetained.RetainedTurns == 0 && notRetained.Notices.Contains("HistoryNotRetained"), "history byte overflow omits the whole oversized turn explicitly");
        var injectedHistory = await Begin(external);
        await Agent(s => s.AskAsync(injectedHistory, "normal"));
        Check(await Fails(() => Agent(s => s.AskAsync(injectedHistory, "cross")), "InvalidTool"), "injection in replayed evidence cannot add a second connection's tool");
        var reasoningHistory = await Begin(external);
        await Agent(s => s.AskAsync(reasoningHistory, "reasoning"));
        using (var lease = sessions.Start(reasoningHistory, new(true, admin, account), defaults, default))
        {
            var saved = JsonSerializer.Serialize(lease.History);
            Check(!saved.Contains("synthetic-encrypted-reasoning") && !saved.Contains("synthetic-agent-key") && !saved.Contains("ProtectedTokens"),
                "completed history retains neither hidden reasoning nor credentials");
        }
        CheckMemory();
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
        var reportHttp = await Http("begin", new { connection = own, account, sharingAccepted = true });
        using var reportBegun = JsonDocument.Parse(await reportHttp.Content.ReadAsStringAsync());
        var reportResponse = await Http("ask", new { conversation = reportBegun.RootElement.GetProperty("conversation").GetGuid(), prompt = "own-aggregate" });
        Check(reportResponse.IsSuccessStatusCode, "aggregate source reaches real cookie-protected agent HTTP endpoint");
        using var reportJson = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        var reportSource = reportJson.RootElement.GetProperty("sources")[0];
        Check(reportSource.GetProperty("aggregate").GetProperty("complete").GetBoolean() && !reportSource.GetProperty("aggregate").GetProperty("groupsOverlap").GetBoolean() &&
            reportSource.GetProperty("summary").GetString() is { Length: > 20 } summary && !summary.Contains("AgentAggregateFacts"), "HTTP aggregate completeness is rendered with localized source summary");
        Check(protocol.Payloads.All(p => !p.Contains("synthetic-agent-key") && !p.Contains("ProtectedTokens") && !p.Contains("private-provider-error")), "all captured model inputs exclude backend credentials and provider errors");
        Console.WriteLine("Agent synthetic-model / real OAuth-MCP-PostgreSQL checks passed.");

        void CheckMemory()
        {
            var memoryClock = new AdjustableClock(); using var memory = new ReadAgentSessions(memoryClock);
            var owner = new ActorSnapshot(true, admin, account); var other = new ActorSnapshot(true, member, account);
            var budget = defaults with { HistoryTurns = 2, HistoryBytes = 16384, ServerHistoryBytes = 16384 };
            var id = memory.Create(owner, external);
            for (var n = 1; n <= 3; n++)
            {
                using var lease = memory.Start(id, owner, budget, default); var turn = new AgentTurn("q" + n, new("a", [], []), []);
                lease.Commit(turn);
                if (n == 1) Check(Throws(() => lease.Commit(turn)), "one lease cannot create duplicate completed turns");
            }
            using (var lease = memory.Start(id, owner, budget, default))
                Check(lease.History.Select(t => t.Question).SequenceEqual(new[] { "q2", "q3" }), "history evicts whole oldest turn pairs deterministically");
            using (var lease = memory.Start(id, owner, budget, default))
            {
                memory.Cancel(id, owner); Check(Throws(() => lease.Commit(new("late-cancelled", new("a", [], []), []))), "cancelled late reply cannot commit");
            }
            using (var lease = memory.Start(id, owner, budget, default))
            {
                memory.Delete(id, owner); Check(Throws(() => lease.Commit(new("late-deleted", new("a", [], []), []))), "deleted conversation cannot be resurrected by late completion");
                var replacement = memory.Create(owner, external);
                Check(Throws(() => memory.Start(replacement, owner, budget, default)), "deletion does not release a still-running lease's concurrency quota early");
                memory.Delete(replacement, owner);
            }
            budget = budget with { HistoryBytes = 8192, ServerHistoryBytes = 8192 };
            id = memory.Create(owner, external);
            using (var lease = memory.Start(id, owner, budget, default)) lease.Commit(new("large", new(new string('x', 900), [], []), []));
            var secondId = memory.Create(other, second);
            using (var lease = memory.Start(secondId, other, budget, default)) lease.Commit(new("large2", new(new string('y', 900), [], []), []));
            using (var lease = memory.Start(id, owner, budget, default)) Check(lease.History.Count == 0 && lease.Trimmed, "global charged-memory bound evicts whole idle history with notice");
            using (var lease = memory.Start(secondId, other, budget, default))
            {
                memoryClock.Advance(TimeSpan.FromMinutes(31));
                Check(Throws(() => memory.Verify(secondId, other)) && lease.Token.IsCancellationRequested && Throws(() => lease.Commit(new("expired", new("a", [], []), []))), "expiry clears context, cancels work and rejects late commit");
            }
        }
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
