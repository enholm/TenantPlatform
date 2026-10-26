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

static class AccountDeadlineChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid shared, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        var reader = new AgreementDeadlineReader(new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System), new AgreementDeadlineData(guarded), TimeProvider.System);
        await using var client = await connect();
        Dictionary<string, object?> Args() => new() { ["accountId"] = account };
        var empty = Data(await client.CallToolAsync("agreements_deadlines", Args()));
        Check(empty.GetProperty("totalCount").GetInt32() == 0, "missing stored deadlines do not initialize");
        var today = AgreementReminderSchedule.Today(DateTimeOffset.UtcNow, "Europe/Oslo");
        Guid owned; Guid otherOwner; Guid revision;
        await using (var db = admin.CreateDbContext())
        {
            var a = await db.Agreements.FirstAsync(x => x.AccountId == account && x.OwnerUserId == user);
            owned = a.Id; revision = a.Revision; otherOwner = (await db.Agreements.SingleAsync(x => x.Id == shared)).OwnerUserId;
        }
        var deadlines = Enumerable.Range(0, 31).Select(i => new AgreementDeadline { Id = Guid.NewGuid(), AccountId = account,
            AgreementId = i == 30 ? shared : owned, Kind = AgreementDeadlineKind.Notice,
            DueDate = today.AddDays(i == 29 ? 100 : i % 3 == 0 ? -1 : i % 3 == 1 ? 0 : 31), PeriodStartDate = today,
            Status = i == 27 ? AgreementFollowupStatus.Completed : AgreementFollowupStatus.Untreated,
            State = i == 28 ? AgreementDeadlineState.Replaced : AgreementDeadlineState.Current,
            AssignedUserId = i == 0 ? otherOwner : i == 30 ? user : null,
            CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow }).ToArray();
        await using (var db = admin.CreateDbContext()) { db.AgreementDeadlines.AddRange(deadlines); await db.SaveChangesAsync(); }
        foreach (var (filter, args) in new (AgreementDeadlineFilter, Dictionary<string, object?>)[] {
            (new(), Args()), (new() { Mine = true }, new() { ["accountId"] = account, ["mine"] = true }),
            (new() { CustomDates = true, State = null, IncludeCompleted = true }, new() { ["accountId"] = account, ["customDates"] = true, ["state"] = "All", ["includeCompleted"] = true }),
            (new() { Status = AgreementFollowupStatus.Completed }, new() { ["accountId"] = account, ["status"] = "Completed" }),
            (new() { CustomDates = true, From = today, To = today }, new() { ["accountId"] = account, ["customDates"] = true, ["from"] = today.ToString("yyyy-MM-dd"), ["to"] = today.ToString("yyyy-MM-dd") }),
            (new() { Search = "no such deadline" }, new() { ["accountId"] = account, ["search"] = "no such deadline" }),
            (new() { Page = 999 }, new() { ["accountId"] = account, ["page"] = 999 }) })
        {
            var expected = await reader.ReadAsync(account, filter); var actual = Data(await client.CallToolAsync("agreements_deadlines", args));
            Check(actual.GetProperty("totalCount").GetInt32() == expected.Total && actual.GetProperty("page").GetInt32() == expected.Page &&
                actual.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).SequenceEqual(expected.Items.Select(x => x.Id)), "Application/MCP filter and paging agreement");
            var categories = actual.GetProperty("categories");
            Check(categories.GetProperty("overdue").GetInt32() == 10 && categories.GetProperty("next30").GetInt32() == 9 && categories.GetProperty("missingResponsible").GetInt32() == 2 &&
                categories.GetProperty("beforeListFilters").GetBoolean() && categories.GetProperty("groupsOverlap").GetBoolean(), "unfiltered visible categories include shared resource and preserve overlap");
            Check(!new[] { "ownerId", "owners", "assignedUserId", "responsibleUserId", "responsibleName", "SECRET_OWNER" }.Any(actual.GetRawText().Contains), "no identities or person catalogue");
        }
        Check((await reader.ReadAsync(account, new() { Mine = true })).Total == 27, "Mine includes inherited owner and explicit self assignment; excludes another assignee");
        var last = Data(await client.CallToolAsync("agreements_deadlines", new Dictionary<string, object?>() { ["accountId"] = account, ["page"] = 2 }));
        Check(last.GetProperty("returnedCount").GetInt32() == 3 && !last.GetProperty("complete").GetBoolean() && !last.GetProperty("hasMore").GetBoolean(), "last page is not falsely complete");
        foreach (var invalid in new Dictionary<string, object?>[] {
            new() { ["accountId"] = account, ["assignedUserId"] = user }, new() { ["accountId"] = account, ["userId"] = user },
            new() { ["accountId"] = account, ["mine"] = "true" }, new() { ["accountId"] = account, ["horizonDays"] = "30" },
            new() { ["accountId"] = account, ["horizonDays"] = 1 }, new() { ["accountId"] = account, ["state"] = "current" },
            new() { ["accountId"] = account, ["from"] = "2026-01-01" }, new() { ["accountId"] = account, ["customDates"] = true, ["horizonDays"] = 30 },
            new() { ["accountId"] = account, ["customDates"] = true, ["from"] = "2026-02-30" }, new() { ["accountId"] = account, ["page"] = 10001 } })
            Check(Error(await client.CallToolAsync("agreements_deadlines", invalid)) == "INVALID_ARGUMENT", "invalid/person selector rejected");
        Check(Error(await client.CallToolAsync("agreements_deadlines", new Dictionary<string, object?>() { ["accountId"] = foreign })) == "RESOURCE_UNAVAILABLE", "foreign account rejected");
        using (var stop = new CancellationTokenSource()) { stop.Cancel(); try { await reader.ReadAsync(account, new(), stop.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { Check(true, "Application cancellation"); } }
        await using (var db = admin.CreateDbContext())
        {
            var a = await db.Agreements.SingleAsync(x => x.Id == owned);
            Check(a.Revision == revision && !a.DeadlinesInitialized && !await db.AgreementDeadlineHistory.AnyAsync(x => x.AccountId == account), "no revision/history/initialization writes");
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AgreementId == shared && x.UserId == user)); await db.SaveChangesAsync();
        }
        var revoked = Data(await client.CallToolAsync("agreements_deadlines", Args()));
        Check(revoked.GetProperty("totalCount").GetInt32() == 27 && revoked.GetProperty("categories").GetProperty("overdue").GetInt32() == 9, "grant revocation filters both list and categories");
        await using (var db = admin.CreateDbContext())
        {
            db.AgreementAccess.Add(new AgreementAccess { AccountId = account, AgreementId = shared, UserId = user, Level = AgreementAccessLevel.Read });
            db.AgreementDeadlines.RemoveRange(await db.AgreementDeadlines.Where(d => deadlines.Select(r => r.Id).Contains(d.Id)).ToListAsync()); await db.SaveChangesAsync();
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"CompletedByUserId\" FROM agreement_deadlines", "SELECT \"Comment\" FROM agreement_deadline_history", "UPDATE agreement_deadlines SET \"State\"=1 WHERE false" })
        {
            try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("private/write SQL accepted"); }
            catch (PostgresException ex) when (ex.SqlState == "42501") { Check(true, "least SELECT and no writes"); }
        }
    }
    static JsonElement Data(CallToolResult result) => result.IsError == true ? throw new Exception(JsonSerializer.Serialize(result)) : result.StructuredContent!.Value;
    static string Error(CallToolResult result) => result.IsError == true ? ((TextContentBlock)result.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL account deadlines: " + message); Console.WriteLine("PASS account deadlines: " + message); }
}
