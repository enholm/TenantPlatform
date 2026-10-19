using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Mcp;
using TenantPlatform.Web.Security;

static class ReportingChecks
{
    public static async Task Run(Factory admin, string runtime, string password, Func<Guid, Task<McpClient>> connect)
    {
        var account = Guid.NewGuid(); var foreign = Guid.NewGuid(); var user = Guid.NewGuid(); var owner = Guid.NewGuid();
        var org = Guid.NewGuid(); var foreignOrg = Guid.NewGuid(); var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid(); var foreignBuilding = Guid.NewGuid();
        var element = Guid.NewGuid(); var area = Guid.NewGuid(); var unit = Guid.NewGuid();
        var today = AgreementReminderSchedule.Today(TimeProvider.System.GetUtcNow(), "Europe/Oslo");
        var rows = new List<Agreement>();
        await using (var db = admin.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Reporting" }, new Account { Id = foreign, Name = "Foreign reporting" });
            foreach (var id in new[] { user, owner })
            {
                db.Users.Add(new User { Id = id, FirstName = "PRIVATE_PERSON", Email = id + "@example.test", IsActive = true });
                db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = id });
            }
            var login = new LoginAccount { Id = Guid.NewGuid(), UserId = user, Email = user + "@example.test", CreatedUtc = DateTimeOffset.UtcNow };
            login.PasswordHash = new PasswordService().HashPassword(login, password); db.LoginAccounts.Add(login);
            db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Local counterparty" }, new Organization { Id = foreignOrg, AccountId = foreign, Name = "FOREIGN_METADATA" });
            db.Buildings.AddRange(new Building { Id = b1, AccountId = account, Name = "Building one" }, new Building { Id = b2, AccountId = account, Name = "Building two" }, new Building { Id = foreignBuilding, AccountId = foreign, Name = "FOREIGN_METADATA" });
            db.OrganizationElements.Add(new OrganizationElement { Id = element, AccountId = account, Name = "Department" });
            db.GeographicAreas.Add(new GeographicArea { Id = area, AccountId = account, Name = "Area" });
            db.Units.Add(new Unit { Id = unit, AccountId = account, BuildingId = b1, Name = "Unit" });
            for (var i = 0; i < 28; i++)
            {
                var a = new Agreement { Id = Guid.NewGuid(), AccountId = account, CounterpartyOrganizationId = i == 3 ? foreignOrg : org,
                    OwnerUserId = i < 26 ? user : owner, Title = $"Report {i:D2}", Type = AgreementType.Supplier,
                    Status = i == 1 ? AgreementStatus.Draft : AgreementStatus.Active, StartDate = today.AddYears(-1), CurrentPeriodStartDate = today.AddYears(-1),
                    EndDate = i == 0 ? today : i == 1 ? today.AddMonths(3) : i == 2 ? today.AddMonths(3).AddDays(1) : null,
                    BuildingId = i == 3 ? foreignBuilding : i % 3 == 0 ? b1 : i % 3 == 1 ? b2 : null,
                    OrganizationElementId = element, GeographicAreaId = area, IsArchived = i == 0,
                    CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid() };
                if (i == 0) a.UnitId = unit;
                rows.Add(a); db.Agreements.Add(a);
            }
            db.AgreementAccess.Add(new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = rows[26].Id, UserId = user, Level = AgreementAccessLevel.Read });
            await db.SaveChangesAsync();
        }
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        AgreementReportingReader Reader(Guid actor, Guid selected)
        {
            var auth = new AuthorizationService(new FrozenActor(actor, selected), new AuthorizationData(guarded), TimeProvider.System);
            return new(auth, new AgreementReportingData(guarded), TimeProvider.System);
        }
        var reader = Reader(user, account);
        var (_, page) = await reader.ListAsync(account, new());
        Check(page.TotalCount == 27 && page.Items.Length == 25, "authorized paging excludes hidden agreement before count");
        Check(page.Items.Select(a => a.Title).SequenceEqual(rows.Take(25).Select(a => a.Title)), "stable Title/Id sorting survives metadata joins");
        Check(page.Items.Single(a => a.Id == rows[3].Id) is { Building: null, Counterparty: null }, "foreign legacy references sanitized without removing agreement");
        Check(page.Items.Single(a => a.Id == rows[0].Id) is { Unit: not null, OrganizationElement: not null, GeographicArea: not null }, "account-bound dimensions exposed");
        var partition = (await reader.CountAsync(account, new(), AgreementReportGrouping.Building)).Data;
        Check(partition.Groups.Select(g => g.Key).Order().SequenceEqual(new[] { b1.ToString("D"), b2.ToString("D"), "missing" }.Order()) && partition.Groups.Single(g => g.Key == "missing").Name is null,
            "two different buildings and missing/foreign references form three nonoverlapping groups");
        Check((await reader.ListAsync(account, new(), 999)).Data is { Page: 2, TotalCount: 27 } last && last.Items.Length == 2, "real final page clamping");
        foreach (var filter in new AgreementReportFilter[] { new(), new(Status: AgreementStatus.Active, Archive: AgreementArchiveSelection.Exclude),
            new(EndsWithinMonths: 3), new(EndDateFrom: today, EndDateTo: today.AddMonths(3)), new(MissingBuilding: true),
            new(BuildingId: b1, OrganizationElementId: element, GeographicAreaId: area), new(UnitId: unit), new(Search: "Local counterparty"),
            new(Archive: AgreementArchiveSelection.Only), new(BuildingId: foreignBuilding), new(Search: "no matches") })
        {
            var (_, list) = await reader.ListAsync(account, filter);
            foreach (var grouping in Enum.GetValues<AgreementReportGrouping>())
            {
                var (_, counts) = await reader.CountAsync(account, filter, grouping);
                Check(list.TotalCount == counts.TotalCount && counts.Groups.Sum(g => g.Count) == counts.TotalCount, "identical filters partition unique agreements in SQL: " + grouping);
                Check(!JsonSerializer.Serialize(counts).Contains("FOREIGN_METADATA"), "groups do not expose foreign metadata");
            }
        }
        Check((await reader.ListAsync(account, new(EndsWithinMonths: 3))).Data.TotalCount == 2, "relative inclusive calendar-month bounds include endpoints and exclude null/next day");
        var fixedReader = new AgreementReportingReader(new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System), new AgreementReportingData(guarded), new FixedClock());
        var fixedSelection = (await fixedReader.ListAsync(account, new(EndsWithinMonths: 3))).Selection;
        Check(fixedSelection.Today == new DateOnly(2026, 3, 29) && fixedSelection.Filter.EndDateTo == new DateOnly(2026, 6, 29), "single UTC instant uses account date over DST/midnight");
        Check(fixedSelection.Filter.EndsWithinMonths is null, "normalized filter can be reused without a relative/absolute date conflict");
        await reader.CountAsync(account, fixedSelection.Filter, AgreementReportGrouping.Building);
        foreach (var invalid in new AgreementReportFilter[] { new(EndDateFrom: today, EndDateTo: today.AddDays(-1)), new(EndsWithinMonths: 0), new(EndsWithinMonths: 3, EndDateFrom: today), new(BuildingId: b1, MissingBuilding: true), new(Archive: (AgreementArchiveSelection)999), new(BuildingId: Guid.Empty), new(Search: "\n") })
            await Throws<ArgumentException>(() => reader.ListAsync(account, invalid));
        await Throws<ArgumentException>(() => reader.ListAsync(account, new(), 0));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Throws<OperationCanceledException>(() => reader.ListAsync(account, new(), ct: cancelled.Token));
        }
        await Throws<UnauthorizedAccessException>(() => Reader(user, foreign).CountAsync(foreign, new(), AgreementReportGrouping.Building));
        var detailsAuth = new AuthorizationService(new FrozenActor(user, account), new AuthorizationData(guarded), TimeProvider.System);
        var details = new AgreementDetailsReader(detailsAuth, new AgreementDetailsData(guarded));
        Check((await details.ReadSummaryAsync(account, rows[26].Id)).Id == rows[26].Id, "Read grant permits bounded details");
        await Throws<UnauthorizedAccessException>(() => details.ReadSummaryAsync(account, rows[27].Id));
        await Throws<UnauthorizedAccessException>(() => details.ReadSummaryAsync(account, Guid.NewGuid()));
        await using var client = await connect(user);
        foreach (var tool in ReadTools.Catalog) Check(new TenantPlatform.Infrastructure.Agent.AgentSchemaValidator().Supported(tool.InputSchema.GetRawText()), "input schema supported without validator changes: " + tool.Name);
        var wire = await Call("agreements_query", new() { ["endsWithinMonths"] = 3 });
        Check(wire.GetProperty("totalCount").GetInt32() == 2 && wire.GetProperty("selection").GetProperty("today").GetString() == today.ToString("yyyy-MM-dd"), "real MCP filtered list equals direct Application");
        var aggregate = await Call("agreements_aggregate", new() { ["groupBy"] = "Building", ["endsWithinMonths"] = 3 });
        Check(aggregate.GetProperty("totalCount").GetInt32() == 2 && aggregate.GetProperty("complete").GetBoolean() && !aggregate.GetProperty("groupsOverlap").GetBoolean(), "real MCP aggregate completeness and partition semantics");
        var detail = await Call("agreements_details", new() { ["agreementId"] = rows[26].Id });
        Check(detail.GetProperty("agreement").GetProperty("id").GetGuid() == rows[26].Id && !detail.GetRawText().Contains("PRIVATE_PERSON") && !detail.GetRawText().Contains("revision"), "real MCP summary omits personal and internal fields");
        foreach (var args in new Dictionary<string, object?>[] { new() { ["endDateFrom"] = "2026-02-30" }, new() { ["archive"] = "anything" }, new() { ["endsWithinMonths"] = 13 }, new() { ["page"] = null }, new() { ["missingBuilding"] = "true" }, new() { ["sql"] = "SELECT" } })
        {
            args["accountId"] = account;
            Check((await client.CallToolAsync("agreements_query", args)).IsError == true, "wire rejects invalid filters");
        }
        await using (var db = admin.CreateDbContext())
        {
            var grant = await db.AgreementAccess.SingleAsync(g => g.AgreementId == rows[26].Id); grant.Level = AgreementAccessLevel.Edit; await db.SaveChangesAsync();
        }
        Check((await details.ReadSummaryAsync(account, rows[26].Id)).Id == rows[26].Id, "Edit grant preserves read access");
        await using (var db = admin.CreateDbContext()) { db.Remove(await db.AgreementAccess.SingleAsync(g => g.AgreementId == rows[26].Id)); await db.SaveChangesAsync(); }
        Check((await reader.CountAsync(account, new(), AgreementReportGrouping.Building)).Data.TotalCount == 26, "grant revocation updates SQL totals");
        Check((await client.CallToolAsync("agreements_details", new Dictionary<string, object?> { ["accountId"] = account, ["agreementId"] = rows[26].Id })).IsError == true, "grant revocation observed over same MCP client");
        await using (var db = admin.CreateDbContext())
        {
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = (await db.UserAccounts.SingleAsync(m => m.AccountId == account && m.UserId == user)).Id, Role = UserRole.AccountAdmin }); await db.SaveChangesAsync();
        }
        Check((await reader.CountAsync(account, new(), AgreementReportGrouping.Status)).Data.TotalCount == 28, "AccountAdmin scope applied before aggregation");
        await using (var db = admin.CreateDbContext())
        {
            Check(!await db.Agreements.Where(a => a.AccountId == account).AnyAsync(a => a.DeadlinesInitialized) && !await db.AgreementReminderSettings.AnyAsync(s => s.AccountId == account), "all reads remain uninitialized");
            Check((await db.Agreements.Where(a => a.AccountId == account).OrderBy(a => a.Id).Select(a => a.Revision).ToArrayAsync()).SequenceEqual(rows.OrderBy(a => a.Id).Select(a => a.Revision)), "all reads preserve agreement revisions");
            for (var i = 0; i < 101; i++)
            {
                var building = new Building { Id = Guid.NewGuid(), AccountId = account, Name = "Overflow " + i }; db.Buildings.Add(building);
                db.Agreements.Add(new Agreement { Id = Guid.NewGuid(), AccountId = account, Title = "Overflow " + i, BuildingId = building.Id, CounterpartyOrganizationId = org, OwnerUserId = user,
                    Type = AgreementType.Supplier, Status = AgreementStatus.Active, StartDate = today, CurrentPeriodStartDate = today, CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid() });
            }
            await db.SaveChangesAsync();
        }
        await Throws<AgreementReportLimitException>(() => reader.CountAsync(account, new(Search: "Overflow"), AgreementReportGrouping.Building));
        Check((await client.CallToolAsync("agreements_aggregate", new Dictionary<string, object?> { ["accountId"] = account, ["search"] = "Overflow", ["groupBy"] = "Building" })).Content.OfType<TextContentBlock>().Single().Text == "RESULT_TOO_LARGE", "101 groups returns whole-result error");
        await using (var db = admin.CreateDbContext()) { var membership = await db.UserAccounts.SingleAsync(m => m.AccountId == account && m.UserId == user); db.RemoveRange(await db.UserAccountRoles.Where(r => r.UserAccountId == membership.Id).ToListAsync()); db.Remove(membership); await db.SaveChangesAsync(); }
        await Throws<UnauthorizedAccessException>(() => reader.ListAsync(account, new()));
        Console.WriteLine("PASS 8D reporting: direct Application, PostgreSQL and real MCP transport");

        async Task<JsonElement> Call(string name, Dictionary<string, object?> args)
        {
            args["accountId"] = account; var result = await client.CallToolAsync(name, args);
            if (result.IsError == true) throw new Exception(name + ": " + result.Content.OfType<TextContentBlock>().Single().Text);
            return result.StructuredContent!.Value;
        }
    }
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL 8D: " + label); Console.WriteLine("PASS 8D: " + label); }
    static async Task Throws<T>(Func<Task> operation) where T : Exception { try { await operation(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 3, 28, 23, 30, 0, TimeSpan.Zero); }
}
