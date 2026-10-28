using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TenantPlatform.Application.Agent;
using TenantPlatform.Infrastructure.Agent;

// TEST ONLY: the production HTTP adapter reads realistic Responses envelopes. Never contacts OpenAI.
sealed class AgentProtocolFixture
{
    public static async Task CheckReportSchemaAsync()
    {
        var fixture = new AgentProtocolFixture { ReportArguments = "{\"title\":\"Mine kontrakter\"}" };
        using var http = new HttpClient(new ModelHandler(fixture));
        var model = new OpenAiReadAgentModel(http,
            Options.Create(new ReadAgentOptions {
                Enabled = true, Model = "synthetic-model", ApiKey = "synthetic-agent-key" }));
        foreach (var prompt in new[] {
            "Lag en rapport på mine sider som viser en paginert liste med kontrakter. Siden kan hete Mine kontrakter",
            "Lag en rapport på mine sider som viser avtaler for juli, augist og september 2026.",
            "Lag bare et diagram med aktive avtaler per bygg de neste tre månedene." })
        {
            if (!AgentReportDrafts.Requested(prompt, false)) throw new Exception("Report request must expose the proposal capability");
            var reply = await model.RespondAsync([AgentReportDrafts.Tool()],
                [JsonSerializer.SerializeToElement(new { role = "assistant", content = "Jeg kan ikke opprette et rapportutkast herfra." }),
                 JsonSerializer.SerializeToElement(new { role = "user", content = prompt })], new(), CancellationToken.None);
            if (reply.Calls.Count != 1 || reply.Calls[0].Name != "propose_report_page") throw new Exception("Report proposal did not pass the model transport");
        }
        fixture.ReportArguments = null;
        await model.RespondAsync([], [JsonSerializer.SerializeToElement(new { role = "user", content = "history-only" })], new(), CancellationToken.None);
        var schema = AgentReportDrafts.Tool().Parameters;
        if (schema.GetProperty("additionalProperties").GetBoolean() ||
            schema.GetProperty("required").GetArrayLength() != 4 ||
            schema.GetProperty("properties").TryGetProperty("accountId", out _)) throw new Exception("Proposal authority/required-field contract changed");
        var properties = schema.GetProperty("properties");
        if (!properties.TryGetProperty("columns", out _) || !properties.TryGetProperty("groupBy", out var grouping) ||
            !grouping.GetRawText().Contains("\"Building\"") || !grouping.GetRawText().Contains("\"Status\"")) throw new Exception("9D proposal presentation schema missing");
        foreach (var prompt in new[] { "Fjern avtaletabellen.", "Grupper diagrammet etter status i stedet.", "Flytt nøkkeltallet nederst." })
            if (!AgentReportDrafts.Requested(prompt, true)) throw new Exception("9D follow-up capability not recognized");
        foreach (var (status, code) in new[] { (400, "ModelRequestRejected"), (422, "ModelRequestRejected"), (401, "ModelAuthentication"), (403, "ModelForbidden"), (404, "ModelNotFound"), (429, "ModelRateLimited"), (503, "ModelUnavailable") })
        {
            fixture.HttpStatus = status;
            try { await model.RespondAsync([], [JsonSerializer.SerializeToElement(new { role = "user", content = "diagnostic" })], new(), default); throw new Exception("Expected safe HTTP error"); }
            catch (ReadAgentException e) when(e.Code == code) { if (e.ToString().Contains("private-provider-error")) throw new Exception("Provider body escaped"); }
        }
        fixture.HttpStatus = 200; fixture.InvalidJson = true;
        try { await model.RespondAsync([], [JsonSerializer.SerializeToElement(new { role = "user", content = "diagnostic" })], new(), default); throw new Exception("Expected invalid response"); }
        catch (ReadAgentException e) when(e.Code == "ModelResponseInvalid") { }
        fixture.InvalidJson = false;
        foreach (var (prompt, code) in new[] { ("connection-failed", "ModelConnectionFailed"), ("malformed-shape", "ModelResponseInvalid") })
        {
            try { await model.RespondAsync([], [JsonSerializer.SerializeToElement(new { role = "user", content = prompt })], new(), default); throw new Exception("Expected safe adapter error"); }
            catch (ReadAgentException e) when(e.Code == code) { if (e.ToString().Contains("private-provider-error")) throw new Exception("Transport details escaped"); }
        }
        Console.WriteLine("Report function schema/Responses transport checks passed (synthetic HTTP, no database or model calls).");
    }
    private sealed class ModelHandler(AgentProtocolFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => fixture.Reply(request, ct);
    }
    public int HttpStatus = 200;
    public bool InvalidJson;
    public Func<Task>? BeforeReply, AfterMcp, BeforeReport;
    public bool McpUnavailable, BlockMcp;
    public readonly TaskCompletionSource McpStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource McpCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? ReportArguments;
    public bool ReportReadFirst;
    public string OtherTool = "";
    public Guid ForeignAccount;
    public Guid ReportAgreement;
    public Guid ReportIndex;
    public Guid ReportBasis;
    public Guid? ExternalAccountSeen;
    public readonly List<string> Payloads = [];
    public TaskCompletionSource? WhenBlocked;
    public readonly TaskCompletionSource Blocking = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<HttpResponseMessage> Reply(HttpRequestMessage request, CancellationToken ct)
    {
        var raw = await request.Content!.ReadAsStringAsync(ct);
        lock (Payloads) Payloads.Add(raw);
        using var json = JsonDocument.Parse(raw); var root = json.RootElement;
        if (request.RequestUri!.AbsoluteUri != "https://api.openai.com/v1/responses" || request.Headers.Authorization?.Parameter != "synthetic-agent-key" ||
            root.GetProperty("store").GetBoolean() || root.GetProperty("tools").EnumerateArray().Any(t => t.GetProperty("type").GetString() != "function" || t.GetProperty("strict").GetBoolean())) throw new Exception("Unexpected model transport");
        // Function arguments have an object root. A generated nullable reference-type root is not a function parameter object.
        // Reject at the synthetic HTTP boundary as well as checking the serialized schema, rather than accepting every tool definition.
        if (root.GetProperty("tools").EnumerateArray().Any(t =>
            !t.GetProperty("parameters").TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object"))
            return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"code\":\"invalid_function_parameters\"}}") };
        var reportAvailable = root.GetProperty("tools").EnumerateArray().Any(t => t.GetProperty("name").GetString() == AgentReportDrafts.ToolName);
        var instructions = root.GetProperty("instructions").GetString()!;
        if (!instructions.Contains(reportAvailable ? "CURRENT REPORT CAPABILITY: propose_report_page IS AVAILABLE" : "CURRENT REPORT CAPABILITY: propose_report_page is NOT available"))
            throw new Exception("Current report capability must be present in trusted instructions and match actual supplied tools");
        var allInput = root.GetProperty("input").EnumerateArray().ToArray();
        var userIndex = Array.FindLastIndex(allInput, i => i.TryGetProperty("role", out var r) && r.GetString() == "user");
        var mode = allInput[userIndex].GetProperty("content").GetString()!;
        if (BeforeReply is { } action) { BeforeReply = null; await action(); }
        if (HttpStatus != 200) return new((HttpStatusCode)HttpStatus) { Content = new StringContent("private-provider-error synthetic-agent-key") };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("not JSON private-provider-error") };
        if (mode == "connection-failed") throw new HttpRequestException("private-provider-error synthetic-agent-key");
        if (mode == "malformed-shape") return new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"completed\",\"output\":{}}") };
        if (mode == "model-error") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private-provider-error synthetic-agent-key") };
        if (mode is "cancel" or "timeout") { Blocking.TrySetResult(); WhenBlocked?.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        if (mode == "oversize-response") return new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 600000)) };
        var input = root.GetProperty("input"); var completed = allInput.Skip(userIndex + 1).Count(i => i.TryGetProperty("type", out var t) && t.GetString() == "function_call_output");
        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        var output = new List<object>();
        if (ReportArguments is { } reportArgs)
        {
            if (ReportReadFirst && completed == 0)
            {
                var query = tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("page", out _) && t.GetProperty("parameters").GetProperty("properties").TryGetProperty("type", out _) && !t.GetProperty("parameters").GetProperty("properties").TryGetProperty("attention", out _) && !t.GetProperty("parameters").GetProperty("properties").TryGetProperty("kind", out _));
                var account = query.GetProperty("parameters").GetProperty("properties").GetProperty("accountId").GetProperty("const").GetString();
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] {
                    new { type = "function_call", call_id = "intent_read", name = query.GetProperty("name").GetString(), arguments = JsonSerializer.Serialize(new { accountId = account, search = "Draft fixture" }) } } }), Encoding.UTF8, "application/json") };
            }
            if (BeforeReport is { } beforeReport) { BeforeReport = null; await beforeReport(); }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] {
                new { type = "function_call", call_id = "draft_proposal", name = "propose_report_page", arguments = reportArgs } } }), Encoding.UTF8, "application/json") };
        }

        if (mode == "builtin") return new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"web_search_call\"}]}", Encoding.UTF8, "application/json") };
        if (mode == "reasoning" && completed == 0) output.Add(new { type = "reasoning", id = "rs_fixture", summary = Array.Empty<object>(), encrypted_content = "synthetic-encrypted-reasoning" });
        if ((completed < (mode == "multi" ? 2 : 1) || mode == "round-limit") && mode != "history-only")
        {
            var tool = mode == "building-aggregate" ? tools.Single(t => t.GetProperty("description").GetString()!.StartsWith("Count unique buildings"))
                : mode == "building-query" ? tools.Single(t => t.GetProperty("description").GetString()!.StartsWith("Read the authorized account building LIST"))
                : mode == "request-aggregate" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("groupBy", out _) && t.GetProperty("parameters").GetProperty("properties").GetProperty("status").GetRawText().Contains("Submitted"))
                : mode is "own-list" or "protected" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("attention", out _))
                : mode == "own-query" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("page", out _) && t.GetProperty("parameters").GetProperty("properties").TryGetProperty("type", out _) && !t.GetProperty("parameters").GetProperty("properties").TryGetProperty("attention", out _) && !t.GetProperty("parameters").GetProperty("properties").TryGetProperty("kind", out _))
                : mode == "own-aggregate" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("groupBy", out _) && t.GetProperty("parameters").GetProperty("properties").TryGetProperty("type", out _))
                : mode == "own-priority" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("prioritized selection"))
                : mode == "own-insights" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("dimension", out _))
                : mode == "own-deadlines" ? tools.Single(t => t.GetProperty("parameters").GetProperty("properties").TryGetProperty("mine", out _))
                : mode == "own-followup" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Read stored deadline occurrences"))
                : mode == "own-events" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Read stored follow-up events"))
                : mode == "own-relations" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Find exact relation IDs"))
                : mode == "own-finance" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Read stored financial line versions"))
                : mode == "own-bases" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Find stored calculation bases"))
                : mode == "own-basis" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Read one stored calculation basis snapshot"))
                : mode == "own-indices" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Find account index register entries"))
                : mode == "own-index-values" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Read stored index values"))
                : mode == "own-forecast" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Calculate the existing agreement forecast"))
                : mode == "own-details" ? tools.Single(t => t.GetProperty("description").GetString()!.Contains("Contract v2."))
                : tools.FirstOrDefault(t => t.GetProperty("parameters").GetProperty("properties").EnumerateObject().Count() == 1, tools[0]);
            var args = new JsonObject();
            if (tool.GetProperty("parameters").TryGetProperty("properties", out var properties) && properties.TryGetProperty("accountId", out var account))
                args["accountId"] = mode == "override" ? ForeignAccount.ToString() : account.TryGetProperty("const", out var fixedAccount) ? fixedAccount.GetString() : ForeignAccount.ToString();
            if (mode == "building-aggregate") args["groupBy"] = "City";
            if (mode == "own-bases") { args["direction"] = "Cost"; args["agreementId"] = ReportAgreement.ToString(); }
            if (mode == "own-basis") args["basisId"] = ReportBasis.ToString();
            if (mode == "own-relations") args["kind"] = "Building";
            if (mode == "own-insights") args["dimension"] = "Type";
            if (mode is "own-aggregate" or "request-aggregate") args["groupBy"] = mode == "request-aggregate" ? "Status" : "Building";
            if (mode is "own-details" or "own-followup" or "own-events" or "own-finance" or "own-forecast") args["agreementId"] = ReportAgreement.ToString();
            if (mode == "own-index-values") { args["indexId"] = ReportIndex.ToString(); args["view"] = "History"; args["from"] = "2026-01-01"; args["to"] = "2026-12-31"; }
            if (mode == "own-forecast") { args["from"] = "2026-01-01"; args["to"] = "2026-03-31"; }
            if (mode == "invalid") args["unexpected"] = true;
            output.Add(new { type = "function_call", id = "fc_" + completed, call_id = "call_" + completed,
                name = mode == "unknown" ? "not_available" : mode == "cross" ? OtherTool : tool.GetProperty("name").GetString(), arguments = args.ToJsonString(), status = "completed" });
        }
        else output.Add(new { type = "message", id = "msg_fixture", role = "assistant", status = "completed", content = new[] {
            new { type = "output_text", text = mode == "protected" ? "PROTECTED_DERIVED_MARKER" : mode == "long-answer" ? new string('z', 40000) : "Synthetic answer [1]. <script>alert('unsafe')</script><img src='https://evil.test/pixel'>", annotations = Array.Empty<object>() } } });
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id = "resp_fixture", @object = "response", created_at = 1770000000, model = "synthetic-no-network",
            status = mode == "incomplete" ? "incomplete" : "completed", output, usage = new { input_tokens = 50, output_tokens = 20, total_tokens = 70 } }), Encoding.UTF8, "application/json") };
    }
}
