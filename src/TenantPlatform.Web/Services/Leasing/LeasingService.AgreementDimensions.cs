using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

public sealed class LeasingAgreementDimensionPatch
{
    public Guid DimensionId { get; set; }
    public List<Guid>? ValueIds { get; set; }
}
public sealed record LeasingAgreementDimensionValue(Guid Id, Guid? ParentId, string Name, string Code, string Path,
    int Depth, bool Selectable, bool IsAssigned)
{
    public bool IsHistorical => IsAssigned && !Selectable;
}
public sealed record LeasingAgreementDimensionOption(Guid DimensionId, string Code, string Name, string? Description,
    bool IsRequired, bool AllowMultiple, bool LeafOnly, List<LeasingAgreementDimensionValue> Values);
public sealed record LeasingAgreementDimensionOptions(bool ReadOnly, bool Inherited, List<LeasingAgreementDimensionOption> Dimensions);

public sealed partial class LeasingService
{
    private sealed record LeasingAgreementCatalog(List<Dimension> Dimensions, List<DimensionValue> Values,
        Dictionary<Guid, LeasingAgreementDimensionRule> Rules, string? DefaultLanguage);
    private sealed record AgreementSelection(Guid DimensionId, Guid ValueId, string DimensionName, string DimensionCode,
        string ValueName, string ValueCode, string ValuePath);

