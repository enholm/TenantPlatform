using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Agent;

public sealed class ReadAgentService(ICurrentActor actor, IAuthorizationService authorization, McpRegistryService registry,
    IReadAgentModel model, IAgentSchemaValidator schemas, ReadAgentSessions sessions, ReadAgentLimits limits, AgentReportDrafts? drafts = null)
{
    public bool Ready => model.Ready && limits.Valid;
    private async Task<ActorSnapshot> Require(CancellationToken ct)
    {
        var who = actor.GetSnapshot();
        if (!who.IsAuthenticated || who.AccountId is not Guid account ||
            !await authorization.CanAsync(Permissions.McpConnections.Use, new(ResourceKind.Account, AccountId: account), ct))
        {
            if (who.IsAuthenticated) sessions.ClearUser(who.UserId);
            throw new UnauthorizedAccessException();
        }
        var scope = await authorization.GetAccountActorScopeAsync(account, ct);
        if (scope.UserId != who.UserId || actor.GetSnapshot() != who) throw new UnauthorizedAccessException();
        return who;
    }
    private async Task<IReadOnlyList<McpAvailableTool>> Catalog(CancellationToken ct)
    {
        var page = await registry.ListAsync(ct);
        var tools = await registry.AvailableToolsAsync(ct);
        return tools.Where(t => !t.Name.StartsWith("personal_pages_", StringComparison.Ordinal) && t.UseClass == McpToolUse.AutomaticRead &&
            (External(t.ConnectionId, page) || ReadAgentAreas.Known(t.Name))).ToArray();
    }
    private static bool External(Guid connection, McpRegistryPage page)
    {
        var server = page.Servers.SingleOrDefault(s => s.Id == connection) ?? throw new UnauthorizedAccessException();
        return page.Profiles.Single(p => p.Id == server.ProfileId && p.Version == server.ProfileVersion).Binding.External;
    }
    public async Task<IReadOnlyList<AgentConnection>> ConnectionsAsync(CancellationToken ct = default)
    {
        await Require(ct);
        if (!Ready) return [];
        var registryPage = await registry.ListAsync(ct);
        return (await Catalog(ct)).GroupBy(t => t.ConnectionId).Select(g => new AgentConnection(g.Key, g.First().ConnectionName, g.Count(), Enum.GetValues<ReadAgentArea>().Select(a => new AgentAreaOption(a, g.Count(t => ReadAgentAreas.Includes(a, External(t.ConnectionId, registryPage), t.Name))))
                .Where(a => a.ToolCount > 0).ToArray())).ToArray();
    }
    public async Task<Guid> BeginAsync(Guid selectedConnection, Guid expectedAccount, bool sharingAccepted, ReadAgentArea area, CancellationToken ct = default)
        => await BeginCoreAsync(selectedConnection, expectedAccount, sharingAccepted, area, ct, null);
    private async Task<Guid> BeginCoreAsync(Guid selectedConnection, Guid expectedAccount, bool sharingAccepted, ReadAgentArea area, CancellationToken ct, Guid? preserve)
    {
        var who = await Require(ct);
        if (!Ready) throw new ReadAgentException("Disabled");
        if (who.AccountId != expectedAccount || !sharingAccepted) throw new ReadAgentException("ConsentRequired");
        if (!Enum.IsDefined(area)) throw new ReadAgentException("AreaInvalid");
        var page = await registry.ListAsync(ct);
        var available = (await Catalog(ct)).Where(t => t.ConnectionId == selectedConnection).ToArray();
        if (available.Length == 0) throw new ReadAgentException("ConnectionUnavailable");
        if (!available.Any(t => ReadAgentAreas.Includes(area, External(selectedConnection, page), t.Name))) throw new ReadAgentException("AreaInvalid");
        ct.ThrowIfCancellationRequested();
        if (await Require(ct) != who) throw new UnauthorizedAccessException();
        return sessions.Create(who, selectedConnection, preserve, area);
    }
    public async Task DeleteAsync(Guid conversation, CancellationToken ct = default) => sessions.Delete(conversation, await Require(ct));
    public async Task<Guid> SwitchAreaAsync(Guid conversation, ReadAgentArea area, bool resetAccepted, CancellationToken ct = default)
    {
        var who = await Require(ct);
        if (!resetAccepted || !Enum.IsDefined(area)) throw new ReadAgentException("AreaInvalid");
        var connection = sessions.Connection(conversation, who);
        var page = await registry.ListAsync(ct);
        if (!(await Catalog(ct)).Any(t => t.ConnectionId == connection && ReadAgentAreas.Includes(area, External(connection, page), t.Name))) throw new ReadAgentException("AreaInvalid");
        if (await Require(ct) != who) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        return sessions.Replace(conversation, who, area);
    }
    public async Task<ReadAgentArea> AreaAsync(Guid conversation, CancellationToken ct = default) => sessions.Area(conversation, await Require(ct));
    // Safe read for SSR; never creates a conversation or fetches model/business data.
    public async Task<AgentReportDraft> EditPageAsync(Guid pageId, CancellationToken ct = default)
    {
        await Require(ct);
        if (drafts is null || !Ready) throw new ReadAgentException("Disabled");
        return await drafts.OpenAsync(pageId, ct);
    }
    public async Task<AgentReportEditing> BeginEditAsync(Guid pageId, Guid expectedAccount, bool sharingAccepted, CancellationToken ct = default)
        => await BeginEditCoreAsync(pageId, expectedAccount, sharingAccepted, ct, null);
    private async Task<AgentReportEditing> BeginEditCoreAsync(Guid pageId, Guid expectedAccount, bool sharingAccepted, CancellationToken ct, Guid? preserve)
    {
        var who = await Require(ct);
        if (who.AccountId != expectedAccount || !sharingAccepted) throw new ReadAgentException("ConsentRequired");
        var draft = await EditPageAsync(pageId, ct);
        if (preserve is Guid old && draft.Definition.ConnectionId != sessions.Connection(old, who)) throw new ReadAgentException("DraftSourceChanged");
        var id = await BeginCoreAsync(draft.Definition.ConnectionId, expectedAccount, sharingAccepted, ReadAgentAreas.FromReport(draft.Definition.Area), ct, preserve);
        try
        {
            using var run = sessions.Start(id, who, limits, ct);
            var report = await drafts!.PreviewAsync(draft, 1, run.Token);
            if (await Require(run.Token) != who) throw new UnauthorizedAccessException();
            run.Token.ThrowIfCancellationRequested(); run.CompleteDraft(draft);
            return new(id, report);
        }
        catch { sessions.Delete(id, who); throw; }
    }
    public async Task DiscardReportAsync(Guid conversation, Guid draftId, Guid revision, CancellationToken ct = default)
    {
        var who = await Require(ct);
        using var run = sessions.Start(conversation, who, limits, ct);
        if (run.Draft is not { } d || d.Id != draftId || d.Revision != revision) throw new ReadAgentException("DraftConflict");
        sessions.Delete(conversation, who);
    }
    public async Task<AgentReportEditing> ReloadReportAsync(Guid conversation, Guid draftId, Guid revision, CancellationToken ct = default)
    {
        var who = await Require(ct); Guid pageId;
        using (var run = sessions.Start(conversation, who, limits, ct)) {
            if (run.Draft is not { Edit: not null } d || d.Id != draftId || d.Revision != revision) throw new ReadAgentException("DraftConflict");
            pageId = d.Edit.PageId;
        }
        // Keep the old draft until a fresh, isolated conversation/preview succeeds. Never rebase an old proposal.
        var opened = await BeginEditCoreAsync(pageId, who.AccountId!.Value, true, ct, conversation);
        try { await DiscardReportAsync(conversation, draftId, revision, ct); return opened; }
        catch { sessions.Delete(opened.Conversation, who); throw; }
    }
    public async Task CancelAsync(Guid conversation, CancellationToken ct = default) => sessions.Cancel(conversation, await Require(ct));
    public async Task<AgentAnswer> AskAsync(Guid conversation, string prompt, CancellationToken ct = default)
    {
        if (!limits.Valid) throw new ReadAgentException("Disabled");
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        total.CancelAfter(TimeSpan.FromSeconds(limits.Seconds));
        try { return await ExecuteAsync(conversation, prompt, total.Token); }
        catch (OperationCanceledException) { throw new ReadAgentException("CancelledOrTimedOut"); }
    }
    private async Task<AgentAnswer> ExecuteAsync(Guid conversation, string prompt, CancellationToken ct)
    {
        var who = await Require(ct);
        if (!Ready) throw new ReadAgentException("Disabled");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > limits.PromptCharacters) throw new ReadAgentException("PromptLimit");
        var connection = sessions.Connection(conversation, who);
        var area = sessions.Area(conversation, who);
        using var run = sessions.Start(conversation, who, limits, ct);
        var token = run.Token;
        try
        {
            var registryPage = await registry.ListAsync(token);
            var selected = (await Catalog(token)).Where(t => t.ConnectionId == connection && ReadAgentAreas.Includes(area, External(connection, registryPage), t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
            if (selected.Length == 0) { run.Invalidate(); throw new ReadAgentException("ConnectionUnavailable"); }
            if (selected.Length > limits.Tools) throw new ReadAgentException("ToolLimit"); // Never silently truncate a catalogue.
            var page = await registry.ListAsync(token);
            var external = External(connection, page);
            var bindings = selected.ToDictionary(t => ModelName(t), t => t, StringComparer.Ordinal);
            var tools = bindings.Select(p =>
            {
                if (!schemas.Supported(p.Value.InputSchema)) throw new ReadAgentException("SchemaUnsupported");
                var parameters = JsonNode.Parse(p.Value.InputSchema)!.AsObject();
                // Only our documented account-bound tools receive this explicit constraint. External schemas stay byte-semantically unchanged.
                if (!external)
                {
                    if (parameters["properties"] is not JsonObject properties || properties["accountId"] is not JsonObject accountProperty ||
                        accountProperty["type"]?.ToString() != "string") throw new ReadAgentException("SchemaUnsupported");
                    accountProperty["const"] = who.AccountId!.Value.ToString("D");
                }
                return new AgentTool(p.Key, p.Value.Description, JsonSerializer.SerializeToElement(parameters));
            }).ToArray();
            var input = new List<JsonElement>();
            var sources = new List<AgentSource>(); var used = new List<McpAvailableTool>(); var notices = new HashSet<string>();
            var calls = 0; var resultBytes = 0; var modelBytes = 0; var callIds = new HashSet<string>(StringComparer.Ordinal);
            var retainedCalls = new List<AgentPastCall>();
            if (run.Trimmed) notices.Add("HistoryTrimmed");
            var history = run.History;
            // Re-fetch the complete transitive source closure before replaying ANY previous derived text.
            var previousCalls = history.SelectMany(t => t.Calls).Concat(run.Draft?.Dependencies ?? []).DistinctBy(c => c.Id).ToArray();
            var freshHistory = new Dictionary<Guid, (AgentPastCall Call, string Result, int Number)>();
            var invalidated = run.Invalidated;
            foreach (var past in previousCalls)
            {
                var tool = selected.SingleOrDefault(t => t.Name == past.Tool && t.Version == past.Version);
                if (tool is null) { invalidated = true; break; }
                try
                {
                    var result = await Invoke(tool, past.Arguments, past.Id, true);
                    if (Fingerprint(result) != past.Fingerprint) { invalidated = true; break; }
                    freshHistory.Add(past.Id, (past, result, sources.Count));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or McpClientException)
                { run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
            }
            if (invalidated)
            {
                if (run.Draft?.Dependencies?.Count > 0) { run.ClearDraft(); run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
                run.Invalidate(); notices.Add("HistoryInvalidated"); freshHistory.Clear(); sources.Clear(); used.Clear(); retainedCalls.Clear();
                foreach (var turn in history) input.Add(JsonSerializer.SerializeToElement(new { role = "user", content = turn.Question }));
            }
            else
            {
                var replayed = new HashSet<Guid>();
                foreach (var turn in history)
                {
                    input.Add(JsonSerializer.SerializeToElement(new { role = "user", content = turn.Question }));
                    foreach (var past in turn.Calls.Where(c => replayed.Add(c.Id)))
                    {
                        var fresh = freshHistory[past.Id]; var name = ModelName(selected.Single(t => t.Name == past.Tool && t.Version == past.Version));
                        var id = "history_" + past.Id.ToString("N"); callIds.Add(id);
                        input.Add(JsonSerializer.SerializeToElement(new { type = "function_call", call_id = id, name, arguments = past.Arguments }));
                        input.Add(JsonSerializer.SerializeToElement(new { type = "function_call_output", call_id = id,
                            output = JsonSerializer.Serialize(new { source = fresh.Number, evidenceId = past.Id, revalidated = true, untrustedMcpResult = JsonSerializer.Deserialize<JsonElement>(fresh.Result) }) }));
                    }
                    if (turn.Answer is { } old) input.Add(JsonSerializer.SerializeToElement(new { role = "assistant",
                        content = JsonSerializer.Serialize(new { untrustedPreviousAnswer = old.Text, historicalSources = old.Sources }) }));
                }
            }
            var proposalAllowed = drafts is not null && !external && ReadAgentAreas.Reports(area) && AgentReportDrafts.Requested(prompt, run.Draft is not null) &&
                (run.Draft?.SavedPageId is null || AgentReportDrafts.Requested(prompt, false)) && selected.Any(t => TenantPlatform.Application.PersonalPages.ReportAreas.Sources.Any(s => s.QueryTool == t.Name || s.AggregateTool == t.Name));
            if (run.Draft is { } currentDraft)
            {
                if (drafts is null) throw new ReadAgentException("DraftInvalid");
                try { await drafts.ValidateAsync(currentDraft, token, async (name, version, args, ct) => {
                    var binding = selected.SingleOrDefault(t => t.Name == name && t.Version == version) ?? throw new ReadAgentException("HistoryUnavailable");
                    return await Invoke(binding, args, Guid.NewGuid(), true, true, ct);
                }); }
                catch (Exception e) when (e is UnauthorizedAccessException or McpClientException || e is ReadAgentException { Code: "HistoryUnavailable" }) { run.ClearDraft(); run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
                input.Add(JsonSerializer.SerializeToElement(new { role = "assistant", content = JsonSerializer.Serialize(new {
                    untrustedCurrentReportIntent = currentDraft.Intent, saved = currentDraft.SavedPageId.HasValue,
                    editingExistingPage = currentDraft.Edit is not null,
                    storedReferenceFilters = currentDraft.Edit is not null && (currentDraft.Definition.RequestFilter?.BuildingId.HasValue == true || currentDraft.Definition.RequestFilter?.MissingBuilding == true || currentDraft.Definition.Filter.BuildingId.HasValue || currentDraft.Definition.Filter.UnitId.HasValue || currentDraft.Definition.Filter.OrganizationElementId.HasValue || currentDraft.Definition.Filter.GeographicAreaId.HasValue),
                    instruction = "Intent only, no retained report data. Revise this whole unsaved declaration only on explicit user request. Backend preserves stored reference filters; new identifiers cannot be chosen. Only an explicitly opened edit session can update an existing page through a separate user button." }, AgentReportDrafts.Json) }));
            }
            if (proposalAllowed) { if (tools.Length + 1 > limits.Tools) throw new ReadAgentException("ToolLimit"); tools = [..tools, AgentReportDrafts.ForArea(ReadAgentAreas.ToReport(area))]; }
            if (area == ReadAgentArea.Structure && !external && AgentReportDrafts.Requested(prompt, false)) notices.Add("StructureReportsUnavailable");
            if (area == ReadAgentArea.Structure && !external)
                input.Add(JsonSerializer.SerializeToElement(new { role = "developer", content = "Selected area is Structure: building list and counts only. No building report adapter or proposal tool exists. Explain that building report creation/editing is not yet supported when requested; do not claim to create a page or switch to an agreement report. City is an exact stored string, not a normalized municipality; None means no grouping." }));
            input.Add(JsonSerializer.SerializeToElement(new { role = "user", content = prompt }));
            for (var round = 0; round < limits.Rounds; round++)
            {
                token.ThrowIfCancellationRequested();
                await ValidateContext();
                if (round > 0) await ValidateHistoricalResources();
                var contextBytes = Bytes(JsonSerializer.Serialize(input)) + Bytes(JsonSerializer.Serialize(tools));
                // Reserve framing/instructions too, conservatively, on EVERY resend.
                modelBytes += contextBytes + 8192;
                if (contextBytes > limits.ContextBytes || modelBytes > limits.TotalModelBytes) throw new ReadAgentException("ContextLimit");
                var reply = await model.RespondAsync(tools, input, limits, token);
                token.ThrowIfCancellationRequested();
                if (reply.Calls.Count == 0)
                {
                    await ValidateContext();
                    await ValidateHistoricalResources();
                    if (string.IsNullOrWhiteSpace(reply.Text)) throw new ReadAgentException("ModelUnavailable");
                    if (reply.Text.Length > limits.AnswerCharacters) throw new ReadAgentException("AnswerLimit");
                    if (sources.Count == 0) notices.Add("NoSources");
                    var answer = new AgentAnswer(reply.Text, sources.ToArray(), notices.Order().ToArray());
                    var saved = run.Commit(new(prompt, answer, retainedCalls.ToArray()));
                    if (saved.Trimmed) notices.Add("HistoryTrimmed");
                    if (!saved.Retained) notices.Add("HistoryNotRetained");
                    return answer with { Notices = notices.Order().ToArray(), RetainedTurns = saved.Count, CurrentTurnRetained = saved.Retained, ContextInvalidated = invalidated };
                }
                if (reply.Calls.Any(c => c.Name == AgentReportDrafts.ToolName))
                {
                    if (!proposalAllowed || drafts is null || reply.Calls.Count != 1 || !callIds.Add(reply.Calls[0].Id)) throw new ReadAgentException("InvalidTool");
                    if (++calls > limits.Calls) throw new ReadAgentException("CallLimit");
                    var report = await drafts.PrepareAsync(reply.Calls[0].Arguments, connection, run.Draft, ReadAgentAreas.ToReport(area), async (name, version, args, readToken) => {
                        var binding = selected.SingleOrDefault(t => t.Name == name && t.Version == version) ?? throw new ReadAgentException("ConnectionUnavailable");
                        return await Invoke(binding, args, Guid.NewGuid(), false, false, readToken);
                    }, token);
                    await ValidateContext(); await ValidateHistoricalResources(); token.ThrowIfCancellationRequested();
                    // Preview data never enter model input or retained turn text. Intent lives separately in the session.
                    report = report with { Draft = report.Draft with { Dependencies = retainedCalls.ToArray() } };
                    // Current-turn reads may have changed while the model was proposing. Revalidate intent evidence before publication too.
                    try { await drafts.ValidateAsync(report.Draft, token, async (name, version, args, ct) => {
                        var binding = selected.SingleOrDefault(t => t.Name == name && t.Version == version) ?? throw new ReadAgentException("HistoryUnavailable");
                        return await Invoke(binding, args, Guid.NewGuid(), true, false, ct);
                    }); }
                    catch (Exception e) when (e is UnauthorizedAccessException or McpClientException || e is ReadAgentException { Code: "HistoryUnavailable" }) { run.ClearDraft(); run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
                    var saved = run.Commit(new(prompt, null, retainedCalls.ToArray()), report.Draft);
                    if (saved.Trimmed) notices.Add("HistoryTrimmed");
                    if (!saved.Retained) notices.Add("HistoryNotRetained");
                    return new("", sources.ToArray(), notices.Order().ToArray(), saved.Count, saved.Retained, invalidated, report);
                }
                if (calls + reply.Calls.Count > limits.Calls) throw new ReadAgentException("CallLimit");
                if (reply.Calls.Any(c => !callIds.Add(c.Id))) throw new ReadAgentException("InvalidTool");
                input.AddRange(reply.Output);
                foreach (var call in reply.Calls)
                {
                    if (!bindings.TryGetValue(call.Name, out var tool)) throw new ReadAgentException("InvalidTool");
                    var evidence = Guid.NewGuid();
                    var result = await Invoke(tool, call.Arguments, evidence, false);
                    input.Add(JsonSerializer.SerializeToElement(new { type = "function_call_output", call_id = call.Id,
                        output = JsonSerializer.Serialize(new { source = sources.Count, evidenceId = sources.Last().EvidenceId, untrustedMcpResult = JsonSerializer.Deserialize<JsonElement>(result) }) }));
                }
            }
            throw new ReadAgentException("RoundLimit");

            async Task<string> Invoke(McpAvailableTool tool, string arguments, Guid evidenceId, bool revalidated, bool retain = true, CancellationToken? readToken = null)
            {
                if (++calls > limits.Calls) throw new ReadAgentException("CallLimit");
                if (Bytes(arguments) > 16 * 1024 || !schemas.Valid(tool.InputSchema, arguments)) throw new ReadAgentException("InvalidArguments");
                using var args = JsonDocument.Parse(arguments);
                if (args.RootElement.ValueKind != JsonValueKind.Object) throw new ReadAgentException("InvalidArguments");
                if (!external && (!args.RootElement.TryGetProperty("accountId", out var argAccount) || argAccount.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(argAccount.GetString(), out var proposed) || proposed != who.AccountId)) throw new ReadAgentException("AccountOverride");
                await ValidateContext();
                // Registry independently revalidates the exact version, approval, class, all local access and personal grant.
                var result = await registry.CallAsync(connection, tool.Name, tool.Version, arguments, readToken ?? token);
                (readToken ?? token).ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                used.Add(tool);
                await ValidateContext(); // No result is sent to the model after a detected revocation.
                var bytes = Bytes(result); resultBytes += bytes;
                if (bytes > limits.ResultBytes || resultBytes > limits.TotalResultBytes) throw new ReadAgentException("ResultLimit");
                using var json = JsonDocument.Parse(result);
                AgentPageFacts? paging = null; AgentAttentionFacts? attention = null; AgentAggregateFacts? aggregate = null; AgentFinanceFacts? finance = null;
                if (external) notices.Add("ExternalCompleteness");
                else
                {
                    var data = json.RootElement.GetProperty("structuredContent");
                    if (data.GetProperty("schemaVersion").GetString() != (tool.Name == "agreements_details" ? "2" : "1") || data.GetProperty("accountId").GetGuid() != who.AccountId) throw new ReadAgentException("SourceUnavailable");
                    if (tool.Name is "agreements_list" or "agreements_query" or "agreements_followup" or "agreements_deadlines" or "agreements_followup_history" or "agreements_relations" or "service_requests_query" or "buildings_query")
                    {
                        notices.Add("Paging");
                        paging = new(data.GetProperty("requestedPage").GetInt32(), data.GetProperty("page").GetInt32(), data.GetProperty("pageSize").GetInt32(),
                            data.GetProperty("returnedCount").GetInt32(), data.GetProperty("totalCount").GetInt32(), data.GetProperty("hasMore").GetBoolean(),
                            data.GetProperty("nextPage").ValueKind == JsonValueKind.Null ? null : data.GetProperty("nextPage").GetInt32(),
                            data.GetProperty("complete").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean(), data.GetProperty("limitReached").GetBoolean());
                    }
                    if (tool.Name is "agreements_bases" or "agreements_basis")
                    {
                        if (data.GetProperty("coverage").GetString() != (tool.Name == "agreements_bases" ? "StoredCalculationBases" : "StoredCalculationBasisSnapshot")) throw new ReadAgentException("SourceUnavailable");
                        var p = data.GetProperty("data");
                        var summaries = tool.Name == "agreements_bases" ? p.GetProperty("items").EnumerateArray().ToArray() : [p.GetProperty("summary")];
                        if (summaries.Any(s => s.GetProperty("freshness").GetString() != "NotEvaluated")) throw new ReadAgentException("SourceUnavailable");
                        if (tool.Name == "agreements_bases")
                        {
                            notices.Add("Paging");
                            paging = new(p.GetProperty("requestedPage").GetInt32(), p.GetProperty("page").GetInt32(), p.GetProperty("pageSize").GetInt32(), p.GetProperty("returnedCount").GetInt32(), p.GetProperty("totalCount").GetInt32(), p.GetProperty("hasMore").GetBoolean(),
                                p.GetProperty("nextPage").ValueKind == JsonValueKind.Null ? null : p.GetProperty("nextPage").GetInt32(), p.GetProperty("complete").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean(), p.GetProperty("limitReached").GetBoolean());
                        }
                    }
                    if (tool.Name is "agreements_indices" or "agreements_index_values")
                    {
                        var p = data.GetProperty("data");
                        var coverage = data.GetProperty("coverage").GetString();
                        if (tool.Name == "agreements_indices" ? coverage != "AccountIndexRegister" : coverage != (data.GetProperty("view").GetString() == "Current" ? "CurrentStoredRevisions" : "StoredRevisionHistory")) throw new ReadAgentException("SourceUnavailable");
                        notices.Add("Paging");
                        paging = new(p.GetProperty("requestedPage").GetInt32(), p.GetProperty("page").GetInt32(), p.GetProperty("pageSize").GetInt32(),
                            p.GetProperty("returnedCount").GetInt32(), p.GetProperty("totalCount").GetInt32(), p.GetProperty("hasMore").GetBoolean(),
                            p.GetProperty("nextPage").ValueKind == JsonValueKind.Null ? null : p.GetProperty("nextPage").GetInt32(),
                            p.GetProperty("complete").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean(), p.GetProperty("limitReached").GetBoolean());
                    }
                    if (tool.Name is "agreements_financial_lines" or "agreements_forecast")
                    {
                        var f = data.GetProperty("data");
                        var forecast = tool.Name == "agreements_forecast";
                        var complete = forecast ? data.GetProperty("complete").GetBoolean() : f.GetProperty("versions").GetProperty("complete").GetBoolean() && f.GetProperty("prices").GetProperty("complete").GetBoolean();
                        finance = new(f.GetProperty("currency").GetString(), f.GetProperty("direction").GetString(), data.GetProperty("coverage").GetString()!,
                            forecast ? f.GetProperty("from").GetString() : null, forecast ? f.GetProperty("to").GetString() : null, complete,
                            forecast ? null : f.GetProperty("versions").GetProperty("totalCount").GetInt32(), forecast ? null : f.GetProperty("prices").GetProperty("totalCount").GetInt32(),
                            forecast ? f.GetProperty("periods").GetArrayLength() : null);
                        if (finance.Coverage != (forecast ? "CalculatedForecastNotActualTransactions" : "StoredLineAndPriceHistory")) throw new ReadAgentException("SourceUnavailable");
                    }
                    if (tool.Name == "agreements_insights")
                    {
                        aggregate = new(data.GetProperty("dimension").GetString()!, data.GetProperty("totalCount").GetInt32(),
                            data.GetProperty("groups").GetArrayLength(), data.GetProperty("complete").GetBoolean(), data.GetProperty("groupsOverlap").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean());
                        if (!aggregate.Complete || aggregate.GroupsOverlap || data.GetProperty("dataScope").GetString() != "NonArchivedVisibleAgreements") throw new ReadAgentException("SourceUnavailable");
                        notices.Add("InsightScope");
                    }
                    if (tool.Name == "agreements_prioritized")
                    {
                        if (data.GetProperty("selectionLimit").GetInt32() != 5 || data.GetProperty("isCompleteAgreementList").GetBoolean() ||
                            !data.GetProperty("selectionComplete").GetBoolean()) throw new ReadAgentException("SourceUnavailable");
                        notices.Add("PrioritySelection");
                    }
                    if (tool.Name == "agreements_deadlines")
                    {
                        var categories = data.GetProperty("categories");
                        if (!categories.GetProperty("beforeListFilters").GetBoolean() || !categories.GetProperty("groupsOverlap").GetBoolean() ||
                            data.GetProperty("dataCoverage").GetString() != "StoredOccurrencesOnly") throw new ReadAgentException("SourceUnavailable");
                        notices.Add("DeadlineCategories");
                    }
                    if (tool.Name is "agreements_aggregate" or "service_requests_aggregate" or "buildings_aggregate")
                    {
                        aggregate = new(data.GetProperty("groupBy").GetString()!, data.GetProperty("totalCount").GetInt32(),
                            data.GetProperty("groups").GetArrayLength(), data.GetProperty("complete").GetBoolean(), data.GetProperty("groupsOverlap").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean());
                        if (!data.GetProperty(tool.Name == "buildings_aggregate" ? "countsUniqueBuildings" : tool.Name == "agreements_aggregate" ? "countsUniqueAgreements" : "countsUniqueRequests").GetBoolean() || !aggregate.Complete || aggregate.GroupsOverlap) throw new ReadAgentException("SourceUnavailable");
                    }
                    if (tool.Name == "agreements_attention")
                    {
                        notices.Add("Overlapping");
                        attention = new(data.GetProperty("today").GetString()!, data.GetProperty("notice").GetInt32(), data.GetProperty("renewal").GetInt32(),
                            data.GetProperty("expired").GetInt32(), data.GetProperty("horizonDays").GetInt32());
                    }
                }
                var fingerprint = Fingerprint(result);
                var identical = retainedCalls.FirstOrDefault(c => c.Tool == tool.Name && c.Version == tool.Version && c.Arguments == arguments && c.Fingerprint == fingerprint);
                if (identical is not null) evidenceId = identical.Id;
                sources.Add(new(sources.Count + 1, connection, tool.ConnectionName, tool.Name, tool.Version, paging, attention, revalidated, evidenceId, aggregate, finance));
                if (retain && identical is null) retainedCalls.Add(new(evidenceId, tool.Name, tool.Version, arguments, fingerprint));
                return result;
            }

            async Task ValidateHistoricalResources()
            {
                if (invalidated) return;
                foreach (var past in previousCalls)
                {
                    if (++calls > limits.Calls) throw new ReadAgentException("CallLimit");
                    string fresh;
                    try { fresh = await registry.CallAsync(connection, past.Tool, past.Version, past.Arguments, token); }
                    catch (Exception ex) when (ex is McpClientException or UnauthorizedAccessException)
                    { run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
                    resultBytes += Bytes(fresh);
                    if (Bytes(fresh) > limits.ResultBytes || resultBytes > limits.TotalResultBytes) throw new ReadAgentException("ResultLimit");
                    if (Fingerprint(fresh) != past.Fingerprint) { run.Invalidate(); throw new ReadAgentException("HistoryUnavailable"); }
                }
                await ValidateContext();
            }

            async Task ValidateContext()
            {
                sessions.Verify(conversation, who);
                if (await Require(token) != who) throw new UnauthorizedAccessException();
                if (used.Count == 0) return;
                var fresh = await Catalog(token);
                if (used.Any(t => !fresh.Any(f => f.ConnectionId == connection && f.Name == t.Name && f.Version == t.Version))) throw new UnauthorizedAccessException();
            }
        }
        catch (OperationCanceledException) { throw new ReadAgentException("CancelledOrTimedOut"); }
        catch (UnauthorizedAccessException) { run.ClearDraft(); run.Invalidate(); throw; }
        catch (ReadAgentException) { throw; }
        catch (TenantPlatform.Application.PersonalPages.PersonalPageException e) { throw new ReadAgentException("Draft" + e.Code); }
        catch (McpClientException) { throw new ReadAgentException("SourceUnavailable"); }
        catch (Exception) { throw new ReadAgentException("ModelUnavailable"); } // No provider/remote bodies escape.
    }
    public async Task<AgentReportPreview> PreviewReportAsync(Guid conversation, Guid draftId, Guid revision, int page = 1, CancellationToken ct = default)
    {
        var who = await Require(ct);
        if (drafts is null) throw new ReadAgentException("Disabled");
        using var run = sessions.Start(conversation, who, limits, ct);
        var draft = run.Draft;
        if (draft is null || draft.Id != draftId || draft.Revision != revision) throw new ReadAgentException("DraftConflict");
        AgentReportPreview result;
        try { result = await drafts.PreviewAsync(draft, page, run.Token); }
        catch (ReadAgentException e) when (e.Code == "HistoryUnavailable") { run.ClearDraft(); run.Invalidate(); throw; }
        catch (UnauthorizedAccessException) { run.ClearDraft(); run.Invalidate(); throw; }
        sessions.Verify(conversation, who); run.Token.ThrowIfCancellationRequested();
        return result;
    }
    public async Task<Guid> SaveReportAsync(Guid conversation, Guid draftId, Guid revision, CancellationToken ct = default, bool copy = false, bool? editing = null)
    {
        var who = await Require(ct);
        if (drafts is null) throw new ReadAgentException("Disabled");
        using var run = sessions.Start(conversation, who, limits, ct);
        var draft = run.Draft;
        if (draft is null || draft.Id != draftId || draft.Revision != revision || draft.Definition.ConnectionId != sessions.Connection(conversation, who)) throw new ReadAgentException("DraftConflict");
        if (editing.HasValue && editing.Value != (draft.Edit is not null)) throw new ReadAgentException("DraftConflict");
        try
        {
            var id = await drafts.SaveAsync(draft, run.Token, copy);
            sessions.Verify(conversation, who);
            if (await Require(run.Token) != who) throw new UnauthorizedAccessException();
            run.Token.ThrowIfCancellationRequested(); run.CompleteDraft(draft with { SavedPageId = id, SavedAsCopy = copy });
            return id;
        }
        catch (ReadAgentException e) when (e.Code == "HistoryUnavailable") { run.ClearDraft(); run.Invalidate(); throw; }
        catch (UnauthorizedAccessException) { run.ClearDraft(); run.Invalidate(); throw; }
        catch (TenantPlatform.Application.PersonalPages.PersonalPageException e) { throw new ReadAgentException("Draft" + e.Code); }
        catch (OperationCanceledException) { throw new ReadAgentException("CancelledOrTimedOut"); }
    }
    public static string ModelName(McpAvailableTool tool) => "read_" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(tool.ConnectionId.ToString("N") + "\n" + tool.Name + "\n" + tool.Version)))[..56].ToLowerInvariant();
    private static string Fingerprint(string result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
