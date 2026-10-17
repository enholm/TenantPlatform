using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Buildings;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Buildings;

static class BuildingChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAIL building Application: " + name);
            Console.WriteLine($"PASS {provider} building Application: {name}");
        }
        async Task Denied(Func<Task> action, string name)
        {
            try { await action(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid();
        var scoped = Guid.NewGuid(); var platform = Guid.NewGuid(); var building = Guid.NewGuid(); var hidden = Guid.NewGuid();
        var foreignBuilding = Guid.NewGuid(); var scopedMembership = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Building A" }, new Account { Id = other, Name = "Building B" });
            foreach (var id in new[] { admin, scoped, platform })
            {
                db.Users.Add(new() { Id = id, FirstName = "Building", Email = id + "@example.test", IsActive = true, IsPlatformAdmin = id == platform });
                var membership = new UserAccount { Id = id == scoped ? scopedMembership : Guid.NewGuid(), AccountId = account, UserId = id };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Buildings.AddRange(new Building { Id = building, AccountId = account, Name = "Scoped", CountryCode = "NO" },
                new Building { Id = hidden, AccountId = account, Name = "Other building", CountryCode = "NO" },
                new Building { Id = foreignBuilding, AccountId = other, Name = "Foreign", CountryCode = "NO" });
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = scopedMembership, Role = UserRole.PropertyAdmin, BuildingId = building });
            // A separate leasing assignment must not lend its wider scope to PropertyAdmin.
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = scopedMembership, Role = UserRole.LeasingReportReader });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, scoped, account));
        var service = new BuildingService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), new BuildingData(factory));
        var evaluator = new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System);
        var units = new TenantPlatform.Application.Units.UnitService(evaluator, new TenantPlatform.Infrastructure.Units.UnitData(factory));
        var home = new TenantPlatform.Application.Dashboard.HomeReader(evaluator, new HomeData(factory));
        var accounts = new TenantPlatform.Application.Accounts.AccountService(evaluator, new TenantPlatform.Infrastructure.Accounts.AccountData(factory));
        var organizations = new TenantPlatform.Application.Organizations.OrganizationService(evaluator, new TenantPlatform.Infrastructure.Organizations.OrganizationData(factory));
        guard.Protect = true;
        try
        {
            Check((await home.ReadAsync(account)).BuildingCount == 1, "Home limits aggregates to the same PropertyAdmin assignment's building");
            await Denied(() => accounts.GetAccountsAsync(), "account administrator screens require global platform authority");
            Check((await organizations.GetOrganizationsAsync(account)).Count == 0, "PropertyAdmin organization selector preserves explicit read exception");
            Check((await service.GetBuildingsAsync(account)).Count == 2, "isolated explicit legacy PropertyAdmin list exception");
            Check((await service.GetBuildingAsync(account, building))?.Name == "Scoped", "scoped detail read");
            await Denied(() => service.GetBuildingAsync(account, hidden), "list exception grants no other building detail");
            await Denied(() => service.GetBuildingAsync(account, foreignBuilding), "foreign building denied");
            await Denied(() => service.GetBuildingsAsync(other), "client account rejected");
            actor.Current = new(true, platform, account);
            await Denied(() => service.GetBuildingsAsync(account), "PlatformAdmin has no automatic account list access");
            await Denied(() => home.ReadAsync(account), "PlatformAdmin has no Home aggregate authority");
            Check((await accounts.GetAccountsAsync()).Any(x => x.Id == other), "global platform account management remains separate from tenant data");
            actor.Current = new(true, admin, account);
            Check((await home.ReadAsync(account)).BuildingCount == 2, "Home account administrator aggregates");
            Check((await service.GetBuildingAsync(account, hidden))?.Name == "Other building", "AccountAdmin reads selected account");
        }
        finally { guard.Protect = false; }
        if (provider == "PostgreSQL")
        {
            actor.Current = new(true, scoped, account);
            var rootUnit = await units.CreateUnitAsync(account, new() { BuildingId = building, Name = "Root" });
            var childUnit = await units.CreateUnitAsync(account, new() { BuildingId = building, ParentUnitId = rootUnit, Name = "Child" });
            Check((await units.GetUnitAsync(account, rootUnit))?.ChildUnitCount == 1, "unit create and account-bounded detail aggregates");
            await Denied(() => units.UpdateUnitAsync(account, rootUnit, new() { BuildingId = hidden, Name = "Move attack" }), "unit move verifies target building authority");
            try
            {
                await units.UpdateUnitAsync(account, rootUnit, new() { BuildingId = building, ParentUnitId = childUnit, Name = "Cycle" });
                throw new Exception("Expected unit cycle denial");
            }
            catch (TenantPlatform.Application.Units.UnitValidationException ex) when (ex.Message == "UnitParentCreatesCircularHierarchy")
            { Check(true, "Application unit cycle validation runs under account transaction lock"); }
            Check(!(await units.CanDeleteUnitAsync(account, rootUnit)).CanDelete, "unit parent cannot be deleted");
            await units.DeleteUnitAsync(account, childUnit);
            await units.DeleteUnitAsync(account, rootUnit);
            actor.Current = new(true, admin, account);
            var organization = await organizations.CreateOrganizationAsync(account, new() { Name = "  Tenant  ", OrganizationNumber = "  123  ", IsActive = true });
            Check((await organizations.GetOrganizationAsync(account, organization)) is { Name: "Tenant", OrganizationNumber: "123" }, "organization create through Application preserves normalization");
            actor.Current = new(true, scoped, account);
            Check((await organizations.GetOrganizationAsync(account, organization))?.Name == "Tenant", "PropertyAdmin explicit organization read exception");
            await Denied(() => organizations.UpdateOrganizationAsync(account, organization, new() { Name = "Attack" }), "organization read exception gives no edit authority");
            actor.Current = new(true, admin, account);
            await organizations.UpdateOrganizationAsync(account, organization, new() { Name = "Updated tenant", IsActive = false });
            await organizations.DeleteOrganizationAsync(account, organization);
            actor.Current = new(true, platform, null);
            var newAccount = await accounts.CreateAccountAsync(new() { Name = "  Managed account  ", DefaultLanguage = "nb-NO", IsActive = true });
            Check((await accounts.GetAccountAsync(newAccount))?.Name == "Managed account", "platform creates account without tenant membership");
            await accounts.UpdateAccountAsync(newAccount, new() { Name = "Renamed", DefaultLanguage = "en-GB", IsActive = false });
            Check((await accounts.GetAccountAsync(newAccount)) is { Name: "Renamed", IsActive: false }, "platform updates account through Application");
            await accounts.DeleteAccountAsync(newAccount);
            actor.Current = new(true, admin, account);
            var id = await service.CreateBuildingAsync(account, new() { Name = "  New  ", CountryCode = "no" });
            Check((await service.GetBuildingAsync(account, id)) is { Name: "New", CountryCode: "NO" }, "create normalizes trusted account resource");
            await service.UpdateBuildingAsync(account, id, new() { Name = "Updated", CountryCode = "se" });
            Check((await service.GetBuildingAsync(account, id)) is { Name: "Updated", CountryCode: "SE" }, "update through Application and locked account predicate");
            actor.Current = new(true, scoped, account);
            await Denied(() => service.UpdateBuildingAsync(account, id, new() { Name = "Attack", CountryCode = "NO" }), "unrelated role scope cannot authorize update");
            await Denied(() => service.DeleteBuildingAsync(account, id), "unrelated building delete denied");
            actor.Current = new(true, admin, account);
            await service.DeleteBuildingAsync(account, id);
            await using var db = factory.CreateDbContext();
            Check(!await db.Buildings.AnyAsync(x => x.Id == id), "authorized delete commits");
        }
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == admin)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await Denied(() => service.GetBuildingsAsync(account), "inactive administrator rejected on reused service");
    }
}
