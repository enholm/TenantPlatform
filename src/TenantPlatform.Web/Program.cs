using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using TenantPlatform.Web.Components;
using Microsoft.AspNetCore.Builder;

using Microsoft.EntityFrameworkCore;

using TenantPlatform.Infrastructure.Persistence;


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