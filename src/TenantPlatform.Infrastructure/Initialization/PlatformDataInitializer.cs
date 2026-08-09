using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Initialization;

public static class PlatformDataInitializer
{
    public static Task InitializeAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Foreløpig har domenemodellen ingen globale platform-data
        // som skal opprettes uavhengig av en Account.
        //
        // Senere kan dette for eksempel være:
        // - globale språk
        // - systemkonfigurasjon
        // - globale tjenestemaler
        // - andre faste plattformdata

        return Task.CompletedTask;
    }
}