using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.AccountSettings;

var connection = Environment.GetEnvironmentVariable("ACCOUNT_REGISTER_TEST_CONNECTION") ?? throw new Exception("Set ACCOUNT_REGISTER_TEST_CONNECTION.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_account_register_tests") throw new Exception("Only the disposable tenant_account_register_tests database is allowed.");
var schema = "register_test_" + Guid.NewGuid().ToString("N");
await using var setup = new NpgsqlConnection(connection);
await setup.OpenAsync();
await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await cmd.ExecuteNonQueryAsync();
try
{
    cs.SearchPath = schema;
    var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString).Options);
    await using (var db = factory.CreateDbContext())
    {
        Assert(!db.Database.HasPendingModelChanges(), "model matches snapshot");
        await db.Database.MigrateAsync();
        Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies");
    }
    var account = Guid.NewGuid(); var otherAccount = Guid.NewGuid();
    var admin = Guid.NewGuid(); var member = Guid.NewGuid(); var foreign = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    {
        db.Accounts.AddRange(new Account { Id = account, Name = "A" }, new Account { Id = otherAccount, Name = "B" });
        foreach (var id in new[] { admin, member, foreign })
        {
            db.Users.Add(new User { Id = id, FirstName = "Test", Email = $"{id}@example.test" });
            var membership = new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = id == foreign ? otherAccount : account };
            db.UserAccounts.Add(membership);
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = id == member ? UserRole.TenantUser : UserRole.AccountAdmin });
        }
        await db.SaveChangesAsync();
    }
    foreach (var location in new[] { false, true })
    {
        var context = new Context(admin, account);
        var auth = new TenantAuthorizationService(factory, context);
        var departments = new DepartmentService(factory, context, auth);
        var locations = new LocationService(factory, context, auth);
        Task Save(Guid? id, AccountRegisterInput input) => location ? locations.SaveAsync(id, input) : departments.SaveAsync(id, input);
        Task Delete(Guid id) => location ? locations.DeleteAsync(id) : departments.DeleteAsync(id);
        async Task<List<(Guid Id, Guid AccountId, string Name, string? Description, bool IsActive)>> Read() => location
            ? (await locations.GetAllAsync()).Select(x => (x.Id, x.AccountId, x.Name, x.Description, x.IsActive)).ToList()
            : (await departments.GetAllAsync()).Select(x => (x.Id, x.AccountId, x.Name, x.Description, x.IsActive)).ToList();
        var label = location ? "locations" : "departments";
        await Save(null, new() { Name = "  Operations  ", Description = "  Description  " });
        var item = (await Read()).Single();
        Assert(item.AccountId == account && item.Name == "Operations" && item.Description == "Description" && item.IsActive, label + ": create and normalize");
        await Save(item.Id, new() { Name = "Updated", Description = "  ", IsActive = false });
        var updated = (await Read()).Single();
        Assert(updated.Name == "Updated" && updated.Description == null && !updated.IsActive, label + ": edit and deactivate");
        foreach (var input in new[] { new AccountRegisterInput { Name = " " }, new() { Name = new string('x', 201) }, new() { Name = "Valid", Description = new string('x', 2001) } })
            await Expect<ArgumentException>(() => Save(null, input), label + ": invalid input rejected");
        context.Current = new() { IsAuthenticated = true, UserId = foreign, CurrentAccountId = otherAccount };
        Assert((await Read()).Count == 0, label + ": other account cannot list records");
        await Expect<InvalidOperationException>(() => Save(item.Id, new() { Name = "Attack" }), label + ": cross-account edit rejected");
        await Expect<InvalidOperationException>(() => Delete(item.Id), label + ": cross-account delete rejected");
        await Save(null, new() { Name = "Other account" });
        context.Current = new() { IsAuthenticated = true, UserId = member, CurrentAccountId = account };
        await Expect<UnauthorizedAccessException>(async () => { await Read(); }, label + ": non-admin read rejected");
        await Expect<UnauthorizedAccessException>(() => Save(null, new() { Name = "Attack" }), label + ": non-admin create rejected");
        await Expect<UnauthorizedAccessException>(() => Save(item.Id, new() { Name = "Attack" }), label + ": non-admin edit rejected");
        await Expect<UnauthorizedAccessException>(() => Delete(item.Id), label + ": non-admin delete rejected");
        context.Current = new() { IsAuthenticated = true, IsPlatformAdmin = true, UserId = Guid.NewGuid(), CurrentAccountId = account };
        await Expect<UnauthorizedAccessException>(async () => { await Read(); }, label + ": platform flag does not grant account maintenance");
        context.Current = new() { IsAuthenticated = true, UserId = admin, CurrentAccountId = otherAccount };
        await Expect<UnauthorizedAccessException>(async () => { await Read(); }, label + ": forged account context rejected");
        context.Current = new() { IsAuthenticated = true, UserId = admin };
        await Expect<UnauthorizedAccessException>(async () => { await Read(); }, label + ": missing account rejected");
        context.Current = new() { UserId = admin, CurrentAccountId = account };
        await Expect<UnauthorizedAccessException>(async () => { await Read(); }, label + ": anonymous access rejected");
        context.Current = new() { IsAuthenticated = true, UserId = admin, CurrentAccountId = account };
        Assert((await Read()).Single().Name == "Updated", label + ": foreign mutations left record unchanged");
        await Delete(item.Id);
        Assert((await Read()).Count == 0, label + ": delete");
        await Expect<InvalidOperationException>(() => Delete(item.Id), label + ": stale ID rejected");
    }
    // Even an account with no remaining users cannot be deleted while register data remains.
    foreach (var location in new[] { false, true })
    {
        var isolatedAccount = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id = isolatedAccount, Name = "Register-only account" });
            if (location) db.Locations.Add(new Location { Id = Guid.NewGuid(), AccountId = isolatedAccount, Name = "Office" });
            else db.Departments.Add(new Department { Id = Guid.NewGuid(), AccountId = isolatedAccount, Name = "IT" });
            await db.SaveChangesAsync();
        }
        var accountService = new TenantPlatform.Web.Services.Accounts.AccountService(factory);
        Assert(!(await accountService.CanDeleteAccountAsync(isolatedAccount)).CanDelete,
            location ? "locations prevent account deletion" : "departments prevent account deletion");
    }
    var adminContext = new Context(admin, account);
    var memberContext = new Context(member, account);
    Assert((await new TenantAuthorizationService(factory, adminContext).GetNavigationPermissionsAsync()).CanManageAccountStructure, "admin menu permission");
    Assert(!(await new TenantAuthorizationService(factory, memberContext).GetNavigationPermissionsAsync()).CanManageAccountStructure, "member menu permission hidden");
}
finally
{
    await using var cmd = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup);
    await cmd.ExecuteNonQueryAsync();
}
static void Assert(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
static async Task Expect<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine("PASS: " + label); return; }
    throw new Exception("FAIL: " + label);
}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{
    public TenantPlatformDbContext CreateDbContext() => new(options);
}
sealed class Context(Guid user, Guid account) : ICurrentUserContextService
{
    public CurrentUserContext Current { get; set; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account };
}