    private static async Task<LeasingAgreementCatalog> AgreementCatalog(TenantPlatformDbContext db, Guid account, CancellationToken ct) => new(
        await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account)
            .Include(x => x.Translations.Where(t => t.AccountId == account)).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct),
        await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
        await db.LeasingAgreementDimensionRules.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.DimensionId, ct),
        await db.Accounts.AsNoTracking().Where(x => x.Id == account).Select(x => x.DefaultLanguage).SingleAsync(ct));

    public async Task<LeasingAgreementDimensionOptions> AgreementDimensionOptionsAsync(Guid account, Guid? frameworkId = null,
        Guid? acquisitionId = null, Guid? inheritedFrameworkId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        if (new[] { frameworkId, acquisitionId, inheritedFrameworkId }.Count(x => x.HasValue) > 1) throw new UnauthorizedAccessException();
        List<AgreementSelection> assigned;
        var readOnly = false; var inherited = false;
        if (frameworkId.HasValue)
        {
            var frame = await Frameworks(db, account, user, admin).AsNoTracking().Include(x => x.DimensionAssignments)
                .SingleOrDefaultAsync(x => x.Id == frameworkId, ct) ?? throw new UnauthorizedAccessException();
            assigned = frame.DimensionAssignments.Select(Selection).ToList();
        }
        else if (acquisitionId.HasValue)
        {
            var acquisition = await Acquisitions(db, account, user, admin).AsNoTracking().Include(x => x.DimensionAssignments)
                .SingleOrDefaultAsync(x => x.Id == acquisitionId, ct) ?? throw new UnauthorizedAccessException();
            assigned = acquisition.DimensionAssignments.Select(Selection).ToList();
            readOnly = inherited = acquisition.FrameworkId.HasValue;
        }
        else if (inheritedFrameworkId.HasValue)
        {
            var frame = await Frameworks(db, account, user, admin).AsNoTracking().Include(x => x.DimensionAssignments)
                .SingleOrDefaultAsync(x => x.Id == inheritedFrameworkId, ct) ?? throw new UnauthorizedAccessException();
            assigned = frame.DimensionAssignments.Select(Selection).ToList(); readOnly = inherited = true;
        }
        else
        {
            if (!admin) throw new UnauthorizedAccessException();
            assigned = [];
        }
        return await Options(db, account, assigned, readOnly, inherited, ct);
    }

    private static async Task<LeasingAgreementDimensionOptions> Options(TenantPlatformDbContext db, Guid account,
        List<AgreementSelection> assigned, bool readOnly, bool inherited, CancellationToken ct)
    {
        var catalog = await AgreementCatalog(db, account, ct);
        var options = new List<LeasingAgreementDimensionOption>();
        foreach (var dimension in catalog.Dimensions)
        {
            var selected = assigned.Where(x => x.DimensionId == dimension.Id).Select(x => x.ValueId).ToHashSet();
            var rule = catalog.Rules.GetValueOrDefault(dimension.Id);
            var available = dimension.IsActive && rule?.IsEnabled == true;
            if (!available && selected.Count == 0) continue;
            var paths = DimensionHierarchy.Build(dimension, catalog.Values);
            options.Add(new(dimension.Id, dimension.Code,
                DimensionLocalization.Name(dimension, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                DimensionLocalization.Description(dimension, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                rule?.IsRequired == true, dimension.AllowMultiple, dimension.LeafOnly,
                paths.Where(x => available && x.Selectable || selected.Contains(x.Id))
                    .Select(x => new LeasingAgreementDimensionValue(x.Id, x.ParentId, x.Name, x.Code, x.Path, x.Depth,
                        available && x.Selectable, selected.Contains(x.Id))).ToList()));
        }
        return new(readOnly, inherited, options);
    }

    public async Task<List<LeasingAgreementDimensionRule>> GetAgreementDimensionRulesAsync(Guid account, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (_, admin) = await Member(db, account, ct);
        if (!admin) throw new UnauthorizedAccessException();
        var dimensions = await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
        var rules = await db.LeasingAgreementDimensionRules.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.DimensionId, ct);
        return dimensions.Select(x => rules.GetValueOrDefault(x.Id) ?? new LeasingAgreementDimensionRule { AccountId = account, DimensionId = x.Id }).ToList();
    }

    public async Task SaveAgreementDimensionRuleAsync(Guid account, LeasingAgreementDimensionRule input, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!admin || input.AccountId != Guid.Empty && input.AccountId != account) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        if (!await db.Dimensions.AnyAsync(x => x.AccountId == account && x.Id == input.DimensionId, ct)) throw new UnauthorizedAccessException();
        var rule = await db.LeasingAgreementDimensionRules.SingleOrDefaultAsync(x => x.AccountId == account && x.DimensionId == input.DimensionId, ct);
        if ((rule?.Revision ?? Guid.Empty) != input.Revision) throw new LeasingValidationException("LeasingConcurrency");
        var before = rule is null ? "{}" : Snapshot(rule);
        if (rule is null) { rule = new() { AccountId = account, DimensionId = input.DimensionId }; db.LeasingAgreementDimensionRules.Add(rule); }
        rule.IsEnabled = input.IsEnabled; rule.IsRequired = input.IsEnabled && input.IsRequired; rule.Revision = Guid.NewGuid();
        db.DimensionHistory.Add(new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = input.DimensionId, ActorUserId = user,
            RecordedUtc = clock.GetUtcNow(), BeforeJson = before, AfterJson = Snapshot(rule) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private static AgreementSelection Selection(LeasingFrameworkDimensionAssignment x) => new(x.DimensionId, x.ValueId,
        x.DimensionName, x.DimensionCode, x.ValueName, x.ValueCode, x.ValuePath);
    private static AgreementSelection Selection(LeasingAcquisitionDimensionAssignment x) => new(x.DimensionId, x.ValueId,
        x.DimensionName, x.DimensionCode, x.ValueName, x.ValueCode, x.ValuePath);

    private static async Task<List<AgreementSelection>?> PlanAgreementDimensions(TenantPlatformDbContext db, Guid account,
        List<AgreementSelection> existing, List<LeasingAgreementDimensionPatch>? changes, bool creating, CancellationToken ct)
    {
        if (!creating && changes is null) return null;
        var patches = new List<LeasingAgreementDimensionPatch>(); var seen = new HashSet<Guid>();
        foreach (var patch in changes ?? [])
        {
            if (patch is null || patch.DimensionId == Guid.Empty || patch.ValueIds is null || patch.ValueIds.Contains(Guid.Empty))
                throw new LeasingValidationException("LeasingAgreementDimensionInvalid");
            if (!seen.Add(patch.DimensionId) || patch.ValueIds.Distinct().Count() != patch.ValueIds.Count)
                throw new LeasingValidationException("LeasingAgreementDimensionInvalid");
            patches.Add(new() { DimensionId = patch.DimensionId, ValueIds = [.. patch.ValueIds] });
        }
        var catalog = await AgreementCatalog(db, account, ct);
        var desired = existing.ToList();
        foreach (var patch in patches)
        {
            var dimension = catalog.Dimensions.SingleOrDefault(x => x.Id == patch.DimensionId)
                ?? throw new LeasingValidationException("LeasingAgreementDimensionInvalid");
            var rule = catalog.Rules.GetValueOrDefault(dimension.Id); var ids = patch.ValueIds!;
            if (rule?.IsRequired == true && ids.Count == 0) throw new LeasingValidationException("LeasingAgreementDimensionRequired");
            if (!dimension.AllowMultiple && ids.Count > 1) throw new LeasingValidationException("LeasingAgreementDimensionSingle");
            var old = existing.Where(x => x.DimensionId == dimension.Id).Select(x => x.ValueId).ToHashSet();
            var additions = ids.Except(old).ToList();
            if (additions.Count > 0 && (rule?.IsEnabled != true || !dimension.IsActive)) throw new LeasingValidationException("LeasingAgreementDimensionUnavailable");
            var paths = DimensionHierarchy.Build(dimension, catalog.Values);
            desired.RemoveAll(x => x.DimensionId == dimension.Id);
            foreach (var id in ids)
            {
                var value = paths.SingleOrDefault(x => x.Id == id) ?? throw new LeasingValidationException("LeasingAgreementDimensionInvalid");
                if (additions.Contains(id) && (!value.IsActive || !value.Selectable)) throw new LeasingValidationException("LeasingAgreementDimensionUnavailable");
                desired.Add(new(dimension.Id, id, dimension.Name, dimension.Code, value.Name, value.Code, value.Path));
            }
        }
        if (creating)
        {
            foreach (var rule in catalog.Rules.Values.Where(x => x.IsEnabled && x.IsRequired))
            {
                var dimension = catalog.Dimensions.SingleOrDefault(x => x.Id == rule.DimensionId);
                if (dimension is null || !dimension.IsActive || !DimensionHierarchy.Build(dimension, catalog.Values).Any(x => x.Selectable))
                    throw new LeasingValidationException("LeasingAgreementDimensionConfiguration");
                if (!desired.Any(x => x.DimensionId == rule.DimensionId)) throw new LeasingValidationException("LeasingAgreementDimensionRequired");
            }
        }
        return Same(existing, desired) ? null : desired;
    }

    private static bool Same(List<AgreementSelection> left, List<AgreementSelection> right) =>
        left.Select(x => (x.DimensionId, x.ValueId)).OrderBy(x => x).SequenceEqual(right.Select(x => (x.DimensionId, x.ValueId)).OrderBy(x => x));

    private static List<AgreementSelection>? InheritedPlan(Guid? previousFrameworkId, Guid? nextFrameworkId, bool creating,
        List<LeasingAgreementDimensionPatch>? suppliedChanges, List<AgreementSelection> frameworkSelections)
    {
        if (!nextFrameworkId.HasValue) return null;
        if (suppliedChanges is not null) throw new LeasingValidationException("LeasingAgreementDimensionsInherited");
        return creating || previousFrameworkId != nextFrameworkId ? frameworkSelections.ToList() : null;
    }

    private static void Replace(TenantPlatformDbContext db, LeasingFramework framework, List<AgreementSelection> desired)
    {
        db.LeasingFrameworkDimensionAssignments.RemoveRange(framework.DimensionAssignments);
        framework.DimensionAssignments = desired.Select(x => new LeasingFrameworkDimensionAssignment { Id = Guid.NewGuid(), AccountId = framework.AccountId,
            FrameworkId = framework.Id, DimensionId = x.DimensionId, ValueId = x.ValueId, DimensionName = x.DimensionName,
            DimensionCode = x.DimensionCode, ValueName = x.ValueName, ValueCode = x.ValueCode, ValuePath = x.ValuePath }).ToList();
        db.LeasingFrameworkDimensionAssignments.AddRange(framework.DimensionAssignments);
    }
    private static void Replace(TenantPlatformDbContext db, LeasingAcquisition acquisition, List<AgreementSelection> desired)
    {
        db.LeasingAcquisitionDimensionAssignments.RemoveRange(acquisition.DimensionAssignments);
        acquisition.DimensionAssignments = desired.Select(x => new LeasingAcquisitionDimensionAssignment { Id = Guid.NewGuid(), AccountId = acquisition.AccountId,
            AcquisitionId = acquisition.Id, DimensionId = x.DimensionId, ValueId = x.ValueId, DimensionName = x.DimensionName,
            DimensionCode = x.DimensionCode, ValueName = x.ValueName, ValueCode = x.ValueCode, ValuePath = x.ValuePath }).ToList();
        db.LeasingAcquisitionDimensionAssignments.AddRange(acquisition.DimensionAssignments);
    }

    private async Task PropagateFrameworkDimensions(TenantPlatformDbContext db, LeasingFramework framework, Guid actor, string reason, CancellationToken ct)
    {
        var desired = framework.DimensionAssignments.Select(Selection).ToList();
        var children = await db.LeasingAcquisitions.Where(x => x.AccountId == framework.AccountId && x.FrameworkId == framework.Id)
            .Include(x => x.Items).Include(x => x.DimensionAssignments).AsSplitQuery().ToListAsync(ct);
        foreach (var child in children)
        {
            if (Same(child.DimensionAssignments.Select(Selection).ToList(), desired)) continue;
            var before = Snapshot(child); Replace(db, child, desired); child.Revision = Guid.NewGuid();
            History(db, child.AccountId, null, child.Id, actor, "FrameworkDimensionsUpdated", reason, before, Snapshot(child));
        }
    }
}
