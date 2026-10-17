using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Validation;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Web.Security.OAuth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.McpClient;

public sealed class DelegatedMcpGateway(IHttpContextAccessor http, OpenIddictClientService oauth, OpenIddictValidationService validator,
    IHttpClientFactory clients, InternalMcpClientOptions options, McpRuntimeProfile? profile = null) : IDelegatedMcpGateway
{
    private string Registration => profile?.RegistrationId ?? InternalMcpClientOptions.Registration;
    private string Resource => profile?.Public.Binding.Resource ?? options.Resource;
    private string Endpoint => profile?.Public.Endpoint ?? options.Resource;
    private bool External => profile?.Public.Binding.External == true;
    public const string HttpClientName = "TenantPlatform.InternalMcp";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public async Task<McpCallbackReceipt> ReadCallbackAsync(CancellationToken ct)
    {
        var result = await http.HttpContext!.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null ||
            !Guid.TryParseExact((result.Properties.Items.TryGetValue(InternalMcpClientOptions.UserProperty, out var initiating) ? initiating : null), "D", out var user) ||
            !Guid.TryParseExact((result.Properties.Items.TryGetValue(InternalMcpClientOptions.PendingProperty, out var pendingValue) ? pendingValue : null), "D", out var pending))
            throw new McpClientException("CONNECTION_REJECTED");
        if (profile is not null && (!result.Properties.Items.TryGetValue(OpenIddictClientAspNetCoreConstants.Properties.RegistrationId, out var registered) || registered != Registration)) throw new McpClientException("CONNECTION_REJECTED");
        var access = result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken);
        var refresh = result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken);
        if (External)
        {
            if (!DateTimeOffset.TryParse(result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessTokenExpirationDate),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var end)) throw new McpClientException("CONNECTION_REJECTED");
            return new(user, pending, ExternalReceipt(access, refresh, end));
        }
        return new(user, pending, await Validate(access, refresh, ct));
    }
    public async Task<McpTokenReceipt> RefreshAsync(McpClientTokens tokens, CancellationToken ct)
    {
        var result = await oauth.AuthenticateWithRefreshTokenAsync(new() { RegistrationId = Registration,
            RefreshToken = tokens.RefreshToken, Resources = [Resource], DisableUserInfo = true, CancellationToken = ct });
        return External ? ExternalReceipt(result.AccessToken, result.RefreshToken ?? tokens.RefreshToken, result.AccessTokenExpirationDate) : await Validate(result.AccessToken, result.RefreshToken, ct);
    }
    private static McpTokenReceipt ExternalReceipt(string? access, string? refresh, DateTimeOffset? end)
    {
        // OAuth tokens may be opaque. Trust the SDK-validated code/state/TLS token response, not JWT decoding.
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh) || end is null) throw new McpClientException("CONNECTION_REJECTED");
        return new(Guid.Empty, "external-oauth", end.Value, new() { AccessToken = access, RefreshToken = refresh });
    }
    private async Task<McpTokenReceipt> Validate(string? access, string? refresh, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) throw new McpClientException("CONNECTION_REJECTED");
        var principal = await validator.ValidateAccessTokenAsync(access, ct); // Signature/type/issuer/audience/lifetime, never decode-only.
        if (!Guid.TryParseExact(principal.GetClaim(Claims.Subject), "D", out var user) || user == Guid.Empty ||
            principal.GetClaim(Claims.ClientId) != options.ClientId || !principal.HasScope(OAuthIssuerOptions.ReadScope) ||
            string.IsNullOrEmpty(principal.GetAuthorizationId()) || principal.GetExpirationDate() is not { } end)
            throw new McpClientException("CONNECTION_REJECTED");
        return new(user, principal.GetAuthorizationId()!, end, new() { AccessToken = access, RefreshToken = refresh });
    }
    public async Task<bool> RevokeAsync(McpClientTokens tokens, CancellationToken ct)
    {
        await oauth.RevokeTokenAsync(new() { RegistrationId = Registration, Token = tokens.RefreshToken,
            TokenTypeHint = TokenTypeHints.RefreshToken, CancellationToken = ct });
        return true;
    }
    public Task<McpClientAccounts> AccountsAsync(string accessToken, CancellationToken ct) => Read<McpClientAccounts>(accessToken, "accounts_list", [], ct);
    public Task<McpClientAttention> AttentionAsync(string accessToken, Guid accountId, CancellationToken ct) =>
        Read<McpClientAttention>(accessToken, "agreements_attention", new() { ["accountId"] = accountId }, ct);
    public Task<McpClientAgreements> AgreementsAsync(string accessToken, McpClientQuery query, CancellationToken ct)
    {
        var args = new Dictionary<string, object?> { ["accountId"] = query.AccountId, ["page"] = query.Page };
        if (query.Search is not null) args["search"] = query.Search;
        if (query.Status.HasValue) args["status"] = query.Status.Value.ToString();
        if (query.Type.HasValue) args["type"] = query.Type.Value.ToString();
        if (query.Attention.HasValue) args["attention"] = query.Attention.Value.ToString();
        return Read<McpClientAgreements>(accessToken, "agreements_list", args, ct);
    }
    private HttpClient Backend() => clients.CreateClient(profile is null ? HttpClientName : "TenantPlatform.Mcp." + profile.Public.Id);
    private Task<ModelContextProtocol.Client.McpClient> Connect(string access, HttpClient backend, CancellationToken ct) =>
        ModelContextProtocol.Client.McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri(Endpoint), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + access } }, backend), cancellationToken: ct);
    public async Task<McpToolDiscovery> DiscoverAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(15));
            using var backend = Backend(); await using var client = await Connect(accessToken, backend, budget.Token);
            var tools = new List<McpToolMetadata>(); string? cursor = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 0; page < 5; page++)
            {
                var result = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, budget.Token);
                foreach (var tool in result.Tools)
                {
                    if (tools.Count == 100) return new(tools, false);
                    tools.Add(new(tool.Name, tool.Description, tool.InputSchema.GetRawText(), tool.OutputSchema?.GetRawText()));
                }
                cursor = result.NextCursor;
                if (string.IsNullOrEmpty(cursor)) return new(tools, true);
                if (cursor.Length > 2048 || !seen.Add(cursor)) return new(tools, false);
            }
            return new(tools, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new McpClientException("MCP_UNAVAILABLE"); }
    }
    public async Task<string> CallToolAsync(string accessToken, string name, string arguments, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(15));
            using var backend = Backend(); await using var client = await Connect(accessToken, backend, budget.Token);
            var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments)!;
            var result = await client.CallToolAsync(name, args, cancellationToken: budget.Token);
            if (result.IsError == true) throw new McpClientException("MCP_UNAVAILABLE");
            var output = JsonSerializer.Serialize(result);
            if (System.Text.Encoding.UTF8.GetByteCount(output) > 64 * 1024) throw new McpClientException("RESULT_TOO_LARGE");
            return output;
        }
        catch (McpClientException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new McpClientException("MCP_UNAVAILABLE"); }
    }
    private async Task<T> Read<T>(string accessToken, string name, Dictionary<string, object?> args, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(15));
            using var backend = Backend();
            await using var client = await Connect(accessToken, backend, budget.Token);
            var result = await client.CallToolAsync(name, args, cancellationToken: budget.Token);
            if (result.IsError == true)
            {
                var code = result.Content.OfType<TextContentBlock>().SingleOrDefault()?.Text;
                throw new McpClientException(code is "RESOURCE_UNAVAILABLE" or "RESULT_TOO_LARGE" or "INVALID_ARGUMENT" or "CANCELLED" ? code : "MCP_UNAVAILABLE");
            }
            if (result.StructuredContent is not { } data || data.GetProperty("schemaVersion").GetString() != "1") throw new McpClientException("MCP_UNAVAILABLE");
            return data.Deserialize<T>(Json) ?? throw new McpClientException("MCP_UNAVAILABLE");
        }
        catch (McpClientException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new McpClientException("MCP_UNAVAILABLE"); }
    }
}
