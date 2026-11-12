using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Dimensions;

// PostgreSQL-only runner. Never executed by the offline Unified Dimensions phases.
var connection = Environment.GetEnvironmentVariable("ACCOUNT_REGISTER_TEST_CONNECTION") ?? throw new Exception("Set ACCOUNT_REGISTER_TEST_CONNECTION.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_account_register_tests") throw new Exception("Only the disposable tenant_account_register_tests database is allowed.");
var schema = "register_test_" + Guid.NewGuid().ToString("N");
Console.WriteLine($"Test database: {cs.Database}");
Console.WriteLine($"Test schema: {schema}");
await using var setup = new NpgsqlConnection(connection);
await setup.OpenAsync();
await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await cmd.ExecuteNonQueryAsync();
try
{
    cs.SearchPath = schema;
    var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString).Options);
    // Bounded historical scenario: stop at the rename migration and query unmapped rows, not removed CLR entities.
    await using (var db = factory.CreateDbContext())
    {
        Assert(!db.Database.HasPendingModelChanges(), "final model matches snapshot");
        var old = db.Database.GetMigrations().Single(x => x.EndsWith("_AddAccountDepartmentsAndLocations"));
        var rename = db.Database.GetMigrations().Single(x => x.EndsWith("_AddAccountRegisterHierarchies"));
        await db.GetService<IMigrator>().MigrateAsync(old);
        var account = Guid.NewGuid(); var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO accounts (\"Id\", \"Name\", \"DefaultLanguage\", \"IsActive\") VALUES ({account}, 'Historical account', 'nb-NO', true)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO departments (\"Id\", \"AccountId\", \"Name\", \"Description\", \"IsActive\") VALUES ({id}, {account}, 'Historical department', 'Preserved', false)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO locations (\"Id\", \"AccountId\", \"Name\", \"Description\", \"IsActive\") VALUES ({id}, {account}, 'Historical location', 'Preserved', true)");
        await db.GetService<IMigrator>().MigrateAsync(rename);
        var department = await db.Database.SqlQueryRaw<HistoricalRegisterRow>("SELECT \"Id\", \"ParentId\", \"Description\", \"IsActive\" FROM organization_elements").SingleAsync();
        var location = await db.Database.SqlQueryRaw<HistoricalRegisterRow>("SELECT \"Id\", \"ParentId\", \"Description\", \"IsActive\" FROM geographic_areas").SingleAsync();
        Assert(department.Id == id && department.ParentId is null && department.Description == "Preserved" && !department.IsActive, "historical rename preserves department row at historical boundary");
        Assert(location.Id == id && location.ParentId is null && location.Description == "Preserved" && location.IsActive, "historical rename preserves location row at historical boundary");
        await db.Database.MigrateAsync();
        Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies through intentional legacy drop");
    }
    var accountA = Guid.NewGuid(); var accountB = Guid.NewGuid(); var admin = Guid.NewGuid(); var member = Guid.NewGuid(); var foreign = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    {
        db.Accounts.AddRange(new Account { Id = accountA, Name = "A" }, new Account { Id = accountB, Name = "B" });
        foreach (var id in new[] { admin, member, foreign })
        {
            db.Users.Add(new User { Id = id, Email = id + "@test", IsActive = true });
            var membership = new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = id == foreign ? accountB : accountA };
            db.UserAccounts.Add(membership);
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = id == member ? UserRole.TenantUser : UserRole.AccountAdmin });
        }
        await db.SaveChangesAsync();
    }
    var context = new Context(admin, accountA);
    var auth = new TenantAuthorizationService(factory, context);
    var service = new DimensionService(factory, context, auth, TimeProvider.System);
    var dimension = await service.SaveDimensionAsync(null, new() { Name = "  Classification  ", Code = " class ", LeafOnly = false });
    var definition = (await service.GetAsync()).Dimensions.Single();
    Assert(definition.Name == "Classification" && definition.Code == "CLASS" && definition.AccountId == accountA, "current Dimensions create normalizes and owns data");
    var stale = System.Text.Json.JsonSerializer.Deserialize<Dimension>(System.Text.Json.JsonSerializer.Serialize(definition))!;
    definition.IsActive = false; await service.SaveDimensionAsync(dimension, definition);
    await Expect<DimensionValidationException>(() => service.SaveDimensionAsync(dimension, stale), "stale dimension revision rejected");
    definition = (await service.GetAsync()).Dimensions.Single(); definition.IsActive = true; await service.SaveDimensionAsync(dimension, definition);
    foreach (var input in new[] { new Dimension { Name = " ", Code = "BAD" }, new() { Name = new string('x', 201), Code = "BAD" }, new() { Name = "Valid", Code = "BAD", Description = new string('x', 2001) } })
        await Expect<DimensionValidationException>(() => service.SaveDimensionAsync(null, input), "current register validates bounds");
    var root = await service.SaveValueAsync(dimension, null, new() { DimensionId = dimension, Name = "Root", Code = "ROOT" });
    var child = await service.SaveValueAsync(dimension, null, new() { DimensionId = dimension, ParentId = root, Name = "Child", Code = "CHILD" });
    var leaf = await service.SaveValueAsync(dimension, null, new() { DimensionId = dimension, ParentId = child, Name = "Leaf", Code = "LEAF" });
    async Task Move(Guid id, Guid? parent) { var value = (await service.GetAsync()).Values.Single(x => x.Id == id); value.ParentId = parent; await service.SaveValueAsync(dimension, id, value); }
    await Expect<DimensionValidationException>(() => Move(root, leaf), "descendant cycle rejected");
    await Expect<DimensionValidationException>(() => Move(child, child), "self cycle rejected");
    await Expect<DimensionValidationException>(() => Move(child, Guid.NewGuid()), "missing parent rejected");
    await Move(child, null);
    Assert((await service.GetAsync()).Values.Single(x => x.Id == leaf).ParentId == child, "moving subtree retains descendants");
    var foreignDimension = Guid.NewGuid(); var foreignValue = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    {
        db.Dimensions.Add(new Dimension { Id = foreignDimension, AccountId = accountB, Name = "Foreign", Code = "FOREIGN" });
        db.DimensionValues.Add(new DimensionValue { Id = foreignValue, AccountId = accountB, DimensionId = foreignDimension, Name = "Foreign parent", Code = "FOREIGN" });
        await db.SaveChangesAsync();
    }
    await Expect<DimensionValidationException>(() => Move(child, foreignValue), "foreign parent rejected by current register");
    context.Current = new() { IsAuthenticated = true, UserId = foreign, CurrentAccountId = accountB };
    Assert((await service.GetAsync()).Dimensions.All(x => x.AccountId == accountB), "register listing isolates account");
    await Expect<UnauthorizedAccessException>(() => service.SaveDimensionAsync(dimension, definition), "cross-account edit rejected");
    foreach (var identity in new CurrentUserContext[]
    {
        new() { IsAuthenticated = true, UserId = member, CurrentAccountId = accountA },
        new() { IsAuthenticated = true, IsPlatformAdmin = true, UserId = member, CurrentAccountId = accountA },
        new() { IsAuthenticated = true, UserId = admin, CurrentAccountId = accountB },
        new() { IsAuthenticated = true, UserId = admin }, new() { UserId = admin, CurrentAccountId = accountA }
    })
    {
        context.Current = identity;
        await Expect<UnauthorizedAccessException>(() => service.GetAsync(), "non-admin/forged/missing/anonymous context cannot read administrative register");
        await Expect<UnauthorizedAccessException>(() => service.SaveDimensionAsync(null, new() { Name = "Attack", Code = "ATTACK" }), "unauthorized create rejected");
        await Expect<UnauthorizedAccessException>(() => service.SaveDimensionAsync(dimension, definition), "unauthorized edit rejected");
        await Expect<UnauthorizedAccessException>(() => service.SaveValueAsync(dimension, child, new() { DimensionId = dimension, Name = "Attack", Code = "ATTACK" }), "unauthorized value edit rejected");
    }
    context.Current = new() { IsAuthenticated = true, UserId = admin, CurrentAccountId = accountA };
    await Move(child, null); await Move(root, null);
    async Task<bool> Attempt(Guid id, Guid parent) { try { await Move(id, parent); return true; } catch (DimensionValidationException) { return false; } }
    var results = await Task.WhenAll(Attempt(root, child), Attempt(child, root));
    Assert(results.Count(x => x) == 1, "serialized current-register reparenting prevents concurrent cycle");
    await using (var db = factory.CreateDbContext())
    {
        db.DimensionValues.Add(new DimensionValue { Id = Guid.NewGuid(), AccountId = accountA, DimensionId = dimension, ParentId = foreignValue, Name = "Invalid", Code = "INVALID" });
        await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "account/dimension-aware FK rejects foreign parent outside service");
    }
    var isolated = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    { db.Accounts.Add(new Account { Id = isolated }); db.Dimensions.Add(new Dimension { Id = Guid.NewGuid(), AccountId = isolated, Name = "Data", Code = "DATA" }); await db.SaveChangesAsync(); }
    Assert(!(await new TenantPlatform.Web.Services.Accounts.AccountService(factory).CanDeleteAccountAsync(isolated)).CanDelete, "dimension-only account without users cannot be deleted");
    Assert((await auth.GetNavigationPermissionsAsync()).CanManageAccountStructure, "admin navigation permission preserved");
    context.Current = new() { IsAuthenticated = true, UserId = member, CurrentAccountId = accountA };
    Assert(!(await auth.GetNavigationPermissionsAsync()).CanManageAccountStructure, "member cannot see register administration");
}
catch
{
    Console.Error.WriteLine($"Test failed; schema preserved for diagnosis: {schema}");
    throw;
}
await using (var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup))
    await drop.ExecuteNonQueryAsync();
Console.WriteLine($"Test schema dropped: {schema}");
Console.WriteLine("All account-register PostgreSQL smoke checks passed.");
static void Assert(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
static async Task Expect<T>(Func<Task> action, string label) where T : Exception
{ try { await action(); } catch (T) { Assert(true, label); return; } throw new Exception("FAIL: " + label); }
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{ public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class Context(Guid user, Guid account) : ICurrentUserContextService
{ public CurrentUserContext Current { get; set; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account }; }
public sealed class HistoricalRegisterRow
{ public Guid Id { get; set; } public Guid? ParentId { get; set; } public string? Description { get; set; } public bool IsActive { get; set; } }
