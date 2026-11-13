using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Reports;

public sealed record ReportLocalDate(DateOnly Today, string TimeZoneId);

public interface IReportLocalDateService
{
    Task<ReportLocalDate> GetAsync(Guid accountId, CancellationToken ct = default);
}

public sealed class ReportLocalDateService(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    TimeProvider clock) : IReportLocalDateService
{
    public async Task<ReportLocalDate> GetAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !await db.UserAccounts.AsNoTracking().AnyAsync(x => x.AccountId == accountId &&
                x.UserId == current.UserId && x.User.IsActive, ct))
            throw new UnauthorizedAccessException();
        var fallback = new AgreementReminderSettings().TimeZoneId;
        var timeZoneId = await db.AgreementReminderSettings.AsNoTracking()
            .Where(x => x.AccountId == accountId).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? fallback;
        return new(AgreementReminderSchedule.Today(clock.GetUtcNow(), timeZoneId), timeZoneId);
    }
}
