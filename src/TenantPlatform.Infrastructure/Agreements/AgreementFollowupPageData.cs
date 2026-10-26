using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementFollowupPageData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementFollowupPageData
{
    public async Task<StoredDeadlinePage> ReadPageAsync(AgreementReadScope scope, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.ListScope.AccountId;
        var agreement = await AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId)
            .Select(a => new { a.IsArchived, a.DeadlinesInitialized }).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        var zone = await db.AgreementReminderSettings.AsNoTracking().Where(s => s.AccountId == account)
            .Select(s => s.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo";
        var query = db.AgreementDeadlines.AsNoTracking().Where(d => d.AccountId == account && d.AgreementId == scope.AgreementId);
        var total = await query.CountAsync(ct);
        var actualPage = Math.Min(page, Math.Max(1, (int)(((long)total + AgreementFollowupPageReader.PageSize - 1) / AgreementFollowupPageReader.PageSize)));
        var rows = await query.OrderBy(d => d.State).ThenBy(d => d.DueDate).ThenBy(d => d.Id)
            .Skip((actualPage - 1) * AgreementFollowupPageReader.PageSize).Take(AgreementFollowupPageReader.PageSize)
            .Select(d => new StoredDeadline(d.Id, d.Kind, d.DueDate, d.Status, d.State, d.UpdatedUtc)).ToArrayAsync(ct);
        return new(agreement.DeadlinesInitialized, agreement.IsArchived, zone, actualPage, total, rows);
    }
}
