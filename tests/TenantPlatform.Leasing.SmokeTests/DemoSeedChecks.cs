using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Initialization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Dimensions;

static class DemoSeedChecks
{
    private sealed record SeedCounts(int Frameworks, int Acquisitions, int Standalone, int Linked,
        int Registered, int Draft, int Cancelled, int ActivePlans, int DraftPlans, int PlanTerms,
        int Items, int Classifications, int Equipment);

    public static async Task Run(IDbContextFactory<TenantPlatformDbContext> factory)
    {
        await using (var db = factory.CreateDbContext())
            await DemoDataInitializer.InitializeAsync(db, new PasswordService());
        var firstRun = await Verify(factory);

        await using (var db = factory.CreateDbContext())
            await DemoDataInitializer.InitializeAsync(db, new PasswordService());
        var secondRun = await Verify(factory);
        Check(firstRun == secondRun, "Magida leasing demo seed is idempotent");
    }

    private static async Task<SeedCounts> Verify(IDbContextFactory<TenantPlatformDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        var accountId = SeedIds.MagidaAccount;
        var acquisitionIds = Enumerable.Range(1, 9).Select(SeedIds.MagidaLeasingAcquisition).ToArray();
        var frameworkIds = Enumerable.Range(1, 5).Select(SeedIds.MagidaLeasingFramework).ToArray();
        var orgs = await db.Organizations.AsNoTracking()
            .Where(x => x.AccountId == accountId && (x.Name == "Leasing corp" || x.Name == "Hybr"))
            .ToListAsync();
        Check(orgs.Count == 2 && orgs.All(x => x.Type == OrganizationType.ServiceProvider && x.IsActive),
            "Magida leasing companies are active organization parties");

        var dimensions = await db.Dimensions.AsNoTracking()
            .Where(x => x.AccountId == accountId && (x.Code == "PROJ" || x.Code == "EQUIP"))
            .ToDictionaryAsync(x => x.Code);
        Check(dimensions.Count == 2 && dimensions.Values.All(x => x.IsActive && x.LeafOnly),
            "project and equipment dimensions are active and leaf-only");
        var rules = await db.LeasingDimensionRules.AsNoTracking()
            .Where(x => x.AccountId == accountId && dimensions.Values.Select(d => d.Id).Contains(x.DimensionId))
            .ToListAsync();
        Check(rules.Count == 2 && rules.All(x => x.IsEnabled),
            "both dimensions are enabled in leasing");

        var values = await db.DimensionValues.AsNoTracking()
            .Where(x => x.AccountId == accountId && dimensions.Values.Select(d => d.Id).Contains(x.DimensionId))
            .ToListAsync();
        var projectTree = DimensionHierarchy.Build(dimensions["PROJ"], values);
        var equipmentTree = DimensionHierarchy.Build(dimensions["EQUIP"], values);
        Check(projectTree.Count == 4 && projectTree.All(x => x.Selectable),
            "all four project leaves are selectable");
        Check(equipmentTree.Count == 12 && equipmentTree.Count(x => x.ParentId == null) == 3 &&
            equipmentTree.Count(x => x.Depth == 1 && x.Selectable) == 9 &&
            equipmentTree.Where(x => x.Depth == 0).All(x => !x.Selectable),
            "equipment categories are unselectable and all nine equipment leaves are selectable");

        var frameworks = await db.LeasingFrameworks.AsNoTracking()
            .Where(x => x.AccountId == accountId && frameworkIds.Contains(x.Id)).ToListAsync();
        var acquisitions = await db.LeasingAcquisitions.AsNoTracking()
            .Where(x => x.AccountId == accountId && acquisitionIds.Contains(x.Id)).ToListAsync();
        Check(frameworks.Count == 5 && frameworks.All(x => orgs.Any(o => o.Name == "Leasing corp" && o.Id == x.FinanceOrganizationId)),
            "five Magida framework agreements use Leasing corp");
        Check(acquisitions.Count == 9 && acquisitions.Count(x => x.FrameworkId == null) == 5 &&
            acquisitions.Count(x => x.FrameworkId.HasValue) == 4 &&
            acquisitions.All(x => orgs.Any(o => o.Name == "Leasing corp" && o.Id == x.FinanceOrganizationId) &&
                orgs.Any(o => o.Name == "Hybr" && o.Id == x.SupplierOrganizationId)),
            "five standalone leases and four framework-linked acquisitions use the intended parties");
        Check(acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Registered) == 7 &&
            acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Draft) == 1 &&
            acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Cancelled) == 1,
            "acquisition statuses include registered, draft and cancelled examples");

        var items = await db.LeasingItems.AsNoTracking().Include(x => x.DimensionSelections)
            .Where(x => x.AccountId == accountId && acquisitionIds.Contains(x.AcquisitionId)).ToListAsync();
        var projectLeaves = projectTree.Where(x => x.Selectable).Select(x => x.Id).ToHashSet();
        var equipmentLeaves = equipmentTree.Where(x => x.Selectable).Select(x => x.Id).ToHashSet();
        Check(items.Count == 17 && items.All(x => x.DimensionSelections.Count == 2 &&
            x.DimensionSelections.Any(s => s.DimensionId == dimensions["PROJ"].Id && projectLeaves.Contains(s.ValueId)) &&
            x.DimensionSelections.Any(s => s.DimensionId == dimensions["EQUIP"].Id && equipmentLeaves.Contains(s.ValueId))),
            "every seeded item has one valid project and equipment leaf classification");

        var equipment = await db.LeasingEquipment.AsNoTracking()
            .Where(x => x.AccountId == accountId && acquisitionIds.Contains(x.AcquisitionId)).ToListAsync();
        var registeredItems = items.Where(x => acquisitions.Any(a =>
            a.Id == x.AcquisitionId && a.Status == LeasingAcquisitionStatus.Registered));
        Check(equipment.Count == registeredItems.Sum(x => (int)x.Quantity) &&
            equipment.All(x => items.Any(i => i.Id == x.ItemId && i.CountableEquipment)),
            "registered counted items have matching physical equipment units");

        var plans = await db.LeasingPaymentPlans.AsNoTracking().Include(x => x.Terms)
            .Where(x => x.AccountId == accountId && acquisitionIds.Contains(x.AcquisitionId)).ToListAsync();
        var revisions = await db.LeasingFinancingRevisions.AsNoTracking()
            .Where(x => x.AccountId == accountId && acquisitionIds.Contains(x.AcquisitionId)).ToListAsync();
        var revisionById = revisions.ToDictionary(x => x.Id);
        Check(plans.Count(x => x.Status == LeasingPlanStatus.Active) == 7 &&
            plans.Count(x => x.Status == LeasingPlanStatus.Draft) == 1 && plans.Count == 8,
            "registered acquisitions have active plans and the draft acquisition retains a draft plan");
        Check(revisions.Count == 9 && revisions.All(x => x.EffectiveFrom.HasValue),
            "each acquisition has dated financing terms");
        foreach (var plan in plans)
        {
            var acquisition = acquisitions.Single(x => x.Id == plan.AcquisitionId);
            Check(plan.Terms.Count > 0 && plan.Terms.All(term =>
                term.FinancingRevisionId.HasValue &&
                revisionById.TryGetValue(term.FinancingRevisionId.Value, out var revision) &&
                revision.AcquisitionId == acquisition.Id &&
                revision.EffectiveFrom <= term.PeriodFrom &&
                term.PeriodFrom >= acquisition.PurchaseDate &&
                term.PeriodTo < acquisition.EndDate &&
                term.Gross == term.Net + term.Vat &&
                term.CapitalComponent + term.InterestComponent + term.FeeComponent == term.Net) &&
                plan.Terms.Sum(x => x.CapitalComponent) == acquisition.FinancedAmount,
                "payment-plan dates, financing basis and amounts match the acquisition");
        }

        return new(frameworks.Count, acquisitions.Count, acquisitions.Count(x => x.FrameworkId == null),
            acquisitions.Count(x => x.FrameworkId.HasValue), acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Registered),
            acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Draft),
            acquisitions.Count(x => x.Status == LeasingAcquisitionStatus.Cancelled),
            plans.Count(x => x.Status == LeasingPlanStatus.Active), plans.Count(x => x.Status == LeasingPlanStatus.Draft),
            plans.Sum(x => x.Terms.Count), items.Count, items.Sum(x => x.DimensionSelections.Count), equipment.Count);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
