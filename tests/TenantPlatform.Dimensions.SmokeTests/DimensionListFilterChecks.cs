using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

static class DimensionListFilterChecks
{
    private sealed record Resource(Guid Id, string Kind, string Status, List<(Guid DimensionId, Guid ValueId)> Assignments);

    public static async Task Run()
    {
        var account = Guid.NewGuid(); var geo = Guid.NewGuid(); var dept = Guid.NewGuid(); var historical = Guid.NewGuid();
        var norway = Guid.NewGuid(); var east = Guid.NewGuid(); var oslo = Guid.NewGuid(); var sweden = Guid.NewGuid(); var drift = Guid.NewGuid(); var sales = Guid.NewGuid(); var old = Guid.NewGuid();
        var dimensions = new List<Dimension>
        {
            new() { Id = geo, AccountId = account, Code = "GEO", Name = "Geography", IsActive = true, LeafOnly = true },
            new() { Id = dept, AccountId = account, Code = "DEPT", Name = "Department", IsActive = true },
            new() { Id = historical, AccountId = account, Code = "OLD", Name = "Old dimension", IsActive = false }
        };
        var values = new List<DimensionValue>
        {
            new() { Id = norway, AccountId = account, DimensionId = geo, Name = "Norway", Code = "NO", IsActive = true },
            new() { Id = east, AccountId = account, DimensionId = geo, ParentId = norway, Name = "East", Code = "EAST", IsActive = true },
            new() { Id = oslo, AccountId = account, DimensionId = geo, ParentId = east, Name = "Oslo", Code = "OSL", IsActive = true },
            new() { Id = sweden, AccountId = account, DimensionId = geo, Name = "Sweden", Code = "SE", IsActive = true },
            new() { Id = drift, AccountId = account, DimensionId = dept, Name = "Operations", Code = "OPS", IsActive = true },
            new() { Id = sales, AccountId = account, DimensionId = dept, Name = "Sales", Code = "SALES", IsActive = true },
            new() { Id = old, AccountId = account, DimensionId = historical, Name = "Legacy", Code = "LEGACY", IsActive = false }
        };
        var catalog = DimensionListFilters.Catalog(dimensions, values, new HashSet<Guid> { geo, dept },
            new HashSet<(Guid, Guid)> { (historical, old) }, "en-GB", "nb-NO");
        Verify.That(catalog.Dimensions.Count == 3 && catalog.Dimensions.Single(x => x.DimensionId == geo).Values.Select(x => x.Id).ToHashSet().IsSupersetOf([norway, east, oslo]) &&
            catalog.Dimensions.Single(x => x.DimensionId == historical).Values.Single().IsHistorical,
            "LeafOnly list filter catalog includes selectable roots, intermediate nodes, leaves and actually used historical values");

        var resources = new List<Resource>
        {
            new(Guid.NewGuid(), "Agreement", "Open", [(geo, oslo), (dept, drift)]),
            new(Guid.NewGuid(), "Framework", "Open", [(geo, norway), (dept, drift)]),
            new(Guid.NewGuid(), "StandaloneAcquisition", "Closed", [(geo, sweden), (dept, sales)]),
            new(Guid.NewGuid(), "InheritedAcquisition", "Open", [(geo, oslo), (dept, drift)]),
            new(Guid.NewGuid(), "Agreement", "Open", [(dept, drift)]),
            new(Guid.NewGuid(), "Framework", "Open", [(historical, old)])
        };
        var parentAndDepartment = DimensionListFilters.Plan(catalog,
        [
            new() { DimensionId = geo, ValueId = norway },
            new() { DimensionId = dept, ValueId = drift }
        ]);
        var parentMatches = resources.Where(x => DimensionListFilters.Matches(x.Assignments, parentAndDepartment)).ToList();
        Verify.That(parentMatches.Count == 3 && parentMatches.Select(x => x.Kind).ToHashSet().SetEquals(["Agreement", "Framework", "InheritedAcquisition"]),
            "parent expansion and AND across dimensions cover agreement, framework and inherited acquisition snapshots");

        var childOnly = DimensionListFilters.Plan(catalog, [new() { DimensionId = geo, ValueId = oslo }]);
        Verify.That(DimensionListFilters.Matches(resources[0].Assignments, childOnly) && !DimensionListFilters.Matches(resources[1].Assignments, childOnly),
            "child filter matches child assignment but a parent assignment does not match its child");
        var legacySelection = new DimensionListFilterSelection { DimensionId = geo, ValueIds = [sweden, norway] };
        var legacyMultiple = DimensionListFilters.Plan(catalog, [legacySelection]);
        Verify.That(resources.Count(x => DimensionListFilters.Matches(x.Assignments, legacyMultiple)) == 3 &&
            legacyMultiple.Single().MatchingValueIds.Contains(norway) && !legacyMultiple.Single().MatchingValueIds.Contains(sweden) &&
            legacySelection.ValueId == norway && legacySelection.ValueIds.Count == 0,
            "legacy multiple values normalize state and query visibly to the first value in catalog order");
        var unspecified = DimensionListFilters.Plan(catalog, [new() { DimensionId = geo, IncludeUnspecified = true }]);
        Verify.That(resources.Count(x => DimensionListFilters.Matches(x.Assignments, unspecified)) == 2,
            "not specified matches only resources without an assignment in that dimension");
        var unspecifiedSelection = new DimensionListFilterSelection { DimensionId = geo, ValueId = sweden, IncludeUnspecified = true };
        var normalizedUnspecified = DimensionListFilters.Plan(catalog, [unspecifiedSelection]);
        Verify.That(resources.Count(x => DimensionListFilters.Matches(x.Assignments, normalizedUnspecified)) == 2 &&
            normalizedUnspecified.Single().MatchingValueIds.Length == 0 && unspecifiedSelection.ValueId is null,
            "not specified takes precedence over stale concrete input so no hidden second criterion remains");
        var historicalPlan = DimensionListFilters.Plan(catalog, [new() { DimensionId = historical, ValueId = old }]);
        Verify.That(resources.Single(x => DimensionListFilters.Matches(x.Assignments, historicalPlan)).Kind == "Framework",
            "historical inactive assignments remain filterable and are not treated as unspecified");

        var combined = resources.Where(x => x.Status == "Open" && DimensionListFilters.Matches(x.Assignments, parentAndDepartment))
            .OrderBy(x => x.Id).ToList();
        var total = combined.Count; var firstPage = combined.Take(2).ToList(); var secondPage = combined.Skip(2).Take(2).ToList();
        Verify.That(total == 3 && firstPage.Count == 2 && secondPage.Count == 1 && firstPage.Concat(secondPage).Select(x => x.Id).Distinct().Count() == 3,
            "dimension criteria combine with existing filters before total, stable sorting and pagination");

        await Verify.Throws<DimensionValidationException>(() => Task.FromResult(DimensionListFilters.Plan(catalog,
            [new() { DimensionId = Guid.NewGuid(), ValueId = norway }])), "manipulated or foreign-account dimension IDs are rejected");
        await Verify.Throws<DimensionValidationException>(() => Task.FromResult(DimensionListFilters.Plan(catalog,
            [new() { DimensionId = geo, ValueId = drift }])), "value IDs are validated against their dimension");
        var stable = new List<DimensionListFilterSelection> { new() { DimensionId = geo, ValueId = norway } };
        _ = DimensionListFilters.Catalog(dimensions, values, new HashSet<Guid> { geo, dept }, new HashSet<(Guid, Guid)>(), "sv-SE", "nb-NO");
        Verify.That(stable.Single().DimensionId == geo && stable.Single().ValueId == norway,
            "localized catalog refresh does not change stable selected IDs");
        stable.Clear();
        Verify.That(stable.Count == 0, "filter reset removes dimension criteria");

        await ServiceQueries();
    }

