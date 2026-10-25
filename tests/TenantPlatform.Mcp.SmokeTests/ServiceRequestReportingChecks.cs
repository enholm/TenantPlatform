using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.ServiceRequests;
using TenantPlatform.Mcp;
using TenantPlatform.Web.Security;

static class ServiceRequestReportingChecks
{
    public static async Task Run(Factory admin, string runtime, string password, Func<Guid, Task<McpClient>> connect)
    {
        var account = Guid.NewGuid(); var foreign = Guid.NewGuid(); var org = Guid.NewGuid(); var providerOrg = Guid.NewGuid();
        var building = Guid.NewGuid(); var otherBuilding = Guid.NewGuid(); var foreignBuilding = Guid.NewGuid(); var definition = Guid.NewGuid();
        var requester = Guid.NewGuid(); var tenant = Guid.NewGuid(); var provider = Guid.NewGuid(); var property = Guid.NewGuid(); var administrator = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var rows = new List<ServiceRequest>();
        await using (var db = admin.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Requests" }, new Account { Id = foreign, Name = "Foreign requests" });
            db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Tenant" }, new Organization { Id = providerOrg, AccountId = account, Name = "Provider" });
            db.Buildings.AddRange(new Building { Id = building, AccountId = account, Name = "Local" }, new Building { Id = otherBuilding, AccountId = account, Name = "Other local" }, new Building { Id = foreignBuilding, AccountId = foreign, Name = "FOREIGN_SECRET" });
            db.ServiceDefinitions.Add(new ServiceDefinition { Id = definition, AccountId = account, Code = "9E" });
            foreach (var user in new[] { requester, tenant, provider, property, administrator, outsider })
            {
                db.Users.Add(new User { Id = user, Email = user + "@example.test", IsActive = true, IsPlatformAdmin = user == outsider });
                var membership = new UserAccount { Id = Guid.NewGuid(), UserId = user, AccountId = account };
                db.UserAccounts.Add(membership);
                if (user == tenant || user == provider || user == property || user == administrator)
                    db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id,
                        Role = user == tenant ? UserRole.TenantUser : user == provider ? UserRole.ServiceProviderUser : user == property ? UserRole.PropertyAdmin : UserRole.AccountAdmin,
                        OrganizationId = user == tenant ? org : user == provider ? providerOrg : null, BuildingId = user == property ? building : null });
                var login = new LoginAccount { Id = Guid.NewGuid(), UserId = user, Email = user + "@example.test", CreatedUtc = DateTimeOffset.UtcNow };
                login.PasswordHash = new PasswordService().HashPassword(login, password); db.LoginAccounts.Add(login);
            }
            for (var i = 0; i < 29; i++)
            {
                var row = new ServiceRequest { Id = Guid.NewGuid(), AccountId = i == 28 ? foreign : account, ServiceDefinitionId = definition,
                    RequesterUserId = i < 27 ? requester : administrator, RequesterOrganizationId = org,
                    AssignedServiceProviderOrganizationId = i % 2 == 0 ? providerOrg : null, BuildingId = i == 0 ? foreignBuilding : i % 2 == 0 ? building : otherBuilding,
                    Title = i == 0 ? null : "Request " + i, Status = (ServiceRequestStatus)(i % 8 + 1), CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                    ReplyToken = Guid.NewGuid().ToString("N"), Comment = "PRIVATE_COMMENT" };
                rows.Add(row); db.ServiceRequests.Add(row);
            }
            await db.SaveChangesAsync();
        }
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        ServiceRequestReportingReader Reader(Guid user, Guid? selected = null) => new(new AuthorizationService(new FrozenActor(user, selected ?? account), new AuthorizationData(guarded), TimeProvider.System), new ServiceRequestReportingData(guarded));
        var reader = Reader(requester);
        Check((await reader.ListAsync(account, new())).TotalCount == 27, "requester scope before counts");
        Check((await Reader(tenant).ListAsync(account, new())).TotalCount == 28, "tenant organization scope");
        Check((await Reader(provider).ListAsync(account, new())).TotalCount == 14, "assigned provider excludes unassigned");
        Check((await Reader(property).ListAsync(account, new())).TotalCount == 13, "property administrator building scope");
        Check((await Reader(administrator).ListAsync(account, new())).TotalCount == 28, "account admin scope");
        Check((await Reader(outsider).ListAsync(account, new())).TotalCount == 0, "platform admin does not inherit account data");
        foreach (var filter in new ServiceRequestReportFilter[] { new(), new(Search: "Request 1"), new(Status: ServiceRequestStatus.Completed), new(BuildingId: building), new(BuildingId: foreignBuilding), new(MissingBuilding: true), new(Search: "no matches") })
        {
            var list = await reader.ListAsync(account, filter);
            foreach (var group in Enum.GetValues<ServiceRequestReportGrouping>())
            {
                var counts = await reader.CountAsync(account, filter, group);
                Check(list.TotalCount == counts.TotalCount && counts.Groups.Sum(g => g.Count) == counts.TotalCount, "list/SQL aggregate correspondence " + group);
                Check(!JsonSerializer.Serialize(counts).Contains("FOREIGN_SECRET"), "no foreign group metadata");
            }
        }
        var first = await reader.ListAsync(account, new()); var last = await reader.ListAsync(account, new(), 999);
        Check(first.Items.Length == 25 && first.HasMore && last.Page == 2 && last.Items.Length == 2 && !last.HasMore && !last.Complete, "paging and completeness");
        Check(last.Items.Single(r => r.Id == rows[0].Id) is { Title: null, Building: null, Unit: null, SubmittedAt: null, CompletedAt: null }, "null metadata retained, foreign metadata removed");
        Check(first.Items.Select(r => r.Id).SequenceEqual(rows.Take(27).OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Take(25).Select(r => r.Id)), "stable newest/ID order");
        foreach (var status in Enum.GetValues<ServiceRequestStatus>()) Check((await reader.CountAsync(account, new(Status: status), ServiceRequestReportGrouping.Status)).Groups.All(g => g.Key == status.ToString()), "stored status without inferred transitions");
        await Throws<UnauthorizedAccessException>(() => Reader(requester, foreign).ListAsync(foreign, new()));
        foreach (var invalid in new ServiceRequestReportFilter[] { new(Status: (ServiceRequestStatus)999), new(BuildingId: Guid.Empty), new(BuildingId: building, MissingBuilding: true), new(Search: "\n") })
            await Throws<ArgumentException>(() => reader.ListAsync(account, invalid));
        await Throws<ArgumentException>(() => reader.ListAsync(account, new(), 0));
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); await Throws<OperationCanceledException>(() => reader.ListAsync(account, new(), ct: cancelled.Token)); }
        await using var client = await connect(requester);
        foreach (var name in new[] { "service_requests_query", "service_requests_aggregate" })
        {
            var args = new Dictionary<string, object?> { ["accountId"] = account }; if (name.EndsWith("aggregate")) args["groupBy"] = "Status";
            var result = await client.CallToolAsync(name, args);
            Check(result.IsError != true && result.StructuredContent!.Value.GetProperty("totalCount").GetInt32() == 27, "actual OAuth/MCP/direct equivalence " + name);
            Check(!result.StructuredContent!.Value.GetRawText().Contains("PRIVATE_COMMENT") && !result.StructuredContent.Value.GetRawText().Contains("replyToken"), "no private workflow payload");
            args["userId"] = administrator;
            Check((await client.CallToolAsync(name, args)).IsError == true, "wire cannot spoof identity");
        }
        // Column grants exclude workflow secrets, message/outbox tables and all writes. No broad table SELECT.
        await using (var sql = new NpgsqlConnection(runtime))
        {
            await sql.OpenAsync();
            foreach (var statement in new[] { "SELECT \"ReplyToken\" FROM service_requests", "SELECT \"Comment\" FROM service_requests", "SELECT * FROM service_request_messages", "UPDATE service_requests SET \"Title\"='bad'" })
            {
                try { await using var command = new NpgsqlCommand(statement, sql); await command.ExecuteNonQueryAsync(); throw new Exception("privilege accepted: " + statement); }
                catch (PostgresException ex) when (ex.SqlState == "42501") { }
            }
        }
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => reader.CountAsync(account, new(), ServiceRequestReportGrouping.Status)));
        Check(parallel.All(r => r.TotalCount == 27), "separate short contexts for concurrent request reads");
        await using (var db = admin.CreateDbContext())
        {
            for (var i = 0; i < 101; i++)
            {
                var id = Guid.NewGuid(); db.Buildings.Add(new Building { Id = id, AccountId = account, Name = "Overflow " + i });
                db.ServiceRequests.Add(new ServiceRequest { Id = Guid.NewGuid(), AccountId = account, ServiceDefinitionId = definition, RequesterUserId = requester,
                    RequesterOrganizationId = org, BuildingId = id, Title = "Overflow9E", Status = ServiceRequestStatus.Submitted,
                    CreatedAt = DateTimeOffset.UtcNow, ReplyToken = Guid.NewGuid().ToString("N") });
            }
            await db.SaveChangesAsync();
        }
        await Throws<ServiceRequestReportLimitException>(() => reader.CountAsync(account, new(Search: "Overflow9E"), ServiceRequestReportGrouping.Building));
        var overflow = await client.CallToolAsync("service_requests_aggregate", new Dictionary<string, object?> { ["accountId"] = account, ["search"] = "Overflow9E", ["groupBy"] = "Building" });
        Check(overflow.IsError == true && overflow.Content.OfType<TextContentBlock>().Single().Text == "RESULT_TOO_LARGE", "group overflow never returns partial totals");
        await using (var db = guarded.CreateDbContext()) await Throws<InvalidOperationException>(() => db.SaveChangesAsync());
        await using (var db = admin.CreateDbContext()) { db.RemoveRange(await db.UserAccountRoles.Where(r => r.UserAccount.UserId == provider).ToArrayAsync()); await db.SaveChangesAsync(); }
        Check((await Reader(provider).CountAsync(account, new(), ServiceRequestReportGrouping.Building)).TotalCount == 0, "provider role revocation");
        await using (var db = admin.CreateDbContext()) { db.Remove(await db.UserAccounts.SingleAsync(m => m.AccountId == account && m.UserId == requester)); await db.SaveChangesAsync(); }
        await Throws<UnauthorizedAccessException>(() => reader.ListAsync(account, new()));
        Check((await client.CallToolAsync("service_requests_query", new Dictionary<string, object?> { ["accountId"] = account })).IsError == true, "same MCP session observes membership revocation");
        Console.WriteLine("PASS 9E request reporting foundation: Application, column-limited PostgreSQL and actual OAuth/MCP");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
