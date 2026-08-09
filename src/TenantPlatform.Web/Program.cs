using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using TenantPlatform.Web.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Initialization;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<TenantPlatformDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();



builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Initialize database and add platform data (and also demo data if dev environment)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<TenantPlatformDbContext>();

    var logger = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();

    await DatabaseInitializer.InitializeAsync(
        dbContext,
        logger,
        includeDemoData: app.Environment.IsDevelopment());
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapStaticAssets();
app.UseStaticFiles();
app.UseAntiforgery();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();