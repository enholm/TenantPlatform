using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using TenantPlatform.Application.Buildings;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Buildings;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Mcp;

static class BuildingReportingChecks
{
    public static async Task Run(Factory admin, string runtime, string password, Func<Guid, bool, Task<McpClient>> connect)
    {
        var account = Guid.NewGuid(); var foreign = Guid.NewGuid();
        var administrator = Guid.NewGuid(); var property = Guid.NewGuid(); var member = Guid.NewGuid(); var platform = Guid.NewGuid();
        var scoped = Guid.NewGuid();
        await using (var db = admin.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "STR buildings" }, new Account { Id = foreign, Name = "STR foreign" });
            foreach (var id in new[] { administrator, property, member, platform })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", IsActive = true, IsPlatformAdmin = id == platform });
                var login = new LoginAccount { Id = Guid.NewGuid(), UserId = id, Email = id + "@example.test", CreatedUtc = DateTimeOffset.UtcNow };
                login.PasswordHash = new PasswordService().HashPassword(login, password); db.LoginAccounts.Add(login);
                if (id == platform) continue;
                var membership = new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = account }; db.UserAccounts.Add(membership);
                if (id != member) db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id,
                    Role = id == property ? UserRole.PropertyAdmin : UserRole.AccountAdmin, BuildingId = id == property ? scoped : null });
            }
            for (var i = 0; i < 29; i++) db.Buildings.Add(new Building { Id = i == 0 ? scoped : Guid.NewGuid(), AccountId = account,
                Name = i < 27 ? "Same name" : "Unique search", City = (i % 6) switch { 0 => null, 1 => "", 2 => " ", 3 => "Oslo", 4 => "oslo", _ => " Oslo " }, AddressLine1 = i == 0 ? null : "Road", PostalCode = "0001" });
            db.Buildings.Add(new Building { Id = Guid.NewGuid(), AccountId = foreign, Name = "FOREIGN_SECRET" });
            await db.SaveChangesAsync();
        }
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        BuildingReportingReader Reader(Guid user, Guid? selected = null, IBuildingReportingData? data = null) => new(
            new AuthorizationService(new FrozenActor(user, selected ?? account), new AuthorizationData(guarded), TimeProvider.System), data ?? new BuildingReportingData(guarded));
        var reader = Reader(administrator);
        var first = await reader.ListAsync(account, new()); var second = await reader.ListAsync(account, new(), 2);
        Check(first.TotalCount == 29 && first.Items.Length == 25 && first.HasMore && first.NextPage == 2 && !first.Complete && !first.SnapshotConsistent, "SQL page 1 and real total");
        Check(second.Items.Length == 4 && !second.HasMore && second.NextPage == null && !second.Complete && !second.LimitReached, "last page is not full data set");
        Check(first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count() == 29 && first.Items.Select(x => x.Id).SequenceEqual((await reader.ListAsync(account, new())).Items.Select(x => x.Id)), "equal names have stable ID tie-break and no duplicate pages");
        Check((await reader.ListAsync(account, new(), 999)).Page == 2, "requested page clamps to actual page");
        Check((await Reader(property).ListAsync(account, new())).TotalCount == 29, "PropertyAdmin list exception covers all account buildings");
        var detail = new BuildingService(new AuthorizationService(new FrozenActor(property, account), new AuthorizationData(admin), TimeProvider.System), new BuildingData(admin));
        await Throws<UnauthorizedAccessException>(() => detail.GetBuildingAsync(account, first.Items.First(x => x.Id != scoped).Id));
        foreach (var subject in new[] { member, platform }) await Throws<UnauthorizedAccessException>(() => Reader(subject).ListAsync(account, new()));
        await Throws<UnauthorizedAccessException>(() => Reader(administrator, foreign).ListAsync(foreign, new()));
        await Throws<UnauthorizedAccessException>(() => reader.ListAsync(foreign, new()));
        var empty = await reader.ListAsync(account, new("NO_MATCH")); Check(empty.TotalCount == 0 && empty.Complete, "authorized empty is successful");
        foreach (var filter in new[] { new BuildingReportFilter(), new("  UNIQUE SEARCH  "), new("same"), new("NO_MATCH") })
        {
            var list = await reader.ListAsync(account, filter); var count = await reader.CountAsync(account, filter); var grouped = await reader.CountAsync(account, filter, true);
            Check(list.TotalCount == count.TotalCount && count.Groups.Length == 0 && grouped.Groups.Sum(g => g.Count) == list.TotalCount, "shared SQL filter before every total/group");
        }
        var cities = await reader.CountAsync(account, new(), true);
        Check(cities.Groups.Length == 5 && cities.Groups.Single(g => g.Key == "missing").Count == 10 &&
            cities.Groups.Any(g => g.Name == " ") && cities.Groups.Any(g => g.Name == "Oslo") && cities.Groups.Any(g => g.Name == "oslo") && cities.Groups.Any(g => g.Name == " Oslo "), "only null/empty collapse; case and whitespace exact");
        foreach (var search in new[] { "\n", new string('x', 201), "bad\tvalue" }) await Throws<ArgumentException>(() => reader.ListAsync(account, new(search)));
        foreach (var page in new[] { 0, 10001 }) await Throws<ArgumentException>(() => reader.ListAsync(account, new(), page));
        using (var cts = new CancellationTokenSource()) { cts.Cancel(); await Throws<OperationCanceledException>(() => reader.CountAsync(account, new(), ct: cts.Token)); }
        await using var client = await connect(administrator, true);
        await using var noScope = await connect(administrator, false);
        Check((await client.ListToolsAsync()).Count == 22 && !(await noScope.ListToolsAsync()).Any(t => t.Name is "buildings_query" or "buildings_aggregate"), "structure discovery requires personally granted scope and legacy clients retain twenty tools");
        foreach (var name in new[] { "buildings_query", "buildings_aggregate" })
        {
            var args = new Dictionary<string, object?> { ["accountId"] = account };
            var result = await client.CallToolAsync(name, args);
            var direct = name == "buildings_query" ? JsonSerializer.SerializeToElement(first, new JsonSerializerOptions(JsonSerializerDefaults.Web)) : JsonSerializer.SerializeToElement(await reader.CountAsync(account, new()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(result.IsError != true && result.StructuredContent!.Value.GetRawText() == direct.GetRawText(), "actual OAuth MCP matches direct Application " + name);
            Check(Error(await noScope.CallToolAsync(name, args)) == "AUTHORIZATION_REQUIRED", "agreement scope cannot access buildings");
            args["userId"] = member; Check(Error(await client.CallToolAsync(name, args)) == "INVALID_ARGUMENT", "unknown argument/identity rejected");
            args.Remove("userId"); args["accountId"] = foreign; Check(Error(await client.CallToolAsync(name, args)) == "RESOURCE_UNAVAILABLE", "foreign account safe error");
            args["accountId"] = Guid.NewGuid(); Check(Error(await client.CallToolAsync(name, args)) == "RESOURCE_UNAVAILABLE", "unknown account same error");
        }
        var wireCities = await client.CallToolAsync("buildings_aggregate", new Dictionary<string, object?> { ["accountId"] = account, ["groupBy"] = "City" });
        Check(wireCities.StructuredContent!.Value.GetProperty("groups").GetArrayLength() == 5, "exact City groups through MCP");
        foreach (var bad in new Dictionary<string, object?>[] { new() { ["page"] = 0 }, new() { ["page"] = 10001 }, new() { ["search"] = "\n" }, new() { ["search"] = new string('x',201) } }) {
            bad["accountId"] = account; Check(Error(await client.CallToolAsync("buildings_query", bad)) == "INVALID_ARGUMENT", "wire bounds/search validation"); }
        Check(Error(await client.CallToolAsync("buildings_aggregate", new Dictionary<string, object?> { ["accountId"] = account, ["groupBy"] = "city" })) == "INVALID_ARGUMENT", "exact grouping enum");
        await using (var db = admin.CreateDbContext()) { await db.Users.Where(u => u.Id == administrator).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)); }
        await Throws<UnauthorizedAccessException>(() => reader.ListAsync(account, new()));
        Check(Error(await client.CallToolAsync("buildings_query", new Dictionary<string,object?> { ["accountId"] = account })) == "RESOURCE_UNAVAILABLE", "same OAuth session observes deactivation");
        await using (var db = admin.CreateDbContext()) { await db.Users.Where(u => u.Id == administrator).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, true));
            for (var i = 0; i < 101; i++) db.Buildings.Add(new Building { Id = Guid.NewGuid(), AccountId = account, Name = "OverflowSTR", City = i.ToString("D3") }); await db.SaveChangesAsync(); }
        await Throws<BuildingReportLimitException>(() => reader.CountAsync(account, new("OverflowSTR"), true));
        var overflow = await client.CallToolAsync("buildings_aggregate", new Dictionary<string,object?> { ["accountId"] = account, ["search"] = "OverflowSTR", ["groupBy"] = "City" });
        Check(Error(overflow) == "RESULT_TOO_LARGE" && overflow.StructuredContent == null, "101 groups rejects whole result");
        Check((await reader.CountAsync(account, new("OverflowSTR"))).TotalCount == 101, "ungrouped total not limited by city cardinality");
        await using (var sql = new NpgsqlConnection(runtime)) { await sql.OpenAsync();
            foreach (var statement in new[] { "SELECT \"CountryCode\" FROM buildings", "UPDATE buildings SET \"Name\"='bad'", "INSERT INTO buildings (\"Id\") VALUES (gen_random_uuid())" }) {
                try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("unexpected privilege: " + statement); }
                catch (PostgresException e) when (e.SqlState == "42501") { } } }
        await using (var db = guarded.CreateDbContext()) await Throws<InvalidOperationException>(() => db.SaveChangesAsync());
        var hooked = new AfterBuildingRead(new BuildingReportingData(guarded), async () => { await using var db = admin.CreateDbContext(); await db.UserAccountRoles.Where(r => r.UserAccount.UserId == property).ExecuteDeleteAsync(); });
        await Throws<UnauthorizedAccessException>(() => Reader(property, data: hooked).ListAsync(account, new()));
        await using (var db = admin.CreateDbContext()) { await db.UserAccountRoles.Where(r => r.UserAccount.UserId == administrator && r.UserAccount.AccountId == account).ExecuteDeleteAsync(); await db.UserAccounts.Where(m => m.UserId == administrator && m.AccountId == account).ExecuteDeleteAsync(); }
        await Throws<UnauthorizedAccessException>(() => reader.CountAsync(account, new()));
        Check(Error(await client.CallToolAsync("buildings_query", new Dictionary<string,object?> { ["accountId"] = account })) == "RESOURCE_UNAVAILABLE", "membership revocation with live MCP session");
        Console.WriteLine("PASS STR-001: authorized building query/aggregate, exact SQL groups, minimal columns, actual OAuth/MCP and revocation");
    }
    private static string? Error(CallToolResult r) => r.IsError == true ? r.Content.OfType<TextContentBlock>().Single().Text : null;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class AfterBuildingRead(IBuildingReportingData inner, Func<Task> after) : IBuildingReportingData
    {
        public async Task<BuildingReportPage> ListAsync(BuildingReportScope scope, BuildingReportFilter filter, int page, CancellationToken ct) { var r = await inner.ListAsync(scope, filter, page, ct); await after(); return r; }
        public async Task<BuildingReportCounts> CountAsync(BuildingReportScope scope, BuildingReportFilter filter, bool city, int max, CancellationToken ct) { var r = await inner.CountAsync(scope, filter, city, max, ct); await after(); return r; }
    }
}
