using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Agreements;

internal sealed class AgreementListQueryRow
{
    public required Agreement Agreement { get; init; }
    public required string Counterparty { get; init; }
    public required string Owner { get; init; }
}

internal static class AgreementListQuery
{
    internal static IQueryable<Agreement> Authorized(TenantPlatformDbContext db, Guid accountId, Guid userId, bool admin) =>
        db.Agreements.Where(x => x.AccountId == accountId &&
            (admin || x.OwnerUserId == userId || db.AgreementAccess.Any(g =>
                g.AccountId == accountId && g.AgreementId == x.Id && g.UserId == userId)));

    internal static IQueryable<Agreement> ApplyAttention(
        IQueryable<Agreement> source, AgreementAttention? attention, DateOnly? today)
    {
        if (!attention.HasValue) return source;
        if (!today.HasValue) throw new ArgumentException("Attention filtering requires the account-local date.", nameof(today));
        return AgreementDashboardQueries.Attention(source, attention.Value, today.Value);
    }

    internal static IQueryable<Agreement> ApplyDimensions(TenantPlatformDbContext db, Guid accountId,
        IQueryable<Agreement> source, IReadOnlyCollection<DimensionListFilterCriterion> criteria)
    {
        foreach (var criterion in criteria)
        {
            var dimensionId = criterion.DimensionId;
            var valueIds = criterion.MatchingValueIds;
            var unspecified = criterion.IncludeUnspecified;
            source = source.Where(agreement =>
                (unspecified && !db.AgreementDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.AgreementId == agreement.Id && x.DimensionId == dimensionId)) ||
                db.AgreementDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.AgreementId == agreement.Id && x.DimensionId == dimensionId && valueIds.Contains(x.ValueId)));
        }
        return source;
    }

    internal static IQueryable<AgreementListQueryRow> Project(
        TenantPlatformDbContext db, Guid accountId, IQueryable<Agreement> source) =>
        from agreement in source
        join organization in db.Organizations on agreement.CounterpartyOrganizationId equals organization.Id
        join owner in db.Users on agreement.OwnerUserId equals owner.Id
        where organization.AccountId == accountId
        select new AgreementListQueryRow
        {
            Agreement = agreement,
            Counterparty = organization.Name,
            Owner = owner.FirstName + " " + owner.LastName
        };

    internal static IQueryable<AgreementListQueryRow> ApplyRecordFilters(
        IQueryable<AgreementListQueryRow> source, AgreementFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Agreement.Title.ToLower().Contains(search) ||
                x.Counterparty.ToLower().Contains(search));
        }
        if (filter.Status.HasValue) source = source.Where(x => x.Agreement.Status == filter.Status.Value);
        if (filter.Type.HasValue) source = source.Where(x => x.Agreement.Type == filter.Type.Value);
        if (filter.OwnerUserId.HasValue) source = source.Where(x => x.Agreement.OwnerUserId == filter.OwnerUserId.Value);
        return source;
    }

    internal static Task<List<AgreementOptionDto>> LoadOwnersAsync(
        TenantPlatformDbContext db, IQueryable<Agreement> source, CancellationToken ct) =>
        (from agreement in source
         join user in db.Users on agreement.OwnerUserId equals user.Id
         select new { user.Id, Name = user.FirstName + " " + user.LastName })
        .Distinct().OrderBy(x => x.Name)
        .Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(ct);

    internal static async Task<DimensionListFilterCatalog> LoadDimensionCatalogAsync(
        TenantPlatformDbContext db, Guid accountId, IQueryable<Agreement> source, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.AsNoTracking().Where(x => x.AccountId == accountId)
            .Include(x => x.Translations.Where(t => t.AccountId == accountId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct);
        var enabled = await db.AgreementDimensionRules.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.IsEnabled).Select(x => x.DimensionId).ToHashSetAsync(ct);
        var used = await db.AgreementDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && source.Any(a => a.Id == x.AgreementId))
            .Select(x => new { x.DimensionId, x.ValueId }).Distinct().ToListAsync(ct);
        var language = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => x.DefaultLanguage).SingleAsync(ct);
        return DimensionListFilters.Catalog(dimensions, values, enabled,
            used.Select(x => (x.DimensionId, x.ValueId)).ToHashSet(), CultureInfo.CurrentUICulture.Name, language);
    }
}
