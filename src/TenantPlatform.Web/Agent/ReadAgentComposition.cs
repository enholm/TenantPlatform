using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Web.Security.McpClient;

namespace TenantPlatform.Web.Agent;

public static class ReadAgentComposition
{
    public static void AddReadAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("ReadAgent").Get<ReadAgentOptions>() ?? new();
        services.Configure<ReadAgentOptions>(configuration.GetSection("ReadAgent"));
        services.AddSingleton(options.Limits);
        services.AddSingleton<ReadAgentSessions>();
        services.AddScoped<AgentReportDrafts>(sp => new(sp.GetRequiredService<TenantPlatform.Application.PersonalPages.PersonalPageService>(), sp.GetRequiredService<TenantPlatform.Application.PersonalPages.PersonalPageRunner>(), sp.GetRequiredService<McpRegistryService>()));
        services.AddSingleton<IAgentSchemaValidator, AgentSchemaValidator>();
        services.AddHttpClient<IReadAgentModel, OpenAiReadAgentModel>(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
            .RemoveAllLoggers();
        services.AddScoped<ReadAgentService>(sp =>
        {
            var actor = new RegistryCookieActor(sp.GetRequiredService<IHttpContextAccessor>());
            return new(actor, new AuthorizationService(actor, sp.GetRequiredService<IAuthorizationData>(), sp.GetRequiredService<TimeProvider>()),
                sp.GetRequiredService<McpRegistryService>(), sp.GetRequiredService<IReadAgentModel>(),
                sp.GetRequiredService<IAgentSchemaValidator>(), sp.GetRequiredService<ReadAgentSessions>(), options.Limits, sp.GetRequiredService<AgentReportDrafts>());
        });
    }
}
