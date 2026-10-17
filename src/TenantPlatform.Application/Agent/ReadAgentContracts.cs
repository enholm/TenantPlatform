using System.Text.Json;

namespace TenantPlatform.Application.Agent;

// No model name/key/provider types in the Application contract.
public sealed record ReadAgentLimits
{
    public int PromptCharacters { get; init; } = 4000;
    public int ContextBytes { get; init; } = 192 * 1024;
    public int TotalModelBytes { get; init; } = 1024 * 1024;
    public int Tools { get; init; } = 12;
    public int Rounds { get; init; } = 5;
    public int Calls { get; init; } = 8;
    public int ResultBytes { get; init; } = 64 * 1024;
    public int TotalResultBytes { get; init; } = 128 * 1024;
    public int AnswerCharacters { get; init; } = 12000;
    public int OutputTokens { get; init; } = 4096;
    public int Seconds { get; init; } = 60;
    public int ConcurrentRunsPerUser { get; init; } = 1;
    public bool Valid => PromptCharacters is >= 1 and <= 16000 && ContextBytes is >= 1024 and <= 512 * 1024 &&
        TotalModelBytes is >= 1024 and <= 2 * 1024 * 1024 && Tools is >= 1 and <= 32 && Rounds is >= 1 and <= 10 && Calls is >= 1 and <= 20 &&
        ResultBytes is >= 1 and <= 64 * 1024 && TotalResultBytes >= ResultBytes && TotalResultBytes <= 256 * 1024 &&
        AnswerCharacters is >= 1 and <= 32000 && OutputTokens is >= 128 and <= 8192 && Seconds is >= 1 and <= 120 && ConcurrentRunsPerUser is >= 1 and <= 4;
}
public sealed record AgentTool(string Name, string? Description, JsonElement Parameters);
public sealed record AgentFunctionCall(string Id, string Name, string Arguments);
// Opaque provider output items, retained only within this run, for Responses reasoning/function continuity.
public sealed record AgentModelReply(string? Text, IReadOnlyList<AgentFunctionCall> Calls, IReadOnlyList<JsonElement> Output);
public interface IReadAgentModel
{
    bool Ready { get; }
    Task<AgentModelReply> RespondAsync(IReadOnlyList<AgentTool> tools, IReadOnlyList<JsonElement> input, ReadAgentLimits limits, CancellationToken ct);
}
public interface IAgentSchemaValidator
{
    bool Supported(string schema);
    bool Valid(string schema, string arguments);
}
public sealed record AgentConnection(Guid Id, string Name, int ToolCount);
public sealed record AgentPageFacts(int RequestedPage, int Page, int PageSize, int ReturnedCount, int TotalCount, bool HasMore, int? NextPage, bool Complete, bool SnapshotConsistent, bool LimitReached);
public sealed record AgentAttentionFacts(string Today, int Notice, int Renewal, int Expired, int HorizonDays);
public sealed record AgentSource(int Number, Guid ConnectionId, string ConnectionName, string Tool, string Version, AgentPageFacts? Page = null, AgentAttentionFacts? Attention = null);
public sealed record AgentAnswer(string Text, IReadOnlyList<AgentSource> Sources, IReadOnlyList<string> Notices);
public sealed class ReadAgentException(string code) : Exception(code) { public string Code { get; } = code; }
