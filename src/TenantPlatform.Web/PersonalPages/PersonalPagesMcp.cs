using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.PersonalPages;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.OAuth;
namespace TenantPlatform.Web.PersonalPages;

public sealed class PersonalPagesAdminOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
}
public static class PersonalPagesMcp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private static readonly string[] Names = ["personal_pages_list", "personal_pages_read", "personal_pages_save", "personal_pages_delete", "personal_pages_move"];
    public static void AddPersonalPagesMcp(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("PersonalPages:Administration").Get<PersonalPagesAdminOptions>() ?? new(); services.AddSingleton(options);
        if (!options.Enabled) return;
        if (string.IsNullOrWhiteSpace(options.ConnectionString) || services.LastOrDefault(d => d.ServiceType == typeof(TenantPlatform.Web.Security.McpClient.InternalMcpClientOptions))?.ImplementationInstance is not TenantPlatform.Web.Security.McpClient.InternalMcpClientOptions { Available: true }) throw new InvalidOperationException("Personal pages MCP requires OAuth/internal client and a restricted pages connection.");
        services.AddOpenIddict().AddValidation(v => v.UseAspNetCore().DisableAccessTokenExtractionFromBodyForm().DisableAccessTokenExtractionFromQueryString());
        services.AddAuthorization(o => o.AddPolicy("PersonalPagesMcp", p => { p.AuthenticationSchemes.Add(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme); p.RequireAuthenticatedUser();
            p.RequireAssertion(c => c.User.HasScope(PersonalPageScopes.Manage) && c.User.HasScope(OAuthIssuerOptions.ReadScope) && c.User.GetAuthorizationId() is not null); }));
        services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("PersonalPagesAdmin", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.User.GetClaim("sub") ?? "invalid", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
        services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = Names.Select(n => new Tool { Name = n,
                Description = "Explicit personal page administration. Never AutomaticRead or automatic agent use. All operations require current user/account access and mutation revision. Save accepts a version 1 declaration, not executable content.",
                InputSchema = Input(n),
                Annotations = new() { ReadOnlyHint = n.EndsWith("list") || n.EndsWith("read"), DestructiveHint = n.EndsWith("delete"), OpenWorldHint = false } }).ToList() }))
            .WithCallToolHandler(Call);
    }
    public static void MapPersonalPagesMcp(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<PersonalPagesAdminOptions>().Enabled) return;
        app.Use(async (http, next) =>
        {
            if (!http.Request.Path.StartsWithSegments("/mcp/pages")) { await next(http); return; }
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.Headers.ContainsKey("Origin")) { http.Response.StatusCode = 403; return; }
            if (http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit) bodyLimit.MaxRequestBodySize = 32768;
            if (http.Request.ContentLength > 32768) { http.Response.StatusCode = 413; return; }
            var originalRequestToken = http.RequestAborted;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted); budget.CancelAfter(TimeSpan.FromSeconds(15)); http.RequestAborted = budget.Token;
            var requestBody = http.Request.Body;
            var requestToken = http.RequestAborted;
            await using var input = new MemoryStream();
            var chunk = new byte[4096]; int read;
            while ((read = await requestBody.ReadAsync(chunk, budget.Token)) > 0)
            {
                if (input.Length + read > 32768) { http.Response.StatusCode = 413; return; }
                await input.WriteAsync(chunk.AsMemory(0, read), budget.Token);
            }
            input.Position = 0; http.Request.Body = input;
            var original = http.Response.Body;
            await using var buffer = new MemoryStream(); http.Response.Body = buffer;
            try
            {
                await next(http);
                if (buffer.Length > 131072) { http.Response.Clear(); http.Response.StatusCode = 413; }
                else { buffer.Position = 0; await buffer.CopyToAsync(original, budget.Token); }
            }
            finally { http.Response.Body = original; http.Request.Body = requestBody; http.RequestAborted = originalRequestToken; }
        });
        app.UseRateLimiter();
        var oauth = app.Services.GetRequiredService<OAuthIssuerOptions>();
        app.MapGet("/.well-known/oauth-protected-resource/mcp/pages", () => Results.Json(new { resource = oauth.Resource, authorization_servers = new[] { oauth.Issuer }, scopes_supported = new[] { OAuthIssuerOptions.ReadScope, PersonalPageScopes.Manage }, bearer_methods_supported = new[] { "header" } })).AllowAnonymous();
        app.MapMcp("/mcp/pages").RequireAuthorization("PersonalPagesMcp").RequireRateLimiting("PersonalPagesAdmin");
    }
    public static JsonElement Input(string name)
    {
        var properties = new JsonObject { ["accountId"] = JsonNode.Parse("{\"type\":\"string\",\"format\":\"uuid\"}") };
        var required = new JsonArray("accountId");
        if (name != "personal_pages_list") { properties["id"] = JsonNode.Parse("{\"type\":\"string\",\"format\":\"uuid\"}"); if (name != "personal_pages_save") required.Add("id"); }
        if (name is "personal_pages_save" or "personal_pages_delete" or "personal_pages_move") { properties["revision"] = JsonNode.Parse("{\"type\":\"string\",\"format\":\"uuid\"}"); required.Add("revision"); }
        if (name == "personal_pages_save") { properties["definition"] = Json.GetJsonSchemaAsNode(typeof(PageDefinition)); required.Add("definition"); }
        if (name == "personal_pages_move") { properties["move"] = JsonNode.Parse("{\"type\":\"integer\",\"enum\":[-1,1]}"); required.Add("move"); }
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties });
    }
    private static async ValueTask<CallToolResult> Call(RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        try
        {
            var p = context.Params ?? throw new ArgumentException();
            var args = p.Arguments ?? throw new ArgumentException();
            string[] allowed = p.Name switch { "personal_pages_list" => ["accountId"], "personal_pages_read" => ["accountId", "id"], "personal_pages_save" => ["accountId", "id", "revision", "definition"], "personal_pages_delete" => ["accountId", "id", "revision"], "personal_pages_move" => ["accountId", "id", "revision", "move"], _ => throw new ArgumentException() };
            if (args.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException();
            if (context.User?.Identity?.IsAuthenticated != true || !context.User.HasScope(PersonalPageScopes.Manage) || !Guid.TryParse(context.User.GetClaim("sub"), out var user) || user == Guid.Empty) throw new UnauthorizedAccessException();
            Guid Id(string name) => args.TryGetValue(name, out var val) && val.ValueKind == JsonValueKind.String && Guid.TryParseExact(val.GetString(), "D", out var id) ? id : throw new ArgumentException();
            var account = Id("accountId"); if (account == Guid.Empty) throw new ArgumentException();
            await using var operation = context.Services!.CreateAsyncScope();
            var sp = operation.ServiceProvider;
            using var lease = sp.GetRequiredService<PersonalPageRuns>().Start(new(true, user, account), ct, out var deadline);
            ct = deadline;
            await TenantPlatform.Infrastructure.PersonalPages.PersonalPagesPrivileges.VerifyAsync(sp.GetRequiredService<PersonalPagesAdminOptions>().ConnectionString, ct);
            var store = new PersonalPageData(new PagesFactory(sp.GetRequiredService<PersonalPagesAdminOptions>().ConnectionString));
            var service = PersonalPagesComposition.Create(sp, new FixedActor(new(true, user, account)), store);
            object result = p.Name switch {
                "personal_pages_list" => await service.ListAsync(ct),
                "personal_pages_read" => await service.ReadAsync(Id("id"), ct),
                "personal_pages_save" => await service.SaveAsync(args.ContainsKey("id") ? Id("id") : null, Id("revision"), args["definition"].Deserialize<PageDefinition>(Json) ?? throw new ArgumentException(), ct),
                "personal_pages_delete" => await service.ChangeAsync(Id("id"), Id("revision"), null, ct),
                "personal_pages_move" => await service.ChangeAsync(Id("id"), Id("revision"), args["move"].GetInt32(), ct), _ => throw new ArgumentException() };
            if (result is PersonalPageCollection collection) result = new { collection.Revision, Pages = collection.Pages.Select(p => new { p.Id, p.Definition.Title }).ToArray() };
            var structured = JsonSerializer.SerializeToElement(result, Json);
            if (System.Text.Encoding.UTF8.GetByteCount(structured.GetRawText()) > 60000) return Error("LIMIT");
            return new() { StructuredContent = structured, Content = [new TextContentBlock { Text = "Page administration completed." }] };
        }
        catch (UnauthorizedAccessException) { return Error("RESOURCE_UNAVAILABLE"); }
        catch (PersonalPageException ex) { return Error(ex.Code); }
        catch (OperationCanceledException) { return Error("CANCELLED"); }
        catch (Exception) { return Error("INVALID_OR_UNAVAILABLE"); }
    }
    private static CallToolResult Error(string code) => new() { IsError = true, Content = [new TextContentBlock { Text = code }] };
    sealed class FixedActor(ActorSnapshot value) : ICurrentActor { public ActorSnapshot GetSnapshot() => value; }
    sealed class PagesFactory(string connection) : IDbContextFactory<TenantPlatformDbContext>
    {
        public TenantPlatformDbContext CreateDbContext() => new(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(connection, o => o.CommandTimeout(8)).Options);
        public Task<TenantPlatformDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}
