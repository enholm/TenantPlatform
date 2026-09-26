using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService
{
    private static IQueryable<LeasingAcquisition> WithClassifications(IQueryable<LeasingAcquisition> query) => query
        .Include(x => x.Items).ThenInclude(x => x.DimensionSelections)
        .Include(x => x.Items).ThenInclude(x => x.AllocationRows)
        .Include(x => x.Items).ThenInclude(x => x.AllocationDimensions).AsSplitQuery();

    private static async Task<LeasingClassificationCatalog> Catalog(TenantPlatformDbContext db, Guid account, bool admin, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
        var rules = await db.LeasingDimensionRules.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.DimensionId, ct);
        return new(dimensions.Select(d => new LeasingDimensionOption(d, rules.GetValueOrDefault(d.Id) ?? new() { AccountId = account, DimensionId = d.Id }, DimensionHierarchy.Build(d, values))).ToList(), admin);
    }
    public async Task<LeasingClassificationCatalog> ClassificationCatalogAsync(Guid account, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        if (!admin && !await Acquisitions(db, account, user, false).AnyAsync(ct) && !await Frameworks(db, account, user, false).AnyAsync(ct)) throw new UnauthorizedAccessException();
        return await Catalog(db, account, admin, ct);
    }
    public async Task SaveDimensionRuleAsync(Guid account, LeasingDimensionRule input, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!admin) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var dim = await db.Dimensions.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == input.DimensionId, ct) ?? throw new UnauthorizedAccessException();
        if (input.AllowAllocation && dim.AllowMultiple || !input.IsEnabled && (input.IsRequired || input.AllowAllocation)) throw new LeasingValidationException("LeasingInvalidDimensionRule");
        var rule = await db.LeasingDimensionRules.SingleOrDefaultAsync(x => x.AccountId == account && x.DimensionId == input.DimensionId, ct);
        var before = rule == null ? "{}" : Snapshot(rule);
        if (rule == null) { rule = new() { AccountId = account, DimensionId = dim.Id }; db.LeasingDimensionRules.Add(rule); }
        else CheckRevision(rule.Revision, input.Revision, true);
        rule.IsEnabled = input.IsEnabled; rule.IsRequired = input.IsRequired; rule.AllowAllocation = input.AllowAllocation; rule.Revision = Guid.NewGuid();
        db.DimensionHistory.Add(new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = dim.Id, ActorUserId = user,
            RecordedUtc = clock.GetUtcNow(), BeforeJson = before, AfterJson = Snapshot(rule) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public static List<string> MissingDimensions(LeasingItem item, LeasingClassificationCatalog catalog) => catalog.Dimensions
        .Where(d => d.Rule.IsEnabled && d.Rule.IsRequired && (item.AllocationDimensions.Any(x => x.DimensionId == d.Dimension.Id)
            ? item.AllocationRows.Count == 0 || item.AllocationRows.Any(r => !item.DimensionSelections.Any(s => s.DimensionId == d.Dimension.Id && s.AllocationRowId == r.Id))
            : !item.DimensionSelections.Any(s => s.DimensionId == d.Dimension.Id && s.AllocationRowId == null)))
        .Select(d => d.Dimension.Name).ToList();

    private static void RemoveClassification(TenantPlatformDbContext db, LeasingItem item)
    {
        db.LeasingDimensionSelections.RemoveRange(item.DimensionSelections);
        db.LeasingAllocationRows.RemoveRange(item.AllocationRows);
        db.LeasingAllocationDimensions.RemoveRange(item.AllocationDimensions);
    }
    private static void ApplyClassification(TenantPlatformDbContext db, LeasingItem item, LeasingClassificationInput input,
        LeasingClassificationCatalog catalog, bool registered)
    {
        void Invalid(string key = "LeasingInvalidClassification") => throw new LeasingValidationException(key);
        if (!Enum.IsDefined(input.Mode) || input.VaryingDimensions.Distinct().Count() != input.VaryingDimensions.Count) Invalid();
        var options = catalog.Dimensions.ToDictionary(x => x.Dimension.Id);
        foreach (var id in input.VaryingDimensions)
            if (!options.TryGetValue(id, out var d) || !d.Dimension.IsActive || !d.Rule.IsEnabled || !d.Rule.AllowAllocation || d.Dimension.AllowMultiple) Invalid("LeasingAllocationDimensionUnavailable");
        if (input.Mode == LeasingAllocationMode.None && (input.Rows.Count != 0 || input.VaryingDimensions.Count != 0)) Invalid();
        if (registered && input.Mode != LeasingAllocationMode.None && input.VaryingDimensions.Count == 0) Invalid("LeasingAllocationChooseDimension");
        var oldRows = item.AllocationRows.ToDictionary(x => x.Id);
        var suppliedIds = input.Rows.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (suppliedIds.Distinct().Count() != suppliedIds.Count || suppliedIds.Any(x => !oldRows.ContainsKey(x))) Invalid();
        if (input.Rows.Any(x => x.InputValue < 0 || x.InputValue > 1000000000000m || !Precision(x.InputValue, input.Mode == LeasingAllocationMode.NetAmount ? 2 : 4))) Invalid("LeasingInvalidAllocation");
        var selections = new List<LeasingDimensionSelection>();
        void Choices(List<LeasingDimensionChoice> choices, Guid? row)
        {
            if (choices.Distinct().Count() != choices.Count) Invalid();
            foreach (var group in choices.GroupBy(x => x.DimensionId))
            {
                if (!options.TryGetValue(group.Key, out var d)) { Invalid(); return; }
                if (!d.Rule.IsEnabled || !d.Dimension.IsActive || (!d.Dimension.AllowMultiple && group.Count() > 1) ||
                    (row.HasValue != input.VaryingDimensions.Contains(group.Key))) Invalid();
                foreach (var choice in group)
                {
                    var value = d.Values.SingleOrDefault(x => x.Id == choice.ValueId);
                    if (value == null || !value.Selectable) { Invalid(); return; }
                    selections.Add(new() { Id = Guid.NewGuid(), AccountId = item.AccountId, ItemId = item.Id, AllocationRowId = row,
                        DimensionId = d.Dimension.Id, ValueId = value.Id, DimensionName = d.Dimension.Name, DimensionCode = d.Dimension.Code,
                        ValueName = value.Name, ValueCode = value.Code, ValuePath = value.Path });
                }
            }
        }
        Choices(input.Common, null);
        var rows = new List<LeasingAllocationRow>();
        foreach (var (row, position) in input.Rows.Select((r, i) => (r, i)))
        {
            var target = row.Id.HasValue ? oldRows[row.Id.Value] : new LeasingAllocationRow { Id = Guid.NewGuid(), AccountId = item.AccountId, ItemId = item.Id };
            target.Position = position; target.InputValue = row.InputValue; rows.Add(target); Choices(row.Choices, target.Id);
            if (registered && input.VaryingDimensions.Any(d => !row.Choices.Any(c => c.DimensionId == d))) Invalid("LeasingAllocationChooseRowValues");
        }
        db.LeasingDimensionSelections.RemoveRange(item.DimensionSelections);
        db.LeasingAllocationRows.RemoveRange(item.AllocationRows.Where(x => !rows.Contains(x)));
        db.LeasingAllocationDimensions.RemoveRange(item.AllocationDimensions.Where(x => !input.VaryingDimensions.Contains(x.DimensionId)));
        var dimensions = input.VaryingDimensions.Select(id => item.AllocationDimensions.SingleOrDefault(x => x.DimensionId == id) ??
            new LeasingAllocationDimension { AccountId = item.AccountId, ItemId = item.Id, DimensionId = id }).ToList();
        foreach (var dim in dimensions) { dim.DimensionName = options[dim.DimensionId].Dimension.Name; dim.DimensionCode = options[dim.DimensionId].Dimension.Code; }
        item.AllocationMode = input.Mode; item.AllocationDimensions = dimensions; item.AllocationRows = rows; item.DimensionSelections = selections;
        // Explicit states also work for newly generated IDs on already tracked acquisitions.
        foreach (var value in selections) db.Entry(value).State = EntityState.Added;
        foreach (var row in rows.Where(x => !oldRows.ContainsKey(x.Id))) db.Entry(row).State = EntityState.Added;
        foreach (var dim in dimensions.Where(x => db.Entry(x).State == EntityState.Detached)) db.Entry(dim).State = EntityState.Added;
        if (registered && MissingDimensions(item, catalog).Count != 0) Invalid("LeasingMissingDimensions");
    }
}
