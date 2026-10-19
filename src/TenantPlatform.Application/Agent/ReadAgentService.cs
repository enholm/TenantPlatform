using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Agent;

public sealed class ReadAgentService(ICurrentActor actor, IAuthorizationService authorization, McpRegistryService registry,
    IReadAgentModel model, IAgentSchemaValidator schemas, ReadAgentSessions sessions, ReadAgentLimits limits)
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
        return tools.Where(t => t.UseClass == McpToolUse.AutomaticRead &&
            (External(t.ConnectionId, page) || t.Name is "agreements_list" or "agreements_attention" or "agreements_query" or "agreements_aggregate" or "agreements_details")).ToArray();
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
        return (await Catalog(ct)).GroupBy(t => t.ConnectionId).Select(g => new AgentConnection(g.Key, g.First().ConnectionName, g.Count())).ToArray();
    }
    public async Task<Guid> BeginAsync(Guid selectedConnection, Guid expectedAccount, bool sharingAccepted, CancellationToken ct = default)
    {
        var who = await Require(ct);
        if (!Ready) throw new ReadAgentException("Disabled");
        if (who.AccountId != expectedAccount || !sharingAccepted) throw new ReadAgentException("ConsentRequired");
        if (!(await Catalog(ct)).Any(t => t.ConnectionId == selectedConnection)) throw new ReadAgentException("ConnectionUnavailable");
        return sessions.Create(who, selectedConnection);
    }
    public async Task DeleteAsync(Guid conversation, CancellationToken ct = default) => sessions.Delete(conversation, await Require(ct));
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
        using var run = sessions.Start(conversation, who, limits, ct);
        var token = run.Token;
        try
        {
            var selected = (await Catalog(token)).Where(t => t.ConnectionId == connection).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
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
            var previousCalls = history.SelectMany(t => t.Calls).DistinctBy(c => c.Id).ToArray();
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

            async Task<string> Invoke(McpAvailableTool tool, string arguments, Guid evidenceId, bool revalidated)
            {
                if (++calls > limits.Calls) throw new ReadAgentException("CallLimit");
                if (Bytes(arguments) > 16 * 1024 || !schemas.Valid(tool.InputSchema, arguments)) throw new ReadAgentException("InvalidArguments");
                using var args = JsonDocument.Parse(arguments);
                if (args.RootElement.ValueKind != JsonValueKind.Object) throw new ReadAgentException("InvalidArguments");
                if (!external && (!args.RootElement.TryGetProperty("accountId", out var argAccount) || argAccount.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(argAccount.GetString(), out var proposed) || proposed != who.AccountId)) throw new ReadAgentException("AccountOverride");
                await ValidateContext();
                // Registry independently revalidates the exact version, approval, class, all local access and personal grant.
                var result = await registry.CallAsync(connection, tool.Name, tool.Version, arguments, token);
                token.ThrowIfCancellationRequested();
                used.Add(tool);
                await ValidateContext(); // No result is sent to the model after a detected revocation.
                var bytes = Bytes(result); resultBytes += bytes;
                if (bytes > limits.ResultBytes || resultBytes > limits.TotalResultBytes) throw new ReadAgentException("ResultLimit");
                using var json = JsonDocument.Parse(result);
                AgentPageFacts? paging = null; AgentAttentionFacts? attention = null; AgentAggregateFacts? aggregate = null;
                if (external) notices.Add("ExternalCompleteness");
                else
                {
                    var data = json.RootElement.GetProperty("structuredContent");
                    if (data.GetProperty("schemaVersion").GetString() != "1" || data.GetProperty("accountId").GetGuid() != who.AccountId) throw new ReadAgentException("SourceUnavailable");
                    if (tool.Name is "agreements_list" or "agreements_query")
                    {
                        notices.Add("Paging");
                        paging = new(data.GetProperty("requestedPage").GetInt32(), data.GetProperty("page").GetInt32(), data.GetProperty("pageSize").GetInt32(),
                            data.GetProperty("returnedCount").GetInt32(), data.GetProperty("totalCount").GetInt32(), data.GetProperty("hasMore").GetBoolean(),
                            data.GetProperty("nextPage").ValueKind == JsonValueKind.Null ? null : data.GetProperty("nextPage").GetInt32(),
                            data.GetProperty("complete").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean(), data.GetProperty("limitReached").GetBoolean());
                    }
                    if (tool.Name == "agreements_aggregate")
                    {
                        aggregate = new(data.GetProperty("groupBy").GetString()!, data.GetProperty("totalCount").GetInt32(),
                            data.GetProperty("groups").GetArrayLength(), data.GetProperty("complete").GetBoolean(), data.GetProperty("groupsOverlap").GetBoolean(), data.GetProperty("snapshotConsistent").GetBoolean());
                        if (!data.GetProperty("countsUniqueAgreements").GetBoolean() || !aggregate.Complete || aggregate.GroupsOverlap) throw new ReadAgentException("SourceUnavailable");
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
                sources.Add(new(sources.Count + 1, connection, tool.ConnectionName, tool.Name, tool.Version, paging, attention, revalidated, evidenceId, aggregate));
                if (identical is null) retainedCalls.Add(new(evidenceId, tool.Name, tool.Version, arguments, fingerprint));
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
        catch (UnauthorizedAccessException) { run.Invalidate(); throw; }
        catch (ReadAgentException) { throw; }
        catch (McpClientException) { throw new ReadAgentException("SourceUnavailable"); }
        catch (Exception) { throw new ReadAgentException("ModelUnavailable"); } // No provider/remote bodies escape.
    }
    public static string ModelName(McpAvailableTool tool) => "read_" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(tool.ConnectionId.ToString("N") + "\n" + tool.Name + "\n" + tool.Version)))[..56].ToLowerInvariant();
    private static string Fingerprint(string result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
