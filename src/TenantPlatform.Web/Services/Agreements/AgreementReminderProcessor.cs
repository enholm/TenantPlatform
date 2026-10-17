using TenantPlatform.Application.Agreements;
using Microsoft.Extensions.Options;
namespace TenantPlatform.Web.Services.Agreements;
public class AgreementReminderProcessor(AgreementReminderJob job)
{
    public Task RunAccountAsync(Guid accountId, CancellationToken ct = default) => job.RunAccountAsync(accountId, ct);
    public Task PlanAccountAsync(Guid accountId, CancellationToken ct = default) => job.PlanAccountAsync(accountId, ct);
}
public class AgreementReminderWorker(IServiceScopeFactory scopes, IOptions<AgreementReminderOptions> options,
    ILogger<AgreementReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgreementReminderJob>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError("Agreement reminder worker failed with {ErrorType}.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
