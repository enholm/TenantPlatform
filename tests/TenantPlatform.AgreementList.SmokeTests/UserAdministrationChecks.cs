using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.UserAdministration;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.UserAdministration;

static class UserAdministrationChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool value, string name)
        { if (!value) throw new Exception("FAIL user administration: " + name); Console.WriteLine($"PASS {provider} user administration: {name}"); }
        async Task Denied(Func<Task> action, string name)
        { try { await action(); throw new Exception("Expected denial: " + name); } catch (UnauthorizedAccessException) { Check(true, name); } }
        async Task Invalid(Func<Task> action, string key)
        { try { await action(); throw new Exception("Expected " + key); } catch (InvalidOperationException ex) when (ex.Message == key) { Check(true, key); } }
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var admin = Guid.NewGuid(); var platform = Guid.NewGuid();
        var member = Guid.NewGuid(); var foreign = Guid.NewGuid(); var adminRole = Guid.NewGuid();
        var building = Guid.NewGuid(); var foreignBuilding = Guid.NewGuid(); var foreignOrg = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = a, Name = "Users A" }, new Account { Id = b, Name = "Users B" });
            foreach (var id in new[] { admin, platform, member, foreign })
            {
                db.Users.Add(new User { Id = id, FirstName = "Users", LastName = id.ToString(), Email = id + "@example.test", IsActive = true, IsPlatformAdmin = id == platform });
                if (id == platform) continue;
                var membership = new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = id == foreign ? b : a };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = adminRole, UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Buildings.AddRange(new Building { Id = building, AccountId = a, Name = "Local", CountryCode = "NO" },
                new Building { Id = foreignBuilding, AccountId = b, Name = "Foreign", CountryCode = "NO" });
            db.Organizations.Add(new Organization { Id = foreignOrg, AccountId = b, Name = "Foreign" });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, admin, a));
        var service = new UserAdministrationService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System),
            new UserAdministrationData(factory), new PasswordService(), TimeProvider.System);
        guard.Protect = true;
        try
        {
            Check((await service.GetUsersAsync()).Select(x => x.Id).ToHashSet().SetEquals(new[] { admin, member }), "list is account-bounded before metadata projection");
            Check(await service.GetUserAsync(foreign) is null && (await service.GetUserAccessAsync(foreign)).Count == 0, "foreign user/access metadata absent");
            await Denied(() => service.GetAvailableOrganizationsAsync(b), "foreign selector account denied");
            actor.Current = new(true, member, a);
            await Denied(() => service.GetUsersAsync(), "member cannot list users");
            await Denied(() => service.UpdateUserAsync(admin, new()), "member cannot update user");
            actor.Current = new(true, admin, b);
            await Denied(() => service.GetUsersAsync(), "selected account requires membership and administration");
            actor.Current = new(true, platform, null);
            Check((await service.GetUsersAsync()).Any(x => x.Id == foreign), "fresh global platform authority works without selected tenant");
            actor.Current = new(false, platform, null);
            await Denied(() => service.GetUsersAsync(), "anonymous global identity denied");
        }
        finally { guard.Protect = false; actor.Current = new(true, admin, a); }
        if (provider == "PostgreSQL")
        {
            var created = await service.CreateUserAsync(new() { FirstName = "  New  ", LastName = "Member", Email = Guid.NewGuid() + "@example.test",
                PreferredLanguage = "nb-NO", AccountId = b, TemporaryPassword = "Only-a-test-password-123!" });
            var details = await service.GetUserAsync(created);
            Check(details is { FirstName: "New", IsPlatformAdmin: false, LocalLogin: not null } && details.Accounts.Single().AccountId == a,
                "account admin creation ignores foreign requested account and never grants platform authority");
            await service.UpdateUserAsync(created, new() { FirstName = "Updated", LastName = "Member", Email = details!.Email,
                PreferredLanguage = "en-GB", IsActive = true, IsPlatformAdmin = true, IsLocalLoginEnabled = false });
            Check((await service.GetUserAsync(created)) is { IsPlatformAdmin: false, LocalLogin.IsEnabled: false }, "account admin cannot promote global authority; login updates persist");
            await Denied(() => service.UpdateUserAsync(foreign, new() { FirstName = "Attack", LastName = "User", Email = "attack@example.test", PreferredLanguage = "nb-NO" }), "foreign profile write denied");
            await Invalid(() => service.AddUserRoleAsync(created, new() { AccountId = a, Role = UserRole.PropertyAdmin, BuildingId = foreignBuilding }), "BuildingNotFound");
            await Invalid(() => service.AddUserRoleAsync(created, new() { AccountId = a, Role = UserRole.TenantUser, OrganizationId = foreignOrg }), "OrganizationNotFound");
            async Task<bool> Add()
            {
                try { await service.AddUserRoleAsync(created, new() { AccountId = a, Role = UserRole.PropertyAdmin, BuildingId = building }); return true; }
                catch (InvalidOperationException ex) when (ex.Message == "UserRoleAlreadyExists") { return false; }
            }
            var results = await Task.WhenAll(Add(), Add());
            Check(results.Count(x => x) == 1, "concurrent duplicate role attempts preserve one assignment");
            var assignment = (await service.GetUserAccessAsync(created)).Single().Roles.Single();
            await service.RemoveUserRoleAsync(created, assignment.Id);
            await using (var db = factory.CreateDbContext())
                Check(!await db.UserAccounts.AnyAsync(x => x.AccountId == a && x.UserId == created), "last role and membership removed atomically");
            // Preserve the explicit existing account-admin invitation rule for an existing user.
            await service.AddUserRoleAsync(foreign, new() { AccountId = a, Role = UserRole.PropertyAdmin, BuildingId = building });
            Check((await service.GetUserAccessAsync(foreign)).Single().AccountId == a, "existing user can be added without exposing their other memberships");
        }
        await using (var db = factory.CreateDbContext())
        { db.UserAccountRoles.Remove(await db.UserAccountRoles.SingleAsync(x => x.Id == adminRole)); await db.SaveChangesAsync(); }
        await Denied(() => service.GetUsersAsync(), "revoked admin role takes effect on same service");
        actor.Current = new(true, platform, null);
        await using (var db = factory.CreateDbContext())
        { (await db.Users.SingleAsync(x => x.Id == platform)).IsPlatformAdmin = false; await db.SaveChangesAsync(); }
        await Denied(() => service.GetUsersAsync(), "revoked global platform flag takes effect without cookie renewal");
        await using (var db = factory.CreateDbContext())
        { var user = await db.Users.SingleAsync(x => x.Id == platform); user.IsPlatformAdmin = true; user.IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => service.GetUsersAsync(), "inactive platform user denied");
    }
}
