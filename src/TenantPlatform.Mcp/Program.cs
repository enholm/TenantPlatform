using TenantPlatform.Mcp;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddTenantMcp(builder.Configuration.GetSection("Mcp").Get<McpOptions>() ?? new());
var app = builder.Build();
await RuntimeDatabaseCheck.VerifyAsync(app.Services.GetRequiredService<McpOptions>());
app.MapTenantMcp();
app.Run();
