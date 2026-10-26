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

static class FollowupReadChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid agreement, Guid foreign,
        Func<Guid, Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime)
            .AddInterceptors(new ReadOnlyGuard()).Options);
        var auth = new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System);
        var reader = new AgreementFollowupPageReader(auth, new AgreementFollowupPageData(guarded), TimeProvider.System);
        await using var client = await connect(user);
        Dictionary<string, object?> Args(int page = 1) => new() { ["accountId"] = account, ["agreementId"] = agreement, ["page"] = page };
        Guid revision;
        await using (var db = admin.CreateDbContext()) revision = (await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision;
        var empty = Data(await client.CallToolAsync("agreements_followup", Args()));
        Check(!empty.GetProperty("deadlinesInitialized").GetBoolean() && empty.GetProperty("totalCount").GetInt32() == 0,
            "uninitialized agreement remains uninitialized and empty");
        var rows = Enumerable.Range(0, 27).Select(i => new AgreementDeadline { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement,
            Kind = AgreementDeadlineKind.Notice, DueDate = new DateOnly(2026, 11, 1).AddDays(i), PeriodStartDate = new(2026, 1, 1),
            State = (AgreementDeadlineState)(i % 3 + 1), Status = i % 2 == 0 ? AgreementFollowupStatus.Completed : AgreementFollowupStatus.Untreated,
            CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow }).ToArray();
        await using (var db = admin.CreateDbContext()) { db.AgreementDeadlines.AddRange(rows); await db.SaveChangesAsync(); }
        var direct = await reader.ReadAsync(account, agreement);
        var calendar = new AgreementFollowupPageReader(auth, new AgreementFollowupPageData(guarded), new CalendarClock());
        Check((await calendar.ReadAsync(account, agreement)).Items.Single(d => d.Id == rows[0].Id).DaysLeft == 0,
            "default account time zone uses calendar date across UTC midnight after DST");
        Guid owner;
        await using (var db = admin.CreateDbContext()) owner = (await db.Agreements.SingleAsync(a => a.Id == agreement)).OwnerUserId;
        var ownerReader = new AgreementFollowupPageReader(new AuthorizationService(new FrozenActor(owner, account),
            new AuthorizationData(guarded), TimeProvider.System), new AgreementFollowupPageData(guarded), TimeProvider.System);
        Check((await ownerReader.ReadAsync(account, agreement)).Total == direct.Total, "owner and Read grant retain same stored occurrence set");
        var first = Data(await client.CallToolAsync("agreements_followup", Args()));
        var last = Data(await client.CallToolAsync("agreements_followup", Args(999)));
        Check(direct.Total == 27 && direct.Items.Length == 25 && first.GetProperty("totalCount").GetInt32() == direct.Total,
            "direct authorized Application and actual OAuth/MCP agree");
        Check(first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).SequenceEqual(direct.Items.Select(x => x.Id)), "stable state/date/ID order");
        Check(last.GetProperty("page").GetInt32() == 2 && last.GetProperty("returnedCount").GetInt32() == 2 && !last.GetProperty("complete").GetBoolean() && !last.GetProperty("hasMore").GetBoolean(), "last page preserves full total/completeness");
        Check(direct.Items.Any(d => d.State == AgreementDeadlineState.Withdrawn) && direct.Items.Any(d => d.Status == AgreementFollowupStatus.Completed), "old and completed occurrences preserved");
        var json = first.GetRawText();
        Check(!new[] { "responsible", "assignedUser", "history", "comment", "recipient", "transport", "revision", "canEdit" }.Any(s => json.Contains(s, StringComparison.OrdinalIgnoreCase)), "bounded identity-free projection");
        var invalid = Args(); invalid["userId"] = user;
        Check(Error(await client.CallToolAsync("agreements_followup", invalid)) == "INVALID_ARGUMENT", "spoofed user rejected");
        Check(Error(await client.CallToolAsync("agreements_followup", Args(0))) == "INVALID_ARGUMENT", "invalid page rejected");
        var foreignArgs = Args(); foreignArgs["accountId"] = foreign;
        Check(Error(await client.CallToolAsync("agreements_followup", foreignArgs)) == "RESOURCE_UNAVAILABLE", "foreign account rejected");
        await using (var db = admin.CreateDbContext())
        {
            var foreignResource = Args(); foreignResource["agreementId"] = await db.Agreements.Where(a => a.AccountId == foreign).Select(a => a.Id).FirstAsync();
            Check(Error(await client.CallToolAsync("agreements_followup", foreignResource)) == "RESOURCE_UNAVAILABLE", "foreign agreement under selected local account rejected");
        }
        var unknown = Args(); unknown["agreementId"] = Guid.NewGuid();
        Check(Error(await client.CallToolAsync("agreements_followup", unknown)) == "RESOURCE_UNAVAILABLE", "unknown agreement safely rejected");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await reader.ReadAsync(account, agreement, ct: cancelled.Token); throw new Exception("cancelled call accepted"); }
            catch (OperationCanceledException) { Check(true, "cancelled Application call"); }
        }
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => reader.ReadAsync(account, agreement)));
        Check(parallel.All(p => p.Total == 27), "independent contexts support concurrent reads");
        await using (var db = admin.CreateDbContext())
        {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement);
            Check(a.Revision == revision && !a.DeadlinesInitialized && await db.AgreementDeadlineHistory.CountAsync(h => h.AccountId == account) == 0,
                "reads leave revision, initialization and history unchanged");
            a.IsArchived = true; await db.SaveChangesAsync();
        }
        Check(Data(await client.CallToolAsync("agreements_followup", Args())).GetProperty("isArchived").GetBoolean(), "archived read retains stored occurrences");
        await using (var db = admin.CreateDbContext())
        {
            (await db.Agreements.SingleAsync(a => a.Id == agreement)).IsArchived = false;
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AgreementId == agreement && x.UserId == user));
            await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_followup", Args())) == "RESOURCE_UNAVAILABLE", "grant revocation affects next call with same token");
        await using (var db = admin.CreateDbContext())
        {
            db.AgreementAccess.Add(new AgreementAccess { AccountId = account, AgreementId = agreement, UserId = user, Level = AgreementAccessLevel.Read });
            db.AgreementDeadlines.RemoveRange(await db.AgreementDeadlines.Where(d => d.AgreementId == agreement).ToListAsync());
            await db.SaveChangesAsync();
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"AssignedUserId\" FROM agreement_deadlines", "SELECT \"Comment\" FROM agreement_deadline_history", "UPDATE agreement_deadlines SET \"State\"=1 WHERE false" })
        {
            try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("private/write SQL accepted"); }
            catch (PostgresException ex) when (ex.SqlState == "42501") { Check(true, "least SELECT and no write: " + statement.Split(' ')[0]); }
        }
    }
    static JsonElement Data(CallToolResult result) => result.IsError == true ? throw new Exception(JsonSerializer.Serialize(result)) : result.StructuredContent!.Value;
    static string Error(CallToolResult result) => result.IsError == true ? ((TextContentBlock)result.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL follow-up: " + message); Console.WriteLine("PASS follow-up: " + message); }
    sealed class CalendarClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 31, 23, 30, 0, TimeSpan.Zero);
    }
}
