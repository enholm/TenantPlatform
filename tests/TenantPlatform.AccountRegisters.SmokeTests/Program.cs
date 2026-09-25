using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    var legacyAccount = Guid.NewGuid();
    var legacyId = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    {
        Assert(!db.Database.HasPendingModelChanges(), "model matches snapshot");
        var previousMigration = db.Database.GetMigrations().Single(x => x.EndsWith("_AddAccountDepartmentsAndLocations"));
        await db.GetService<IMigrator>().MigrateAsync(previousMigration);
        db.Accounts.Add(new Account { Id = legacyAccount, Name = "Existing account" });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO departments (\"Id\", \"AccountId\", \"Name\", \"Description\", \"IsActive\") VALUES ({legacyId}, {legacyAccount}, 'Legacy department', 'Preserved', false)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO locations (\"Id\", \"AccountId\", \"Name\", \"Description\", \"IsActive\") VALUES ({legacyId}, {legacyAccount}, 'Legacy location', 'Preserved', true)");
        await db.Database.MigrateAsync();
        var legacyElement = await db.OrganizationElements.AsNoTracking().SingleAsync(x => x.AccountId == legacyAccount);
        var legacyArea = await db.GeographicAreas.AsNoTracking().SingleAsync(x => x.AccountId == legacyAccount);
        Assert(legacyElement.Id == legacyId && legacyElement.ParentId == null && legacyElement.Description == "Preserved" && !legacyElement.IsActive, "existing department preserved as root");
        Assert(legacyArea.Id == legacyId && legacyArea.ParentId == null && legacyArea.Name == "Legacy location" && legacyArea.IsActive, "existing location preserved as root");
        Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies");
        foreach (var type in new[] { typeof(OrganizationElement), typeof(GeographicArea) })
        {
            var parentFk = db.Model.FindEntityType(type)!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == type);
            var expectedName = parentFk.GetConstraintName()!;
            await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON c.connamespace = n.oid WHERE n.nspname = @schema AND c.conname = @name", setup);
            check.Parameters.AddWithValue("schema", schema);
            check.Parameters.AddWithValue("name", expectedName);
            Assert((long)(await check.ExecuteScalarAsync())! == 1, type.Name + ": FK name matches EF model");
        }
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
        var departments = new OrganizationElementService(factory, context, auth);
        var locations = new GeographicAreaService(factory, context, auth);
        Task Save(Guid? id, AccountRegisterInput input) => location ? locations.SaveAsync(id, input) : departments.SaveAsync(id, input);
        Task Delete(Guid id) => location ? locations.DeleteAsync(id) : departments.DeleteAsync(id);
        async Task<List<(Guid Id, Guid AccountId, string Name, string? Description, bool IsActive)>> Read() => location
            ? (await locations.GetAllAsync()).Select(x => (x.Id, x.AccountId, x.Name, x.Description, x.IsActive)).ToList()
            : (await departments.GetAllAsync()).Select(x => (x.Id, x.AccountId, x.Name, x.Description, x.IsActive)).ToList();
        var label = location ? "geographic_areas" : "organization_elements";
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
        // A root, child and grandchild can be moved without losing descendants.
        await Save(null, new() { Name = "Child", ParentId = item.Id });
        var child = (await Read()).Single(x => x.Name == "Child");
        await Save(null, new() { Name = "Grandchild", ParentId = child.Id });
        var grandchild = (await Read()).Single(x => x.Name == "Grandchild");
        await Expect<AccountHierarchyException>(() => Save(item.Id, new() { Name = "Cycle", ParentId = grandchild.Id }), label + ": descendant cycle rejected");
        await Expect<AccountHierarchyException>(() => Save(child.Id, new() { Name = "Self", ParentId = child.Id }), label + ": self-parent rejected");
        await Expect<AccountHierarchyException>(() => Save(null, new() { Name = "Missing", ParentId = Guid.NewGuid() }), label + ": nonexistent parent rejected");
        await Expect<AccountHierarchyException>(() => Save(null, new() { Name = "Foreign", ParentId = legacyId }), label + ": foreign parent rejected");
        await Expect<AccountHierarchyException>(() => Delete(item.Id), label + ": cannot delete parent");
        await Save(child.Id, new() { Name = "Child moved to root" });
        var parentId = location
            ? (await locations.GetAllAsync()).Single(x => x.Id == child.Id).ParentId
            : (await departments.GetAllAsync()).Single(x => x.Id == child.Id).ParentId;
        Assert(parentId == null, label + ": move to root");
        var grandchildParent = location
            ? (await locations.GetAllAsync()).Single(x => x.Id == grandchild.Id).ParentId
            : (await departments.GetAllAsync()).Single(x => x.Id == grandchild.Id).ParentId;
        Assert(grandchildParent == child.Id, label + ": moving subtree preserves child links");
        await Delete(grandchild.Id);
        await Delete(child.Id);

        // Competing reparentings must not both pass validation and create a two-node cycle.
        await Save(null, new() { Name = "Concurrent root" });
        var concurrent = (await Read()).Single(x => x.Name == "Concurrent root");
        async Task<bool> AttemptMove(Guid id, Guid parent)
        {
            try { await Save(id, new() { Name = "Concurrent", ParentId = parent }); return true; }
            catch (AccountHierarchyException) { return false; }
        }
        var results = await Task.WhenAll(AttemptMove(item.Id, concurrent.Id), AttemptMove(concurrent.Id, item.Id));
        Assert(results.Count(x => x) == 1, label + ": concurrent cycle prevented");
        await Save(concurrent.Id, new() { Name = "Concurrent root" });
        await Save(item.Id, new() { Name = "Original root" });
        await Delete(concurrent.Id);
        // The composite FK also protects account boundaries outside the application service.
        await using (var db = factory.CreateDbContext())
        {
            if (location) db.GeographicAreas.Add(new GeographicArea { Id = Guid.NewGuid(), AccountId = account, Name = "Invalid", ParentId = legacyId });
            else db.OrganizationElements.Add(new OrganizationElement { Id = Guid.NewGuid(), AccountId = account, Name = "Invalid", ParentId = legacyId });
            await Expect<DbUpdateException>(() => db.SaveChangesAsync(), label + ": database rejects foreign parent");
        }
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
            if (location) db.GeographicAreas.Add(new GeographicArea { Id = Guid.NewGuid(), AccountId = isolatedAccount, Name = "Office" });
            else db.OrganizationElements.Add(new OrganizationElement { Id = Guid.NewGuid(), AccountId = isolatedAccount, Name = "IT" });
            await db.SaveChangesAsync();
        }
        var accountService = new TenantPlatform.Web.Services.Accounts.AccountService(factory);
        Assert(!(await accountService.CanDeleteAccountAsync(isolatedAccount)).CanDelete,
            location ? "locations prevent account deletion" : "departments prevent account deletion");
    }
    var rootId = Guid.NewGuid(); var childId = Guid.NewGuid(); var leafId = Guid.NewGuid();
    var hierarchy = AccountRegisterHierarchy.Build(new[] {
        (leafId, (Guid?)childId, "Leaf"), (rootId, (Guid?)null, "Root"), (childId, (Guid?)rootId, "Child") });
    Assert(hierarchy.Select(x => x.Id).SequenceEqual(new[] { rootId, childId, leafId }), "tree order places descendants after parent");
    Assert(hierarchy[2].Depth == 2 && hierarchy[2].Path == "Root / Child / Leaf" && hierarchy[2].Ancestors.Contains(rootId), "tree depth, labels and parent exclusions");
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
