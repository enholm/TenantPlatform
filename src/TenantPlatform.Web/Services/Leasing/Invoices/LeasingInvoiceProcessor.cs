using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
namespace TenantPlatform.Web.Services.Leasing.Invoices;
public sealed class LeasingInvoiceProcessor(LeasingInvoiceJob job)
{
    public Task<bool> ProcessOneAsync(CancellationToken ct = default) => job.ProcessOneAsync(ct);
}
public sealed class LeasingInvoiceWorker(IServiceScopeFactory scopes, ILogger<LeasingInvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<LeasingInvoiceProcessor>().ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Invoice worker failed ({ErrorType}); retrying", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
