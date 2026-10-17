using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TenantPlatform.Application.Mcp;

namespace TenantPlatform.Web.Security.McpClient;

public sealed class McpClientTokenProtection(IDataProtectionProvider provider) : IMcpClientTokenProtection
{
    private IDataProtector Protector(Guid user, McpClientBinding binding) => binding.ConnectionId == Guid.Empty ? provider.CreateProtector("TenantPlatform.InternalMcp.tokens.v1",
        user.ToString("D"), binding.ClientId, binding.Issuer, binding.Resource)
        : provider.CreateProtector("TenantPlatform.Mcp.tokens.v2", user.ToString("D"), binding.ConnectionId.ToString("D"), binding.ProfileVersion, binding.ClientId, binding.Issuer, binding.Resource);
    public string Protect(Guid user, McpClientBinding binding, McpClientTokens tokens) => Protector(user, binding).Protect(JsonSerializer.Serialize(tokens));
    public McpClientTokens Unprotect(Guid user, McpClientBinding binding, string payload) =>
        JsonSerializer.Deserialize<McpClientTokens>(Protector(user, binding).Unprotect(payload)) ?? throw new InvalidOperationException("Invalid protected credential.");
}
