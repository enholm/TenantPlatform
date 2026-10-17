using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace TenantPlatform.Web.Services.Leasing;
// Compatibility composition; all bounded system authority and work rules are in Application.
public sealed class LeasingLifecycleProcessor(IDbContextFactory<TenantPlatformDbContext> factory, TimeProvider clock)
{
    public Task RunAccountAsync(Guid account, CancellationToken ct = default) => new LeasingLifecycleJob(new LeasingLifecycleJobData(factory), clock,
        new HostSystemWorkContext(SystemWorkKind.LeasingLifecycle)).RunAccountAsync(account, ct);
}
public sealed class LeasingLifecycleWorker(IServiceScopeFactory scopes,ILogger<LeasingLifecycleWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<LeasingLifecycleJob>().RunAsync(stoppingToken); }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Leasing follow-up worker failed");}
            await Task.Delay(TimeSpan.FromMinutes(5),stoppingToken);
        }
    }
}
