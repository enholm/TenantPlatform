using System.Threading.RateLimiting;

namespace TenantPlatform.Mcp;

// Single-instance pilot limits; before scaling, coordinate quotas between replicas.
public sealed class McpBoundary(RequestDelegate next) : IDisposable
{
    private readonly PartitionedRateLimiter<string> concurrent = PartitionedRateLimiter.Create<string, string>(user =>
        RateLimitPartition.GetConcurrencyLimiter(user, _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }));
    public void Dispose() => concurrent.Dispose();

    public async Task InvokeAsync(HttpContext http, McpOptions options)
    {
        if (http.Request.Path != "/mcp") { await next(http); return; }
        // Unsupported HTTP verbs have no SDK endpoint/policy metadata in stateless mode.
        // They must still fail safely before accessing the per-user limiter.
        if (http.User.Identity?.IsAuthenticated != true || !Guid.TryParseExact(http.User.FindFirst("sub")?.Value, "D", out var user))
        { http.Response.StatusCode = 401; return; }
        if (http.Request.Headers.TryGetValue("Origin", out var origin) &&
            (origin.Count != 1 || !options.AllowedOrigins.Contains(origin.ToString(), StringComparer.Ordinal)))
        { http.Response.StatusCode = 403; return; }
        using var lease = concurrent.AttemptAcquire(user.ToString("D"));
        if (!lease.IsAcquired) { http.Response.StatusCode = 429; return; }
        var requestToken = http.RequestAborted;
        var body = http.Request.Body;
        var response = http.Response.Body;
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        try
        {
            var buffer = new byte[4096];
            int read;
            while ((read = await body.ReadAsync(buffer, requestToken)) > 0)
            {
                if (input.Length + read > 16 * 1024) { http.Response.StatusCode = 413; await http.Response.WriteAsync("REQUEST_TOO_LARGE", requestToken); return; }
                await input.WriteAsync(buffer.AsMemory(0, read), requestToken);
            }
            input.Position = 0;
            http.Request.Body = input;
            http.Response.Body = output;
            await next(http);
            // Measure the whole SDK envelope, including SSE framing/JSON-RPC ID. Never send partial data.
            http.Response.Body = response;
            if (output.Length > 64 * 1024)
            {
                http.Response.StatusCode = 413; http.Response.ContentType = "text/plain"; http.Response.ContentLength = null;
                await http.Response.WriteAsync("RESULT_TOO_LARGE", requestToken); return;
            }
            output.Position = 0;
            await output.CopyToAsync(response, requestToken);
        }
        catch (Exception) when (!requestToken.IsCancellationRequested)
        { if (!http.Response.HasStarted) { http.Response.StatusCode = 503; http.Response.ContentLength = 0; } }
        finally { http.Request.Body = body; http.Response.Body = response; }
    }
}
