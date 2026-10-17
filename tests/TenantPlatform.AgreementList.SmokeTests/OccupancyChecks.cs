using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Occupancies;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Occupancies;

static class OccupancyChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL occupancy: " + name); Console.WriteLine($"PASS {provider} occupancy: {name}"); }
        async Task Denied(Func<Task> action, string name)
        { try { await action(); throw new Exception("Expected denial: " + name); } catch (UnauthorizedAccessException) { Check(true, name); } }
        async Task Invalid(Func<Task> action, string key)
        { try { await action(); throw new Exception("Expected " + key); } catch (OccupancyValidationException ex) when (ex.Message == key) { Check(true, key); } }
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var actorId = Guid.NewGuid(); var membership = Guid.NewGuid();
        var building = Guid.NewGuid(); var outside = Guid.NewGuid(); var root = Guid.NewGuid(); var child = Guid.NewGuid(); var third = Guid.NewGuid();
        var outsideUnit = Guid.NewGuid(); var tenant = Guid.NewGuid(); var foreignTenant = Guid.NewGuid(); var occurrence = Guid.NewGuid(); var inaccessible = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 17); var clock = new Clock(new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Occupancy A" }, new Account { Id = other, Name = "Occupancy B" });
            db.Users.Add(new User { Id = actorId, FirstName = "Property", Email = actorId + "@example.test", IsActive = true });
            db.UserAccounts.Add(new UserAccount { Id = membership, UserId = actorId, AccountId = account });
            db.Buildings.AddRange(new Building { Id = building, AccountId = account, Name = "Local", CountryCode = "NO" }, new Building { Id = outside, AccountId = account, Name = "Outside scope", CountryCode = "NO" });
            db.Units.AddRange(new Unit { Id = root, AccountId = account, BuildingId = building, Name = "Root" },
                new Unit { Id = child, AccountId = account, BuildingId = building, ParentUnitId = root, Name = "Child" },
                new Unit { Id = third, AccountId = account, BuildingId = building, Name = "Historical" },
                new Unit { Id = outsideUnit, AccountId = account, BuildingId = outside, Name = "Outside" });
            db.Organizations.AddRange(new Organization { Id = tenant, AccountId = account, Name = "Tenant", Type = OrganizationType.Tenant, IsActive = true },
                new Organization { Id = foreignTenant, AccountId = other, Name = "Foreign tenant", Type = OrganizationType.Tenant, IsActive = true });
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership, Role = UserRole.PropertyAdmin, BuildingId = building });
            db.Occupancies.AddRange(new Occupancy { Id = occurrence, AccountId = account, UnitId = root, TenantOrganizationId = tenant, ValidFrom = today, ValidTo = today.AddDays(10) },
                new Occupancy { Id = inaccessible, AccountId = account, UnitId = outsideUnit, TenantOrganizationId = tenant, ValidFrom = today });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, actorId, account));
        var service = new OccupancyService(new AuthorizationService(actor, new AuthorizationData(factory), clock), new OccupancyData(factory), clock);
        guard.Protect = true;
        try
        {
            Check((await service.GetOccupanciesAsync(account)).Count == 2, "legacy broad administrative list is explicit");
            Check((await service.GetOccupancyAsync(account, occurrence))?.UnitId == root, "concrete scoped detail succeeds");
            await Denied(() => service.GetOccupancyAsync(account, inaccessible), "list permission cannot read unrelated resource");
            await Denied(() => service.GetOccupanciesAsync(other), "foreign account list denied");
            await Denied(() => service.GetHierarchyConflictsAsync(account, outsideUnit, today, null), "conflict metadata requires concrete unit scope");
            Check((await service.GetHierarchyConflictsAsync(account, child, today, today.AddDays(2))).Single().ConflictType == OccupancyHierarchyConflictType.ParentUnit,
                "pure hierarchy warning preserves parent overlap without creating data");
        }
        finally { guard.Protect = false; }
        if (provider == "PostgreSQL")
        {
            await Invalid(() => service.CreateOccupancyAsync(account, new() { UnitId = child, TenantOrganizationId = foreignTenant, ValidFrom = today }), "OccupancyTenantOrganizationNotFound");
            await Denied(() => service.CreateOccupancyAsync(account, new() { UnitId = outsideUnit, TenantOrganizationId = tenant, ValidFrom = today }), "create requires actual target unit authority");
            async Task<Guid?> Create()
            {
                try { return await service.CreateOccupancyAsync(account, new() { UnitId = child, TenantOrganizationId = tenant, ValidFrom = today, ValidTo = today.AddDays(5) }); }
                catch (OccupancyValidationException ex) when (ex.Message == "OccupancyOverlapsExisting") { return null; }
            }
            var ids = await Task.WhenAll(Create(), Create());
            Check(ids.Count(x => x.HasValue) == 1, "concurrent overlapping occupancy attempts serialize before validation/commit");
            var id = ids.Single(x => x.HasValue)!.Value;
            await Denied(() => service.UpdateOccupancyAsync(account, id, new() { UnitId = outsideUnit, TenantOrganizationId = tenant, ValidFrom = today }), "update cannot move into unauthorized building");
            await service.EndOccupancyAsync(account, id, today.AddDays(1));
            Check((await service.GetOccupancyAsync(account, id))?.ValidTo == today.AddDays(1), "authorized end persists within transaction");
            await service.DeleteOccupancyAsync(account, id);
            var historical = await service.CreateOccupancyAsync(account, new() { UnitId = third, TenantOrganizationId = tenant, ValidFrom = today.AddDays(-10), ValidTo = today.AddDays(-2) });
            Check(!(await service.CanDeleteOccupancyAsync(account, historical)).CanDelete, "historical occupancy deletion rule preserved in Application");
            try { await service.DeleteOccupancyAsync(account, historical); throw new Exception("Expected historical deletion rejection"); }
            catch (OccupancyDeleteNotAllowedException) { Check(true, "actual delete repeats historical rule under lock"); }
        }
        await using (var db = factory.CreateDbContext())
        { db.UserAccountRoles.RemoveRange(await db.UserAccountRoles.Where(x => x.UserAccountId == membership).ToListAsync()); await db.SaveChangesAsync(); }
        await Denied(() => service.GetOccupanciesAsync(account), "revoked property role applies to reused service");
    }
}
