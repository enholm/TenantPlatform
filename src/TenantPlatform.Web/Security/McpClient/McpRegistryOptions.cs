using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenantPlatform.Application.Mcp;

namespace TenantPlatform.Web.Security.McpClient;

public sealed class McpRegistryOptions
{
    public bool Enabled { get; set; }
    public string[] InternalAllowedPrivateAddresses { get; set; } = [];
    public List<McpExternalProfileOptions> Profiles { get; set; } = [];
}
public sealed class McpExternalProfileOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string MetadataAddress { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Resource { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Callback { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    // Exact operator-approved IPs, not hostnames/CIDRs supplied by account administrators.
    public string[] AllowedPrivateAddresses { get; set; } = [];
    public override string ToString() => "[operator MCP profile]";
}
public sealed record McpRuntimeProfile(McpAuthProfile Public, string RegistrationId, string Callback, string Secret, string[] Scopes, string[] AllowedPrivateAddresses, string? MetadataAddress = null)
{
    public override string ToString() => "[loaded MCP profile]";
}
public sealed class McpAuthProfiles : IMcpAuthProfiles
{
    public IReadOnlyList<McpRuntimeProfile> Loaded { get; }
    public McpAuthProfiles(InternalMcpClientOptions own, McpRegistryOptions options)
    {
        var loaded = new List<McpRuntimeProfile>();
        if (options.Enabled && own.Available)
        {
            foreach (var address in options.InternalAllowedPrivateAddresses)
                if (!System.Net.IPAddress.TryParse(address, out var ip) || McpNetworkPolicy.Forbidden(ip)) throw new InvalidOperationException("Invalid private address approval.");
            var version = own.ServiceRequestsAllowed
                ? Hash(new { own.ClientId, own.Issuer, own.Resource, own.Callback, own.ServiceRequestsAllowed, options.InternalAllowedPrivateAddresses })
                : Hash(new { own.ClientId, own.Issuer, own.Resource, own.Callback, options.InternalAllowedPrivateAddresses });
            loaded.Add(new(new("tenantplatform", version, "TenantPlatform", own.Resource, new(own.ClientId, own.Issuer, own.Resource, ProfileVersion: version), true),
                InternalMcpClientOptions.Registration, own.Callback, own.ClientSecret, [OAuth.OAuthIssuerOptions.ReadScope, "offline_access", ..own.ServiceRequestsAllowed ? new[] { TenantPlatform.Application.ServiceRequests.ServiceRequestReportScopes.Read } : Array.Empty<string>()], options.InternalAllowedPrivateAddresses));
            foreach (var value in options.Profiles)
            {
                if (value.Id.Length is < 1 or > 60 || !value.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || value.Id == "tenantplatform" ||
                    loaded.Any(x => x.Public.Id == value.Id) || string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 100 ||
                    value.ClientId.Length is < 1 or > 200 || value.ClientSecret.Length < 32 || value.Scopes.Length is < 1 or > 10 ||
                    value.Scopes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl))) throw new InvalidOperationException("Invalid operator MCP profile.");
                foreach (var address in new[] { value.Endpoint, value.Resource, value.Issuer, value.Callback, value.MetadataAddress }) McpNetworkPolicy.ValidateUrl(address);
                if (new Uri(value.MetadataAddress).GetLeftPart(UriPartial.Authority) != new Uri(value.Issuer).GetLeftPart(UriPartial.Authority) ||
                    new Uri(value.Endpoint).GetLeftPart(UriPartial.Authority) != new Uri(value.Resource).GetLeftPart(UriPartial.Authority) ||
                    new Uri(value.Callback).GetLeftPart(UriPartial.Authority) != new Uri(own.Callback).GetLeftPart(UriPartial.Authority) ||
                    new Uri(value.Callback).AbsolutePath != "/mcp-client/registry/callback/" + value.Id) throw new InvalidOperationException("Invalid MCP profile binding.");
                foreach (var address in value.AllowedPrivateAddresses)
                    if (!System.Net.IPAddress.TryParse(address, out var ip) || McpNetworkPolicy.Forbidden(ip)) throw new InvalidOperationException("Invalid private address approval.");
                var fingerprint = Hash(new { value.Id, value.Issuer, value.MetadataAddress, value.Endpoint, value.Resource, value.ClientId, value.Callback, value.Scopes, value.AllowedPrivateAddresses });
                loaded.Add(new(new(value.Id, fingerprint, value.Name, value.Endpoint, new(value.ClientId, value.Issuer, value.Resource, ProfileVersion: fingerprint, External: true), true),
                    "mcp-registry-" + value.Id, value.Callback, value.ClientSecret, value.Scopes, value.AllowedPrivateAddresses, value.MetadataAddress));
            }
        }
        Loaded = loaded;
    }
    public IReadOnlyList<McpAuthProfile> GetAll() => Loaded.Select(x => x.Public).ToArray();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
