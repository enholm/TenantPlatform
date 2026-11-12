using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    private sealed record AgreementDimensionCatalog(List<Dimension> Dimensions, List<DimensionValue> Values,
        Dictionary<Guid, AgreementDimensionRule> Rules, string? DefaultLanguage);

    private static async Task<AgreementDimensionCatalog> LoadAgreementDimensionCatalogAsync(TenantPlatformDbContext db, Guid account, CancellationToken ct) => new(
        await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account)
            .Include(x => x.Translations.Where(t => t.AccountId == account)).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct),
        await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
        await db.AgreementDimensionRules.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.DimensionId, ct),
        await db.Accounts.AsNoTracking().Where(x => x.Id == account).Select(x => x.DefaultLanguage).SingleAsync(ct));

    private async Task RequireAgreementDimensionAdministratorAsync(TenantPlatformDbContext db, Guid account, CancellationToken ct)
    {
        await RequireMemberAsync(db, account, ct);
        if (!await authorization.CanManageAccountStructureAsync(ct)) throw new UnauthorizedAccessException();
    }

    public async Task<List<AgreementDimensionRuleDto>> GetAgreementDimensionRulesAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAgreementDimensionAdministratorAsync(db, accountId, ct);
        var catalog = await LoadAgreementDimensionCatalogAsync(db, accountId, ct);
        return catalog.Dimensions.Select(d =>
        {
            var rule = catalog.Rules.GetValueOrDefault(d.Id);
            return new AgreementDimensionRuleDto(d.Id, d.Code, DimensionLocalization.Name(d, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                DimensionLocalization.Description(d, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                d.IsActive, d.AllowMultiple, d.LeafOnly, rule?.IsEnabled ?? false, rule?.IsRequired ?? false, rule?.Revision ?? Guid.Empty);
        }).ToList();
    }

    public async Task SaveAgreementDimensionRuleAsync(Guid accountId, AgreementDimensionRule input, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAgreementDimensionAdministratorAsync(db, accountId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, null, ct);
        await SaveAgreementDimensionRuleCoreAsync(db, accountId, input, ct);
        await tx.CommitAsync(ct);
    }

    // Relational wrapper holds the account lock; core retains authorization and
    // can be exercised without substituting provider-specific production paths.
    private async Task SaveAgreementDimensionRuleCoreAsync(TenantPlatformDbContext db, Guid account, AgreementDimensionRule input, CancellationToken ct)
    {
        await RequireAgreementDimensionAdministratorAsync(db, account, ct);
        if (input.AccountId != Guid.Empty && input.AccountId != account) throw new UnauthorizedAccessException();
        if (!await db.Dimensions.AnyAsync(x => x.AccountId == account && x.Id == input.DimensionId, ct)) throw new UnauthorizedAccessException();
        var rule = await db.AgreementDimensionRules.SingleOrDefaultAsync(x => x.AccountId == account && x.DimensionId == input.DimensionId, ct);
        if ((rule?.Revision ?? Guid.Empty) != input.Revision) throw new DimensionValidationException("DimensionConcurrency");
        var before = rule is null ? "{}" : AgreementRuleSnapshot(rule);
        if (rule is null)
        {
            rule = new AgreementDimensionRule { AccountId = account, DimensionId = input.DimensionId };
            db.AgreementDimensionRules.Add(rule);
        }
        rule.IsEnabled = input.IsEnabled;
        rule.IsRequired = input.IsEnabled && input.IsRequired;
        rule.Revision = Guid.NewGuid();
        db.DimensionHistory.Add(new DimensionHistory { Id = Guid.NewGuid(), AccountId = account, DimensionId = rule.DimensionId,
            ActorUserId = userContext.Current.UserId, RecordedUtc = Clock.GetUtcNow(), BeforeJson = before, AfterJson = AgreementRuleSnapshot(rule) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new DimensionValidationException("DimensionConcurrency"); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new DimensionValidationException("DimensionConcurrency"); }
    }

    private static string AgreementRuleSnapshot(AgreementDimensionRule rule) => Json(new
    {
        Module = "Agreements", IsEnabledInAgreements = rule.IsEnabled, IsRequiredAtRegistration = rule.IsRequired, rule.Revision
    });

    public async Task<AgreementDimensionOptionsDto> GetAgreementDimensionOptionsAsync(Guid accountId, Guid? agreementId = null,
        bool forEdit = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        Agreement? agreement = null;
        bool canEdit;
        if (agreementId.HasValue)
        {
            var access = await RequireAsync(db, accountId, agreementId.Value, forEdit, false, ct);
            agreement = access.Agreement;
            canEdit = access.Edit && !agreement.IsArchived;
        }
        else
        {
            canEdit = (await RequireMemberAsync(db, accountId, ct)).Admin;
            if (!canEdit) throw new UnauthorizedAccessException();
        }
        return new(agreementId, agreement?.Revision ?? Guid.Empty, canEdit,
            await LoadAgreementDimensionOptionsAsync(db, accountId, agreement, ct));
    }

    private static async Task<List<AgreementDimensionOptionDto>> LoadAgreementDimensionOptionsAsync(TenantPlatformDbContext db,
        Guid account, Agreement? agreement, CancellationToken ct)
    {
        var catalog = await LoadAgreementDimensionCatalogAsync(db, account, ct);
        var assigned = agreement is null ? [] : await db.AgreementDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == account && x.AgreementId == agreement.Id).ToListAsync(ct);
        var result = new List<AgreementDimensionOptionDto>();
        foreach (var d in catalog.Dimensions)
        {
            var rule = catalog.Rules.GetValueOrDefault(d.Id);
            var selections = assigned.Where(x => x.DimensionId == d.Id).Select(x => x.ValueId).ToHashSet();
            var available = d.IsActive && rule?.IsEnabled == true;
            if (!available && selections.Count == 0) continue;
            var paths = DimensionHierarchy.Build(d, catalog.Values);
            result.Add(new(d.Id, d.Code, DimensionLocalization.Name(d, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                DimensionLocalization.Description(d, CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage),
                d.IsActive, rule?.IsEnabled ?? false, rule?.IsRequired ?? false, d.AllowMultiple, d.LeafOnly,
                paths.Where(x => available && x.Selectable || selections.Contains(x.Id))
                    .Select(x => new AgreementDimensionValueDto(x.Id, x.ParentId, x.Name, x.Code, x.Path, x.Depth,
                        x.IsActive, available && x.Selectable, selections.Contains(x.Id))).ToList()));
        }
        return result;
    }

    private static async Task<DimensionListFilterCatalog> LoadAgreementListFilterCatalogAsync(TenantPlatformDbContext db,
        Guid account, IQueryable<Agreement> visible, CancellationToken ct)
    {
        var catalog = await LoadAgreementDimensionCatalogAsync(db, account, ct);
        var enabled = catalog.Rules.Values.Where(x => x.IsEnabled).Select(x => x.DimensionId).ToHashSet();
        var used = await db.AgreementDimensionAssignments.AsNoTracking()
            .Where(x => x.AccountId == account && visible.Any(a => a.Id == x.AgreementId))
            .Select(x => new { x.DimensionId, x.ValueId }).Distinct().ToListAsync(ct);
        return DimensionListFilters.Catalog(catalog.Dimensions, catalog.Values, enabled,
            used.Select(x => (x.DimensionId, x.ValueId)).ToHashSet(), CultureInfo.CurrentUICulture.Name, catalog.DefaultLanguage);
    }

    public async Task<List<AgreementDimensionHistoryDto>> GetAgreementDimensionHistoryAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAsync(db, accountId, agreementId, false, false, ct);
        return await LoadAgreementDimensionHistoryAsync(db, accountId, agreementId, ct);
    }

    private static async Task<List<AgreementDimensionHistoryDto>> LoadAgreementDimensionHistoryAsync(TenantPlatformDbContext db, Guid account, Guid agreementId, CancellationToken ct)
    {
        var rows = await db.AgreementDimensionHistory.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreementId)
            .OrderByDescending(x => x.RecordedUtc).ThenByDescending(x => x.Id).ToListAsync(ct);
        var actorIds = rows.Select(x => x.ActorUserId).Distinct().ToArray();
        var actors = await db.Users.AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (x.FirstName + " " + x.LastName).Trim(), ct);
        return rows.Select(x => new AgreementDimensionHistoryDto(x.Id, x.RecordedUtc, x.ActorUserId,
            actors.GetValueOrDefault(x.ActorUserId, x.ActorUserId.ToString()),
            ReadJson<List<AgreementDimensionSetSnapshot>>(x.BeforeJson), ReadJson<List<AgreementDimensionSetSnapshot>>(x.AfterJson))).ToList();
    }

    // Called only inside the caller's account -> agreement lock boundary. All
    // patches are validated before any tracked assignment/history is changed.
    private async Task ApplyAgreementDimensionsAsync(TenantPlatformDbContext db, Guid account, Agreement agreement,
        List<AgreementDimensionPatch>? changes, bool creating, CancellationToken ct)
    {
        if (agreement.AccountId != account) throw new UnauthorizedAccessException();
        var patches = new List<AgreementDimensionPatch>();
        var seen = new HashSet<Guid>();
        foreach (var patch in changes ?? [])
        {
            if (patch is null || patch.DimensionId == Guid.Empty || patch.ValueIds is null || patch.ValueIds.Contains(Guid.Empty))
                throw new AgreementValidationException("AgreementDimensionInvalidSelection");
            if (!seen.Add(patch.DimensionId)) throw new AgreementValidationException("AgreementDimensionDuplicatePatch");
            if (patch.ValueIds.Distinct().Count() != patch.ValueIds.Count) throw new AgreementValidationException("AgreementDimensionDuplicateValue");
            // Freeze caller-owned input before awaiting catalog reads.
            patches.Add(new AgreementDimensionPatch { DimensionId = patch.DimensionId, ValueIds = [..patch.ValueIds] });
        }
        // An unrelated edit need not reload or revalidate historical dimensions.
        if (!creating && patches.Count == 0) return;
        var catalog = await LoadAgreementDimensionCatalogAsync(db, account, ct);
        var existing = creating ? [] : await db.AgreementDimensionAssignments
            .Where(x => x.AccountId == account && x.AgreementId == agreement.Id).ToListAsync(ct);
        if (creating)
        {
            var required = catalog.Rules.Values.Where(x => x.IsRequired).ToList();
            foreach (var rule in required)
            {
                var dimension = catalog.Dimensions.SingleOrDefault(x => x.Id == rule.DimensionId);
                if (dimension is null || !rule.IsEnabled || !dimension.IsActive || !DimensionHierarchy.Build(dimension, catalog.Values).Any(x => x.Selectable))
                    throw new AgreementValidationException("AgreementDimensionRequiredConfiguration");
            }
            if (required.Any(rule => !patches.Any(x => x.DimensionId == rule.DimensionId && x.ValueIds!.Count > 0)))
                throw new AgreementValidationException("AgreementDimensionRequired");
        }
        var plans = new List<(Dimension Dimension, List<AgreementDimensionAssignment> Old, List<Guid> Desired, List<DimensionValueOption> Paths)>();
        foreach (var patch in patches)
        {
            var desired = patch.ValueIds!;
            var dimension = catalog.Dimensions.SingleOrDefault(x => x.Id == patch.DimensionId)
                ?? throw new AgreementValidationException("AgreementDimensionInvalidSelection");
            var rule = catalog.Rules.GetValueOrDefault(dimension.Id);
            var values = catalog.Values.Where(x => x.DimensionId == dimension.Id).Select(x => x.Id).ToHashSet();
            if (desired.Any(x => !values.Contains(x))) throw new AgreementValidationException("AgreementDimensionInvalidSelection");
            var old = existing.Where(x => x.DimensionId == dimension.Id).ToList();
            var oldIds = old.Select(x => x.ValueId).ToHashSet();
            if (!creating && oldIds.SetEquals(desired)) continue;
            if (rule?.IsRequired == true && desired.Count == 0) throw new AgreementValidationException("AgreementDimensionRequired");
            if (!dimension.AllowMultiple && desired.Count > 1) throw new AgreementValidationException("AgreementDimensionSingleSelection");
            var additions = desired.Except(oldIds).ToList();
            if (additions.Count > 0 && (rule?.IsEnabled != true || !dimension.IsActive))
                throw new AgreementValidationException("AgreementDimensionUnavailable");
            var paths = DimensionHierarchy.Build(dimension, catalog.Values);
            foreach (var addition in additions)
            {
                var value = paths.Single(x => x.Id == addition);
                if (!value.IsActive) throw new AgreementValidationException("AgreementDimensionInactiveValue");
                if (!value.Selectable) throw new AgreementValidationException("AgreementDimensionLeafOnly");
            }
            if (!oldIds.SetEquals(desired)) plans.Add((dimension, old, desired, paths));
        }
        if (plans.Count == 0) return;
        AgreementDimensionSetSnapshot Snapshot(Dimension dimension, IEnumerable<Guid> ids, List<DimensionValueOption> paths) =>
            new(dimension.Id, dimension.Code, dimension.Name, ids.OrderBy(x => x).Select(id =>
            {
                var value = paths.Single(x => x.Id == id);
                return new AgreementDimensionValueSnapshot(value.Id, value.Code, value.Name, value.Path);
            }).ToList());
        var before = plans.OrderBy(x => x.Dimension.Id).Select(x => Snapshot(x.Dimension, x.Old.Select(v => v.ValueId), x.Paths)).ToList();
        var after = plans.OrderBy(x => x.Dimension.Id).Select(x => Snapshot(x.Dimension, x.Desired, x.Paths)).ToList();
        foreach (var plan in plans)
        {
            db.AgreementDimensionAssignments.RemoveRange(plan.Old.Where(x => !plan.Desired.Contains(x.ValueId)));
            var retained = plan.Old.Select(x => x.ValueId).ToHashSet();
            foreach (var valueId in plan.Desired.Where(x => !retained.Contains(x)))
                db.AgreementDimensionAssignments.Add(new AgreementDimensionAssignment { Id = Guid.NewGuid(), AccountId = account,
                    AgreementId = agreement.Id, DimensionId = plan.Dimension.Id, ValueId = valueId });
        }
        db.AgreementDimensionHistory.Add(new AgreementDimensionHistory { Id = Guid.NewGuid(), AccountId = account,
            AgreementId = agreement.Id, ActorUserId = userContext.Current.UserId, RecordedUtc = Clock.GetUtcNow(), BeforeJson = Json(before), AfterJson = Json(after) });
    }
}
