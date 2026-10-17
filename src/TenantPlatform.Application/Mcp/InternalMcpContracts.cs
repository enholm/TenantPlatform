using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Mcp;

public sealed record McpClientBinding(string ClientId, string Issuer, string Resource);
public sealed record McpClientStatus(bool Connected, bool Pending, DateTimeOffset? ExpiresUtc);
public sealed record McpConnectRequest(Guid UserId, Guid PendingId);
public sealed record McpClientAccount(Guid Id, string Name);
public sealed record McpClientAccounts(string SchemaVersion, McpClientAccount[] Accounts, int ReturnedCount, bool Complete);
public sealed record McpClientAttention(string SchemaVersion, Guid AccountId, DateOnly Today, int Notice, int Renewal, int Expired, int HorizonDays);
public sealed record McpClientAgreement(Guid Id, string Title, string CounterpartyName, string Type, string Status, DateOnly? EndDate, DateOnly? NoticeDeadline, bool IsArchived);
public sealed record McpClientAgreements(string SchemaVersion, Guid AccountId, int RequestedPage, int Page, int PageSize, int TotalCount,
    int ReturnedCount, bool HasMore, int? NextPage, bool Complete, bool SnapshotConsistent, bool LimitReached, McpClientAgreement[] Items);
public sealed record McpClientQuery(Guid AccountId, string? Search = null, AgreementStatus? Status = null, AgreementType? Type = null,
    TenantPlatform.Application.Agreements.AgreementAttention? Attention = null, int Page = 1);

// Do not use a generated record ToString for secrets. These values never leave backend adapters/use cases.
public sealed class McpClientTokens
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public override string ToString() => "[protected MCP credentials]";
}
public sealed record McpTokenReceipt(Guid Subject, string GrantId, DateTimeOffset AccessExpiresUtc, McpClientTokens Tokens);
public sealed record McpCallbackReceipt(Guid Initiator, Guid PendingId, McpTokenReceipt Token);
public sealed class McpClientException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public interface IMcpClientTokenProtection
{
    string Protect(Guid user, McpClientBinding binding, McpClientTokens tokens);
    McpClientTokens Unprotect(Guid user, McpClientBinding binding, string payload);
}
public interface IMcpClientSlot
{
    Task<McpClientCredential?> ReadAsync(CancellationToken ct);
    // Each save commits independently, while the outer cross-process coordination lock stays held.
    Task SaveAsync(McpClientCredential? value, CancellationToken ct);
}
public interface IMcpClientStore
{
    Task<T> WithUserLockAsync<T>(Guid user, Func<IMcpClientSlot, CancellationToken, Task<T>> action, CancellationToken ct);
}
public interface IDelegatedMcpGateway
{
    Task<McpCallbackReceipt> ReadCallbackAsync(CancellationToken ct);
    Task<McpTokenReceipt> RefreshAsync(McpClientTokens tokens, CancellationToken ct);
    Task<bool> RevokeAsync(McpClientTokens tokens, CancellationToken ct);
    Task<McpClientAccounts> AccountsAsync(string accessToken, CancellationToken ct);
    Task<McpClientAttention> AttentionAsync(string accessToken, Guid accountId, CancellationToken ct);
    Task<McpClientAgreements> AgreementsAsync(string accessToken, McpClientQuery query, CancellationToken ct);
}
