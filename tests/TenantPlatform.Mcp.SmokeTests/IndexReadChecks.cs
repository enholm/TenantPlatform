using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Mcp;

static class IndexReadChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid agreement, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        var auth = new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System);
        var reader = new AgreementIndexReader(auth, new AgreementIndexReadData(guarded));
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray(); var foreignIndex = Guid.NewGuid(); var moved = Guid.NewGuid(); var corrected = Guid.NewGuid();
        Guid revision; int audits;
        await using (var db = admin.CreateDbContext()) {
            revision = (await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision;
            for (var n = 0; n < ids.Length; n++) db.AgreementIndexRecords.Add(new() { Id = ids[n], AccountId = account, Code = $"INDEX_READ_{n:D2}", Name = "Ordinary index",
                Description = "Public business description", Source = n == 0 ? "https://statistics.example.test/private-secret/path?api_key=SECRET_QUERY#SECRET_FRAGMENT" : "PRIVATE_TRANSPORT_CONFIGURATION",
                Resolution = n == 1 ? AgreementIndexResolution.Quarter : n == 2 ? AgreementIndexResolution.Year : AgreementIndexResolution.Month, ActorUserId = user });
            db.AgreementIndexRecords.Add(new() { Id = foreignIndex, AccountId = foreign, Code = "FOREIGN", Name = "FOREIGN_SECRET", Resolution = AgreementIndexResolution.Month, ActorUserId = user });
            for (var n = 0; n < 30; n++) db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = account, IndexId = ids[0], Period = new DateOnly(2024, 1, 1).AddMonths(n),
                PeriodKey = n == 0 ? moved : n == 1 ? corrected : Guid.NewGuid(), Revision = 1, Superseded = n is 0 or 1, Value = 100 + n, PublishedDate = n == 1 ? null : new(2030, 1, 1), ActorUserId = user, Reason = "PRIVATE_REASON" });
            db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = account, IndexId = ids[0], Period = new(2027, 1, 1), PeriodKey = moved, Revision = 2, Value = 140, PublishedDate = new(2031, 1, 1), ActorUserId = user, Reason = "PRIVATE_REASON" });
            db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = account, IndexId = ids[0], Period = new(2024, 2, 1),
                PeriodKey = corrected, Revision = 2, Value = 111, ActorUserId = user });
            // Separate indices may have the same period/revision; within one index the database enforces uniqueness.
            foreach (var i in new[] { 1, 2 }) db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = account, IndexId = ids[i], Period = new(2026, 1, 1), PeriodKey = Guid.NewGuid(), Revision = 1, Value = 120, ActorUserId = user });
            await db.SaveChangesAsync(); audits = await db.AuditLogs.CountAsync();
        }
        await using var client = await connect();
        Dictionary<string, object?> Register(int page = 1) => new() { ["accountId"] = account, ["search"] = "INDEX_READ_", ["page"] = page };
        Dictionary<string, object?> Values(Guid? index = null, string view = "Current", int page = 1) => new() { ["accountId"] = account, ["indexId"] = index ?? ids[0], ["view"] = view, ["page"] = page };
        await using (var db = admin.CreateDbContext()) Check(await db.AuditLogs.CountAsync() == audits, "starting read checkpoint");
        var direct = await reader.RegisterAsync(account, "INDEX_READ_");
        var last = await reader.RegisterAsync(account, "INDEX_READ_", page: 999);
        Check(direct.TotalCount == 30 && direct.Items.Length == 25 && last.Items.Length == 5 && last.Page == 2 && !direct.Complete && direct.NextPage == 2, "register pages preserve true totals, clamping and completeness");
        var wire = Data(await client.CallToolAsync("agreements_indices", Register()));
        Check(JsonSerializer.Serialize(direct, Json) == wire.GetProperty("data").GetRawText(), "Application register matches real OAuth/MCP projection");
        Check(!wire.GetRawText().Contains("SECRET_") && !wire.GetRawText().Contains("PRIVATE_") && !wire.GetRawText().Contains("private-secret"), "register wire never contains raw source secrets");
        Check(direct.Items[0].SourceOrigin == "https://statistics.example.test" && direct.Items[1].SourceOrigin is null, "only source origin; no path/parameters/arbitrary configuration");
        foreach (var source in new[] { "https://user:SECRET@statistics.example.test/path", "file:///SECRET", "Bearer SECRET", "https://statistics.example.test\nSECRET", "https://127.0.0.1/SECRET" }) Check(AgreementIndexReader.SourceOrigin(source) is null, "unsafe source withheld");
        Check((await reader.RegisterAsync(account, "INDEX_READ_", AgreementIndexResolution.Quarter)).Items.Single().Id == ids[1] && (await reader.RegisterAsync(account, "INDEX_READ_", AgreementIndexResolution.Year)).Items.Single().Id == ids[2], "actual Month/Quarter/Year resolution filters");
        var current = await reader.ValuesAsync(account, ids[0]); var history = await reader.ValuesAsync(account, ids[0], IndexValueView.History);
        Check(current.TotalCount == 30 && history.TotalCount == 32 && current.Items[0] is { Revision: 2, Superseded: false, Period.Year: 2027, PublishedDate.Year: 2031 }, "current excludes superseded; period differs from future publication date");
        Check((await reader.ValuesAsync(account, ids[0], from: new(2024, 1, 1), to: new(2024, 1, 31))).TotalCount == 0 && (await reader.ValuesAsync(account, ids[0], IndexValueView.History, new(2024, 1, 1), new(2024, 1, 31))).Items.Single().Superseded, "period-moving revision selected before range filter; history explicit");
        var all = history.Items.Concat((await reader.ValuesAsync(account, ids[0], IndexValueView.History, page: 2)).Items).ToArray();
        Check(all.Length == 32 && all.Select(v => v.Id).Distinct().Count() == 32 && all.Any(v => v.PublishedDate is null), "history paging has no omissions/duplicates, null publication preserved");
        Check(all.Where(v => v.Period == new DateOnly(2024, 2, 1)).Select(v => v.Revision).SequenceEqual(new[] { 2, 1 }), "equal period sort keys retain both revisions in explicit descending order");
        wire = Data(await client.CallToolAsync("agreements_index_values", Values()));
        Check(JsonSerializer.Serialize(current, Json) == wire.GetProperty("data").GetRawText(), "Application values match MCP");
        var historical = Data(await client.CallToolAsync("agreements_index_values", Values(view: "History", page: 2)));
        Check(historical.GetProperty("coverage").GetString() == "StoredRevisionHistory" && historical.GetProperty("data").GetProperty("totalCount").GetInt32() == 32, "wire coverage cannot imply complete history from a page");
        foreach (var payload in new[] { wire, historical }) Check(!new[] { "actorUserId", "reason", "canManage", "PRIVATE_", "SECRET_", "RecordedUtc" }.Any(p => payload.GetRawText().Contains(p, StringComparison.OrdinalIgnoreCase)), "no private fields");
        Check(Data(await client.CallToolAsync("agreements_index_values", Values(ids[29]))).GetProperty("data").GetProperty("totalCount").GetInt32() == 0, "accessible empty index differs from missing index");
        foreach (var id in new[] { foreignIndex, Guid.NewGuid() }) Check(Error(await client.CallToolAsync("agreements_index_values", Values(id))) == "RESOURCE_UNAVAILABLE", "foreign and unknown index same error");
        var bad = Values(); bad["view"] = "current"; Check(Error(await client.CallToolAsync("agreements_index_values", bad)) == "INVALID_ARGUMENT", "strict view enum");
        bad = Values(); bad["from"] = "2026-02-01"; bad["to"] = "2026-01-01"; Check(Error(await client.CallToolAsync("agreements_index_values", bad)) == "INVALID_ARGUMENT", "reversed dates rejected");
        bad = Register(); bad["accountId"] = foreign; Check(Error(await client.CallToolAsync("agreements_indices", bad)) == "RESOURCE_UNAVAILABLE", "token does not choose foreign account");
        await using (var db = admin.CreateDbContext()) Check(await db.AuditLogs.CountAsync() == audits && !(await db.Agreements.SingleAsync(a => a.Id == agreement)).DeadlinesInitialized && (await db.AgreementIndexRecords.SingleAsync(i => i.Id == ids[0])).Source.Contains("SECRET_QUERY"), "reads leave audit, initialization and raw source untouched");
        using (var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); try { await reader.ValuesAsync(account, ids[0], ct: cancelled.Token); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { Check(true, "index read cancellation"); }
        }
        // Index register is not agreement-grant filtered: preserve actual legacy rule.
        await using (var db = admin.CreateDbContext()) { db.AgreementAccess.RemoveRange(await db.AgreementAccess.Where(g => g.UserId == user && g.AgreementId == agreement).ToArrayAsync()); await db.SaveChangesAsync(); }
        Check((await reader.ValuesAsync(account, ids[0])).TotalCount == 30, "removing one agreement grant does not remove account register rights");
        await using (var db = admin.CreateDbContext()) { (await db.Users.SingleAsync(u => u.Id == user)).IsActive = false; await db.SaveChangesAsync(); }
        Check(Error(await client.CallToolAsync("agreements_indices", Register())) == "RESOURCE_UNAVAILABLE" && Error(await client.CallToolAsync("agreements_index_values", Values())) == "RESOURCE_UNAVAILABLE", "same token rejects newly deactivated user");
        await using (var db = admin.CreateDbContext()) { (await db.Users.SingleAsync(u => u.Id == user)).IsActive = true; var member = await db.UserAccounts.SingleAsync(m => m.UserId == user && m.AccountId == account); db.UserAccounts.Remove(member); await db.SaveChangesAsync(); }
        Check(Error(await client.CallToolAsync("agreements_index_values", Values())) == "RESOURCE_UNAVAILABLE", "same token rejects revoked membership");
        await using (var db = admin.CreateDbContext()) {
            db.UserAccounts.Add(new() { Id = Guid.NewGuid(), UserId = user, AccountId = account });
            db.AgreementAccess.Add(new() { UserId = user, AccountId = account, AgreementId = agreement, Level = AgreementAccessLevel.Read }); await db.SaveChangesAsync();
            Check((await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision == revision && !await db.AgreementBasisRecords.AnyAsync(b => b.AgreementId == agreement), "no revision or generated bases from reads");
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"ActorUserId\" FROM agreement_indices", "SELECT \"Reason\" FROM agreement_index_values", "SELECT \"RecordedUtc\" FROM agreement_index_values", "UPDATE agreement_indices SET \"Name\"='x' WHERE false" }) {
            try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("private SQL accepted"); }
            catch (PostgresException e) when (e.SqlState == "42501") { Check(true, "narrow index SELECT and write denial"); }
        }
        await using (var db = admin.CreateDbContext()) { db.AgreementIndexValueRecords.RemoveRange(await db.AgreementIndexValueRecords.Where(v => ids.Contains(v.IndexId)).ToArrayAsync()); await db.SaveChangesAsync(); db.AgreementIndexRecords.RemoveRange(await db.AgreementIndexRecords.Where(i => ids.Contains(i.Id) || i.Id == foreignIndex).ToArrayAsync()); await db.SaveChangesAsync(); }
        Console.WriteLine("Index Application/OAuth/MCP/PostgreSQL checks passed.");
    }
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    static JsonElement Data(CallToolResult r) => r.IsError == true ? throw new Exception(JsonSerializer.Serialize(r)) : r.StructuredContent!.Value;
    static string Error(CallToolResult r) => r.IsError == true ? ((TextContentBlock)r.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL index: " + message); Console.WriteLine("PASS index: " + message); }
}
