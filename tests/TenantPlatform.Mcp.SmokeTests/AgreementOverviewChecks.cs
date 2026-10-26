using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Mcp;

static class AgreementOverviewChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid shared, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        var auth = new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System);
        var priority = new AgreementDashboardService(auth, new AgreementDashboardData(guarded), TimeProvider.System);
        var insights = new AgreementInsightsService(auth, new AgreementInsightsData(guarded));
        await using var client = await connect();
        var today = AgreementReminderSchedule.Today(DateTimeOffset.UtcNow, "Europe/Oslo");
        var root = Guid.NewGuid(); var child = Guid.NewGuid(); var unused = Guid.NewGuid(); var foreignNode = Guid.NewGuid();
        Agreement[] originals;
        await using (var db = admin.CreateDbContext())
        {
            originals = await db.Agreements.AsNoTracking().Where(a => a.AccountId == account && a.OwnerUserId == user).OrderBy(a => a.Id).Take(7).ToArrayAsync();
            db.GeographicAreas.AddRange(new GeographicArea { Id = root, AccountId = account, Name = "Root" }, new GeographicArea { Id = child, AccountId = account, ParentId = root, Name = "Child" },
                new GeographicArea { Id = unused, AccountId = account, Name = "UNUSED_NODE" }, new GeographicArea { Id = foreignNode, AccountId = foreign, Name = "FOREIGN_NODE_SECRET" });
            for (var i = 0; i < originals.Length; i++)
            {
                var a = await db.Agreements.SingleAsync(a => a.Id == originals[i].Id);
                a.EndDate = today.AddDays(-i - 1); a.NoticeDeadline = today.AddDays(3); a.IsArchived = i == 6;
                a.GeographicAreaId = i == 0 ? root : i == 1 ? child : null;
            }
            await db.SaveChangesAsync();
        }
        Dictionary<string, object?> Args() => new() { ["accountId"] = account };
        var expectedPriority = await priority.GetDashboardContractsAsync(account);
        var actualPriority = Data(await client.CallToolAsync("agreements_prioritized", Args()));
        Check(actualPriority.GetProperty("selectionLimit").GetInt32() == 5 && actualPriority.GetProperty("returnedCount").GetInt32() == 5 &&
            actualPriority.GetProperty("selectionComplete").GetBoolean() && !actualPriority.GetProperty("isCompleteAgreementList").GetBoolean(), "explicit bounded selection, no fabricated full inventory or total");
        Check(actualPriority.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).SequenceEqual(expectedPriority.Items.Select(i => i.Id)), "exact existing priority/order rules through Application and MCP");
        foreach (var dimension in new[] { AgreementInsightDimension.Type, AgreementInsightDimension.Status, AgreementInsightDimension.Counterparty, AgreementInsightDimension.Geography, AgreementInsightDimension.Organization })
        {
            var expected = await insights.GetInsightsAsync(account, dimension);
            var args = Args(); args["dimension"] = dimension.ToString(); var actual = Data(await client.CallToolAsync("agreements_insights", args));
            Check(actual.GetProperty("totalCount").GetInt32() == expected.Total && expected.Total == 28 &&
                actual.GetProperty("groups").EnumerateArray().Sum(g => g.GetProperty("count").GetInt32()) == 28 && actual.GetProperty("complete").GetBoolean() && !actual.GetProperty("groupsOverlap").GetBoolean(), "nonarchived disjoint partition matches Application: " + dimension);
            Check(!actual.GetRawText().Contains("FOREIGN_NODE_SECRET") && !actual.GetRawText().Contains("UNUSED_NODE") && !actual.GetRawText().Contains("SECRET_OWNER"), "account and visible-node metadata boundary");
        }
        foreach (var (selected, count) in new[] { (root, 2), (child, 1) })
        {
            var args = Args(); args["dimension"] = "Geography"; args["selected"] = selected;
            var result = Data(await client.CallToolAsync("agreements_insights", args));
            Check(result.GetProperty("totalCount").GetInt32() == count && result.GetProperty("groups").EnumerateArray().Any(g => g.GetProperty("direct").GetBoolean()), "selected subtree retains direct versus child partition");
        }
        foreach (var selected in new[] { foreignNode, unused, Guid.NewGuid() })
        {
            var args = Args(); args["dimension"] = "Geography"; args["selected"] = selected;
            Check(Error(await client.CallToolAsync("agreements_insights", args)) == "RESOURCE_UNAVAILABLE", "unknown/foreign/nonvisible nodes share safe error");
        }
        foreach (var invalid in new[] { "Owner", "status", "Unknown" })
        {
            var args = Args(); args["dimension"] = invalid;
            Check(Error(await client.CallToolAsync("agreements_insights", args)) == "INVALID_ARGUMENT", "person grouping and invalid dimensions rejected");
        }
        var nonhierarchy = Args(); nonhierarchy["dimension"] = "Type"; nonhierarchy["selected"] = root;
        Check(Error(await client.CallToolAsync("agreements_insights", nonhierarchy)) == "INVALID_ARGUMENT", "selected only valid for hierarchy");
        Check(Error(await client.CallToolAsync("agreements_prioritized", new Dictionary<string, object?> { ["accountId"] = foreign })) == "RESOURCE_UNAVAILABLE", "foreign priority account rejected");
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel(); try { await insights.GetInsightsAsync(account, AgreementInsightDimension.Type, ct: stop.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "insight cancellation"); }
        }
        await using (var db = admin.CreateDbContext())
        {
            // Keep the grant-backed agreement in the bounded priority result for the revocation check.
            var a = await db.Agreements.SingleAsync(a => a.Id == shared); a.EndDate = today.AddDays(-1); await db.SaveChangesAsync();
            Check(originals.All(o => db.Agreements.AsNoTracking().Single(a => a.Id == o.Id).Revision == o.Revision), "reads do not alter parent revisions");
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(a => a.AgreementId == shared && a.UserId == user)); await db.SaveChangesAsync();
        }
        var after = Data(await client.CallToolAsync("agreements_insights", new Dictionary<string, object?> { ["accountId"] = account, ["dimension"] = "Type" }));
        Check(after.GetProperty("totalCount").GetInt32() == 27, "revoked grant excluded before insight counts");
        Check(!Data(await client.CallToolAsync("agreements_prioritized", Args())).GetProperty("items").EnumerateArray().Any(i => i.GetProperty("id").GetGuid() == shared), "revoked grant excluded from priority selection");
        var extraOrganizations = Enumerable.Range(0, 101).Select(i => new Organization { Id = Guid.NewGuid(), AccountId = account, Name = "Extra " + i }).ToArray();
        var extras = extraOrganizations.Select(o => new Agreement { Id = Guid.NewGuid(), AccountId = account, CounterpartyOrganizationId = o.Id,
            OwnerUserId = user, Title = "Overflow", Type = AgreementType.Other, Status = AgreementStatus.Draft, StartDate = today, CurrentPeriodStartDate = today,
            CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid() }).ToArray();
        await using (var db = admin.CreateDbContext()) { db.Organizations.AddRange(extraOrganizations); db.Agreements.AddRange(extras); await db.SaveChangesAsync(); }
        Check(Error(await client.CallToolAsync("agreements_insights", new Dictionary<string, object?> { ["accountId"] = account, ["dimension"] = "Counterparty" })) == "RESULT_TOO_LARGE", "more than100 groups fails wholly without partial counts");
        await using (var db = admin.CreateDbContext())
        {
            db.Agreements.RemoveRange(await db.Agreements.Where(a => extras.Select(e => e.Id).Contains(a.Id)).ToListAsync());
            foreach (var original in originals)
            {
                var a = await db.Agreements.SingleAsync(a => a.Id == original.Id);
                a.EndDate = original.EndDate; a.NoticeDeadline = original.NoticeDeadline; a.IsArchived = original.IsArchived; a.GeographicAreaId = original.GeographicAreaId;
            }
            (await db.Agreements.SingleAsync(a => a.Id == shared)).EndDate = null;
            db.AgreementAccess.Add(new AgreementAccess { AccountId = account, AgreementId = shared, UserId = user, Level = AgreementAccessLevel.Read });
            await db.SaveChangesAsync(); db.Organizations.RemoveRange(await db.Organizations.Where(o => extraOrganizations.Select(e => e.Id).Contains(o.Id)).ToListAsync());
            db.GeographicAreas.RemoveRange(await db.GeographicAreas.Where(g => g.Id == root || g.Id == child || g.Id == unused || g.Id == foreignNode).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
    static JsonElement Data(CallToolResult result) => result.IsError == true ? throw new Exception(JsonSerializer.Serialize(result)) : result.StructuredContent!.Value;
    static string Error(CallToolResult result) => result.IsError == true ? ((TextContentBlock)result.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL overview: " + message); Console.WriteLine("PASS overview: " + message); }
}