    private static async Task ServiceQueries()
    {
        var account = Guid.NewGuid(); var user = Guid.NewGuid(); var org = Guid.NewGuid(); var dimension = Guid.NewGuid();
        var parent = Guid.NewGuid(); var child = Guid.NewGuid(); var other = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new Factory(options); var context = new Context(user, account); var authorization = new TenantAuthorizationService(factory, context);
        var agreementIds = new List<Guid>(); Guid frameworkId; Guid standaloneId; Guid inheritedId;
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id = account, Name = "Filter account", DefaultLanguage = "nb-NO" });
            db.Users.Add(new User { Id = user, Email = "filter@example.test", FirstName = "Filter", LastName = "Owner", IsActive = true });
            var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = user };
            db.UserAccounts.Add(membership); db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            db.Organizations.Add(new() { Id = org, AccountId = account, Name = "Counterparty" });
            db.Dimensions.Add(new Dimension { Id = dimension, AccountId = account, Name = "Geography", Code = "GEO", IsActive = true, AllowMultiple = true });
            db.DimensionValues.AddRange(
                new DimensionValue { Id = parent, AccountId = account, DimensionId = dimension, Name = "Norway", Code = "NO", IsActive = true },
                new DimensionValue { Id = child, AccountId = account, DimensionId = dimension, ParentId = parent, Name = "Oslo", Code = "OSL", IsActive = true },
                new DimensionValue { Id = other, AccountId = account, DimensionId = dimension, Name = "Sweden", Code = "SE", IsActive = true });
            db.AgreementDimensionRules.Add(new() { AccountId = account, DimensionId = dimension, IsEnabled = true });
            db.LeasingAgreementDimensionRules.Add(new() { AccountId = account, DimensionId = dimension, IsEnabled = true });
            for (var index = 0; index < 27; index++)
            {
                var id = Guid.NewGuid(); agreementIds.Add(id);
                db.Agreements.Add(new Agreement { Id = id, AccountId = account, Title = $"Agreement {index:00}", CounterpartyOrganizationId = org,
                    OwnerUserId = user, Status = index % 2 == 0 ? AgreementStatus.Active : AgreementStatus.Draft });
                if (index < 26) db.AgreementDimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = id,
                    DimensionId = dimension, ValueId = index % 3 == 0 ? child : parent });
                if (index == 0) db.AgreementDimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = id,
                    DimensionId = dimension, ValueId = other });
            }
            frameworkId = Guid.NewGuid(); standaloneId = Guid.NewGuid(); inheritedId = Guid.NewGuid();
            var framework = new LeasingFramework { Id = frameworkId, AccountId = account, Name = "Framework", Number = "F-1", OwnerUserId = user,
                FinanceOrganizationId = org, AcquisitionFrom = new(2026, 1, 1), AcquisitionTo = new(2027, 1, 1), Currency = "NOK" };
            framework.DimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = account, FrameworkId = frameworkId, DimensionId = dimension, ValueId = child,
                DimensionName = "Geography", DimensionCode = "GEO", ValueName = "Oslo", ValueCode = "OSL", ValuePath = "Norway / Oslo" });
            var standalone = new LeasingAcquisition { Id = standaloneId, AccountId = account, Name = "Standalone", Reference = "A-1", OwnerUserId = user,
                SupplierOrganizationId = org, FinanceOrganizationId = org, PurchaseDate = new(2026, 1, 1), EndDate = new(2027, 1, 1), Currency = "NOK" };
            standalone.DimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = standaloneId, DimensionId = dimension, ValueId = other,
                DimensionName = "Geography", DimensionCode = "GEO", ValueName = "Sweden", ValueCode = "SE", ValuePath = "Sweden" });
            var inherited = new LeasingAcquisition { Id = inheritedId, AccountId = account, FrameworkId = frameworkId, Name = "Inherited", Reference = "A-2", OwnerUserId = user,
                SupplierOrganizationId = org, FinanceOrganizationId = org, PurchaseDate = new(2026, 1, 1), EndDate = new(2027, 1, 1), Currency = "NOK" };
            inherited.DimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = inheritedId, DimensionId = dimension, ValueId = child,
                DimensionName = "Geography", DimensionCode = "GEO", ValueName = "Oslo", ValueCode = "OSL", ValuePath = "Norway / Oslo" });
            db.LeasingFrameworks.Add(framework); db.LeasingAcquisitions.AddRange(standalone, inherited); await db.SaveChangesAsync();
        }
        var agreementService = new AgreementService(factory, context, authorization, null!, NullLogger<AgreementService>.Instance);
        var agreementPage = await agreementService.ListAsync(account, new AgreementFilter
        {
            Status = AgreementStatus.Active, Page = 1,
            Dimensions = [new() { DimensionId = dimension, ValueId = parent }]
        });
        Verify.That(agreementPage.TotalCount == 13 && agreementPage.Items.Count == 13 && agreementPage.Items.Select(x => x.Id).Distinct().Count() == 13 &&
            agreementPage.Items.All(x => x.Status == AgreementStatus.Active),
            "Agreements service applies dimensions with existing filters before total and paging without duplicate rows (InMemory)");
        var agreementSecondPage = await agreementService.ListAsync(account, new AgreementFilter
        {
            Page = 2, Dimensions = [new() { DimensionId = dimension, ValueId = parent }]
        });
        Verify.That(agreementSecondPage.TotalCount == 26 && agreementSecondPage.Page == 2 && agreementSecondPage.Items.Count == 1,
            "Agreements service calculates page bounds from the dimension-filtered total (InMemory)");
        var unspecified = await agreementService.ListAsync(account, new AgreementFilter
        {
            Dimensions = [new() { DimensionId = dimension, IncludeUnspecified = true }]
        });
        Verify.That(unspecified.TotalCount == 1 && unspecified.Items.Single().Id == agreementIds[^1],
            "Agreements service applies not-specified semantics before paging (InMemory)");

        var leasingService = new LeasingService(factory, context, authorization, null!, TimeProvider.System, NullLogger<LeasingService>.Instance);
        var frameworks = await leasingService.ListAsync(account, new LeasingListFilter
        {
            Kind = "frameworks", Dimensions = [new() { DimensionId = dimension, ValueId = parent }]
        });
        Verify.That(frameworks.Count == 1 && frameworks.Items.Single().Id == frameworkId,
            "Leasing framework list filters its own agreement-level assignments (InMemory)");
        var acquisitions = await leasingService.ListAsync(account, new LeasingListFilter
        {
            Kind = "acquisitions", Dimensions = [new() { DimensionId = dimension, ValueId = parent }]
        });
        Verify.That(acquisitions.Count == 1 && acquisitions.Items.Single().Id == inheritedId && acquisitions.Items.Single().Id != standaloneId,
            "Leasing acquisition list filters effective inherited snapshots separately from standalone choices (InMemory)");
    }

    private sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
    { public TenantPlatformDbContext CreateDbContext() => new(options); }
    private sealed class Context(Guid user, Guid account) : ICurrentUserContextService
    { public CurrentUserContext Current { get; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account }; }
}
