namespace TenantPlatform.Mcp;

public sealed class McpOptions
{
    public const string ReadScope = "tenantplatform.agreements.read";
    public string Issuer { get; set; } = "";
    public string Resource { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string[] Clients { get; set; } = [];
    public string[] AllowedOrigins { get; set; } = [];
    public string[] TrustedProxies { get; set; } = [];
    public string MetadataUrl => new Uri(new Uri(Resource), "/.well-known/oauth-protected-resource/mcp").AbsoluteUri;
    public void Validate()
    {
        foreach (var value in new[] { Issuer, Resource })
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("MCP requires fixed HTTPS issuer and resource.");
        if (new Uri(Resource).AbsolutePath != "/mcp" || string.IsNullOrWhiteSpace(ConnectionString) || Clients.Length == 0 || Clients.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("MCP requires /mcp resource, database connection and approved clients.");
        foreach (var origin in AllowedOrigins)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.GetLeftPart(UriPartial.Authority) != origin)
                throw new InvalidOperationException("Origins must be exact HTTPS origins.");
        foreach (var proxy in TrustedProxies)
            if (!System.Net.IPAddress.TryParse(proxy, out _)) throw new InvalidOperationException("Trusted proxies must be IP addresses.");
    }
}
