namespace TenantPlatform.Core.Identity;

// Technical, server-side delegated connection. Payload is protected by the host, never plaintext tokens.
public sealed class McpClientCredential
{
    public Guid UserId { get; set; }
    // Empty is the reserved 5B3 legacy slot; never reassigned to a registered server.
    public Guid ConnectionId { get; set; }
    public string ProfileVersion { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Resource { get; set; } = "";
    public string? ProtectedTokens { get; set; }
    public string? GrantId { get; set; }
    public DateTimeOffset? GrantExpiresUtc { get; set; }
    public DateTimeOffset? AccessExpiresUtc { get; set; }
    public Guid? PendingId { get; set; }
    public DateTimeOffset? PendingExpiresUtc { get; set; }
}
