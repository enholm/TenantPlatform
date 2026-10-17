using TenantPlatform.Web.Security.OAuth;

namespace TenantPlatform.Web.Security.McpClient;

public sealed class InternalMcpClientOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Callback { get; set; } = "";
    public bool Available { get; internal set; }
    public const string Registration = "tenantplatform-internal-mcp";
    public const string UserProperty = "tenantplatform.mcp.user";
    public const string PendingProperty = "tenantplatform.mcp.pending";
    public void Inspect(OAuthIssuerOptions issuer)
    {
        static bool Https(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        Available = Enabled && issuer.Enabled && Https(Issuer) && Https(Resource) && Https(Callback) &&
            Issuer == issuer.Issuer && Resource == issuer.Resource && ClientId is { Length: > 0 and <= 200 } && ClientSecret is { Length: >= 32 } &&
            new Uri(Resource).AbsolutePath == "/mcp" && new Uri(Callback).AbsolutePath == "/mcp-client/callback" &&
            new Uri(Callback).GetLeftPart(UriPartial.Authority) == new Uri(Issuer).GetLeftPart(UriPartial.Authority) &&
            issuer.Clients.Any(x => x.ClientId == ClientId && x.Secret == ClientSecret && x.RedirectUris.Count == 1 && x.RedirectUris[0] == Callback);
    }
}
