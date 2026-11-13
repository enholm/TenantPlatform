using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

internal sealed record LeasingListScopes(
    IQueryable<LeasingFramework> Frameworks,
    IQueryable<LeasingAcquisition> Acquisitions);

internal sealed class LeasingListQueryRow
{
    public Guid Id { get; init; }
    public bool Framework { get; init; }
    public required string Name { get; init; }
    public required string Number { get; init; }
    public int Status { get; init; }
    public Guid OwnerUserId { get; init; }
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public required string Currency { get; init; }
    public decimal Amount { get; init; }
}

internal static class LeasingListQuery
{
    internal static IQueryable<LeasingFramework> AuthorizedFrameworks(
        TenantPlatformDbContext db, Guid accountId, Guid userId, bool expandedReader) =>
        db.LeasingFrameworks.Where(x => x.AccountId == accountId && (expandedReader || x.OwnerUserId == userId));

    internal static IQueryable<LeasingAcquisition> AuthorizedAcquisitions(
        TenantPlatformDbContext db, Guid accountId, Guid userId, bool admin) =>
        db.LeasingAcquisitions.Where(x => x.AccountId == accountId && (admin || x.OwnerUserId == userId ||
            db.LeasingFrameworks.Any(f => f.AccountId == accountId && f.Id == x.FrameworkId && f.OwnerUserId == userId)));

    internal static LeasingListScopes ApplyKind(LeasingListScopes scopes, string kind)
    {
        var frameworks = scopes.Frameworks;
        var acquisitions = scopes.Acquisitions;
        if (kind == "orders") acquisitions = acquisitions.Where(x => x.FrameworkId == null);
        if (kind is "orders" or "acquisitions") frameworks = frameworks.Where(x => false);
        if (kind == "frameworks") acquisitions = acquisitions.Where(x => false);
        return new(frameworks, acquisitions);
    }

    internal static LeasingListScopes ApplyDimensions(TenantPlatformDbContext db, Guid accountId,
        LeasingListScopes scopes, IReadOnlyCollection<DimensionListFilterCriterion> criteria)
    {
        var frameworks = scopes.Frameworks;
        var acquisitions = scopes.Acquisitions;
        foreach (var criterion in criteria)
        {
            var dimensionId = criterion.DimensionId;
            var valueIds = criterion.MatchingValueIds;
            var unspecified = criterion.IncludeUnspecified;
            frameworks = frameworks.Where(framework =>
                (unspecified && !db.LeasingFrameworkDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.FrameworkId == framework.Id && x.DimensionId == dimensionId)) ||
                db.LeasingFrameworkDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.FrameworkId == framework.Id && x.DimensionId == dimensionId && valueIds.Contains(x.ValueId)));
            acquisitions = acquisitions.Where(acquisition =>
                (unspecified && !db.LeasingAcquisitionDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.AcquisitionId == acquisition.Id && x.DimensionId == dimensionId)) ||
                db.LeasingAcquisitionDimensionAssignments.Any(x => x.AccountId == accountId &&
                    x.AcquisitionId == acquisition.Id && x.DimensionId == dimensionId && valueIds.Contains(x.ValueId)));
        }
        return new(frameworks, acquisitions);
    }

    internal static LeasingListScopes ApplyRecordFilters(LeasingListScopes scopes, LeasingListFilter filter)
    {
        var frameworks = scopes.Frameworks;
        var acquisitions = scopes.Acquisitions;
        if (filter.Owner.HasValue)
        {
            frameworks = frameworks.Where(x => x.OwnerUserId == filter.Owner.Value);
            acquisitions = acquisitions.Where(x => x.OwnerUserId == filter.Owner.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            frameworks = frameworks.Where(x => x.Name.ToLower().Contains(term) || x.Number.ToLower().Contains(term));
            acquisitions = acquisitions.Where(x => x.Name.ToLower().Contains(term) || x.Reference.ToLower().Contains(term));
        }
        if (!string.IsNullOrEmpty(filter.Status))
        {
            frameworks = Enum.TryParse<LeasingFrameworkStatus>(filter.Status, out var frameworkStatus) && Enum.IsDefined(frameworkStatus)
                ? frameworks.Where(x => x.Status == frameworkStatus) : frameworks.Where(x => false);
            acquisitions = Enum.TryParse<LeasingAcquisitionStatus>(filter.Status, out var acquisitionStatus) && Enum.IsDefined(acquisitionStatus)
                ? acquisitions.Where(x => x.Status == acquisitionStatus) : acquisitions.Where(x => false);
        }
        return new(frameworks, acquisitions);
    }

    internal static IQueryable<LeasingListQueryRow> Project(LeasingListScopes scopes)
    {
        var frameworks = scopes.Frameworks.Select(x => new LeasingListQueryRow
        {
            Id = x.Id, Framework = true, Name = x.Name, Number = x.Number, Status = (int)x.Status,
            OwnerUserId = x.OwnerUserId, Start = x.AcquisitionFrom, End = x.AcquisitionTo,
            Currency = x.Currency, Amount = x.LimitNet + x.LimitVat
        });
        var acquisitions = LeasingPurchaseValueQuery.Project(scopes.Acquisitions).Select(x => new LeasingListQueryRow
        {
            Id = x.Acquisition.Id, Framework = false, Name = x.Acquisition.Name,
            Number = x.Acquisition.Reference, Status = (int)x.Acquisition.Status,
            OwnerUserId = x.Acquisition.OwnerUserId, Start = x.Acquisition.PurchaseDate,
            End = x.Acquisition.EndDate, Currency = x.Acquisition.Currency, Amount = x.AdjustedGross
        });
        return frameworks.Concat(acquisitions);
    }

    internal static async Task<DimensionListFilterCatalog> LoadDimensionCatalogAsync(TenantPlatformDbContext db,
        Guid accountId, LeasingListScopes scopes, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.AsNoTracking().Where(x => x.AccountId == accountId)
            .Include(x => x.Translations.Where(t => t.AccountId == accountId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct);
        var enabled = await db.LeasingAgreementDimensionRules.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.IsEnabled).Select(x => x.DimensionId).ToHashSetAsync(ct);
        var frameworkValues = db.LeasingFrameworkDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && scopes.Frameworks.Any(f => f.Id == x.FrameworkId))
            .Select(x => new { x.DimensionId, x.ValueId });
        var acquisitionValues = db.LeasingAcquisitionDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == accountId && scopes.Acquisitions.Any(a => a.Id == x.AcquisitionId))
            .Select(x => new { x.DimensionId, x.ValueId });
        var used = await frameworkValues.Concat(acquisitionValues).Distinct().ToListAsync(ct);
        var language = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => x.DefaultLanguage).SingleAsync(ct);
        return DimensionListFilters.Catalog(dimensions, values, enabled,
            used.Select(x => (x.DimensionId, x.ValueId)).ToHashSet(), CultureInfo.CurrentUICulture.Name, language);
    }

    internal static async Task<List<LeasingOption>> LoadOwnersAsync(
        TenantPlatformDbContext db, LeasingListScopes scopes, CancellationToken ct)
    {
        var frameworkOwners = scopes.Frameworks.Select(x => x.OwnerUserId);
        var acquisitionOwners = scopes.Acquisitions.Select(x => x.OwnerUserId);
        var ids = frameworkOwners.Concat(acquisitionOwners).Distinct();
        return await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.Id)
            .Select(x => new LeasingOption(x.Id, x.FirstName + " " + x.LastName)).ToListAsync(ct);
    }
}
