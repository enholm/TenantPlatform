using TenantPlatform.Application.Agreements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

// Compatibility composition for the dedicated cleanup job and post-mutation maintenance.
// The Application entry only removes expired/discarded drafts or pending deletions.
public class AgreementAnalysisCleanup(IDbContextFactory<TenantPlatformDbContext> factory, IAgreementDocumentStorage storage, TimeProvider clock)
{
    private AgreementAnalysisCleanupService Compose(string? root = null) => new(new AgreementAnalysisCleanupData(factory, root), storage, clock,
        new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.AgreementAnalysisCleanup));
    public Task CleanDraftAsync(Guid accountId, Guid draftId, CancellationToken ct) => Compose().CleanDraftAsync(accountId, draftId, ct);
    public Task RunAsync(CancellationToken ct) => Compose().RunAsync(ct);
    public Task CleanOrphansAsync(string rootPath, CancellationToken ct) => Compose(rootPath).CleanOrphansAsync(ct);
}
public class AgreementAnalysisCleanupWorker(IServiceScopeFactory scopes, IOptions<AgreementDocumentStorageOptions> options,
    ILogger<AgreementAnalysisCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<AgreementAnalysisCleanup>();
                await cleanup.RunAsync(stoppingToken);
                await cleanup.CleanOrphansAsync(options.Value.RootPath, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Agreement analysis cleanup failed; will retry."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
