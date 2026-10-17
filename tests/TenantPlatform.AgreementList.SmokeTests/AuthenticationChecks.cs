using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;

static class AuthenticationChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL authentication boundary: " + name); Console.WriteLine($"PASS {provider} authentication boundary: {name}"); }
        async Task Denied(Func<Task> action, string name) { try { await action(); throw new Exception("Expected denial: " + name); } catch (UnauthorizedAccessException) { Check(true, name); } }
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var user = Guid.NewGuid(); var platform = Guid.NewGuid(); var inactive = Guid.NewGuid();
        var membershipA = Guid.NewGuid(); var membershipB = Guid.NewGuid(); var password = "Test-only-password-123!"; var hasher = new PasswordService();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = a, Name = "Login A" }, new Account { Id = b, Name = "Login B" }, new Account { Id = c, Name = "Login C" });
            foreach (var id in new[] { user, platform, inactive })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = "Login", IsActive = id != inactive, IsPlatformAdmin = id == platform });
                var login = new LoginAccount { Id = Guid.NewGuid(), UserId = id, Email = id + "@example.test", IsEnabled = true };
                login.PasswordHash = hasher.HashPassword(login, password); db.LoginAccounts.Add(login);
            }
            db.UserAccounts.AddRange(new UserAccount { Id = membershipA, UserId = user, AccountId = a }, new UserAccount { Id = membershipB, UserId = user, AccountId = b });
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membershipA, Role = UserRole.AccountAdmin });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, user, a)); var authorization = new AuthorizationData(factory);
        var service = new AuthenticationSessionService(new LocalAuthenticationService(factory, hasher), new TenantPlatform.Infrastructure.Authentication.AuthenticationSessionData(factory), actor, authorization);
        var navigation = new NavigationReader(actor, authorization);
        guard.Protect = true;
        try
        {
            var menu = await navigation.ReadAsync();
            Check(menu is { IsAuthenticated: true, IsPlatformAdmin: false, Permissions.CanSeeAdministration: true } && menu.Roles.Contains(UserRole.AccountAdmin), "navigation reads fresh membership and roles without writes");
            actor.Current = new(true, platform, null);
            Check((await navigation.ReadAsync()) is { IsPlatformAdmin: true, Permissions.CanSeeAccounts: true, Permissions.CanSeeMeetingRooms: false }, "global platform navigation has no tenant access");
        }
        finally { guard.Protect = false; actor.Current = new(true, user, a); }
        Check((await service.LoginAsync(user + "@example.test", "wrong")).User is null, "invalid credential proof cannot reach a session");
        Check((await service.LoginAsync(inactive + "@example.test", password)).User is null, "inactive user cannot sign in despite a valid password");
        var multiple = await service.LoginAsync(user + "@example.test", password);
        Check(multiple is { User.Id: var signedIn, AccountId: null, SelectAccountRequired: true } && signedIn == user, "multiple accounts without remembered choice require selection");
        var global = await service.LoginAsync(platform + "@example.test", password);
        Check(global is { User.IsPlatformAdmin: true, AccountId: null, SelectAccountRequired: false }, "pure platform administrator can sign in without account membership");
        await Denied(() => service.SelectAccountAsync(c), "client-selected account requires membership");
        var chosen = await service.SelectAccountAsync(b);
        Check(chosen.User?.Id == user && chosen.AccountId == b, "account switch uses trusted actor and verified membership");
        Check((await service.LoginAsync(user + "@example.test", password)).AccountId == b, "remembered account is used only while membership exists");
        Check(await service.ChangeLanguageAsync("EN-gb") == "en-GB", "supported culture normalizes before persistence");
        await using (var db = factory.CreateDbContext())
        {
            Check((await db.Users.SingleAsync(x => x.Id == user)).PreferredLanguage == "en-GB" && (await db.Users.SingleAsync(x => x.Id == platform)).PreferredLanguage != "en-GB", "language updates only the trusted user's profile");
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == membershipB)); await db.SaveChangesAsync();
        }
        await Denied(() => service.SelectAccountAsync(b), "revoked membership rejects a reused selection service");
        Check((await service.LoginAsync(user + "@example.test", password)).AccountId == a, "stale last account is replaced by remaining authorized account");
        await using (var db = factory.CreateDbContext())
        { db.UserAccountRoles.RemoveRange(await db.UserAccountRoles.Where(x => x.UserAccountId == membershipA).ToListAsync()); await db.SaveChangesAsync(); }
        Check(!(await navigation.ReadAsync()).Permissions.CanSeeAdministration, "navigation role revocation takes effect without circuit recreation");
        actor.Current = new(true, platform, null);
        await using (var db = factory.CreateDbContext())
        { (await db.Users.SingleAsync(x => x.Id == platform)).IsPlatformAdmin = false; await db.SaveChangesAsync(); }
        Check(!(await navigation.ReadAsync()).Permissions.CanSeeAccounts, "navigation ignores old global-admin cookie claims");
        actor.Current = new(true, inactive, a);
        await Denied(() => service.ChangeLanguageAsync("nb-NO"), "inactive profile preferences rejected");
        actor.Current = new(false, user, a);
        await Denied(() => service.ChangeLanguageAsync("nb-NO"), "anonymous preferences rejected");
        await Denied(() => service.SelectAccountAsync(a), "anonymous account selection rejected");
        Check(!(await navigation.ReadAsync()).IsAuthenticated, "anonymous navigation returns no protected capabilities");
    }
}
