using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Mcp;

static class AgreementExtensionChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid agreement, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        AgreementReadExtensions Reader(Guid actor) => new(new AuthorizationService(new FrozenActor(actor, account), new AuthorizationData(guarded), TimeProvider.System), new AgreementReadExtensionData(guarded));
        var reader = Reader(user);
        await using var client = await connect();
        Dictionary<string, object?> Args() => new() { ["accountId"] = account, ["agreementId"] = agreement };
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> args) => Data(await client.CallToolAsync(tool, args));
        var deadline = Guid.NewGuid(); var hiddenBuilding = Guid.NewGuid(); var foreignBuilding = Guid.NewGuid();
        var createdAgreements = new List<Guid>(); var buildings = new List<Guid>(); Guid revision; Guid owner;
        await using (var db = admin.CreateDbContext())
        {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); owner = a.OwnerUserId; revision = a.Revision;
            a.Description = "Ordinary business description"; a.Terms = "Payment within 30 days. Renew by written notice.";
            a.Currency = "NOK"; a.Direction = AgreementDirection.Cost;
            a.Form = AgreementForm.Ongoing; a.NoticeCount = 3; a.NoticeUnit = AgreementNoticeUnit.Months; a.AutoRenew = false; a.RenewalMonths = null; a.EndDate = null; a.RenewalDate = null; a.NoticeDeadline = null; a.TerminationEffectiveDate = new(2026, 10, 1); a.CessationDate = new(2027, 1, 1); a.TerminationNoticeCount = 3; a.TerminationNoticeUnit = AgreementNoticeUnit.Months; a.TerminationRegisteredUtc = DateTimeOffset.UtcNow; a.TerminationRegisteredByUserId = owner;
            db.AgreementDeadlines.Add(new() { Id = deadline, AccountId = account, AgreementId = agreement, Kind = AgreementDeadlineKind.Notice,
                DueDate = new(2027, 1, 1), PeriodStartDate = new(2026, 1, 1) });
            for (var i = 0; i < 29; i++) db.AgreementDeadlineHistory.Add(new() { Id = Guid.NewGuid(), AccountId = account,
                DeadlineId = deadline, Kind = (AgreementHistoryKind)(i % 9 + 1), CreatedUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                ActorUserId = owner, AssignedUserId = owner, Comment = "PRIVATE_COMMENT" });
            db.Buildings.AddRange(new Building { Id = hiddenBuilding, AccountId = account, Name = "PRIVATE_UNLINKED" },
                new Building { Id = foreignBuilding, AccountId = foreign, Name = "PRIVATE_FOREIGN" });
            for (var i = 0; i < 28; i++)
            {
                var id = Guid.NewGuid(); buildings.Add(id); db.Buildings.Add(new Building { Id = id, AccountId = account, Name = "Duplicate lookup label" });
                var copy = new Agreement { Id = Guid.NewGuid(), AccountId = account, Title = "Lookup test", Type = AgreementType.Other, BuildingId = id, CounterpartyOrganizationId = a.CounterpartyOrganizationId,
                    OwnerUserId = user, CreatedByUserId = user, UpdatedByUserId = user, StartDate = a.StartDate, CurrentPeriodStartDate = a.CurrentPeriodStartDate, Revision = Guid.NewGuid() };
                createdAgreements.Add(copy.Id); db.Agreements.Add(copy);
            }
            await db.SaveChangesAsync();
        }
        var direct = await reader.DetailsAsync(account, agreement);
        var wire = await Call("agreements_details", Args());
        Check(wire.GetProperty("schemaVersion").GetString() == "2" && wire.GetProperty("agreement").GetProperty("id").GetGuid() == direct.Agreement.Id, "explicit v2 preserves summary and concrete authorization");
        Check(wire.GetProperty("business").GetProperty("terms").GetString() == direct.Business.Terms && direct.Business.Description == "Ordinary business description" && direct.Business.Currency == "NOK", "ordinary text, currency and notice business fields agree with Application");
        Check((await Reader(owner).DetailsAsync(account, agreement)).Business == direct.Business, "owner and Read see same ordinary fields");
        Check(!(await reader.DetailsAsync(account, agreement)).Business.DeadlinesInitialized, "detail read never initializes");
        await using (var db = admin.CreateDbContext()) {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); a.Description = new string('界', 4000); a.Terms = new string('界', 10000); await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_details", Args())) == "RESULT_TOO_LARGE", "large business text fails whole result instead of silent truncation");
        await using (var db = admin.CreateDbContext()) {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); a.Description = direct.Business.Description; a.Terms = direct.Business.Terms; await db.SaveChangesAsync();
        }
        var first = await reader.HistoryAsync(account, agreement); var last = await reader.HistoryAsync(account, agreement, 999);
        var events = await Call("agreements_followup_history", Args());
        Check(first.Total == 29 && first.Items.Length == 25 && last.Page == 2 && last.Items.Length == 4, "history true total and clamped last page");
        Check(first.Items.Select(h => h.Id).Concat(last.Items.Select(h => h.Id)).SequenceEqual(first.Items.Concat(last.Items).OrderBy(h => h.CreatedUtc).ThenBy(h => h.Id).Select(h => h.Id)), "equal timestamps have stable unique ID ordering");
        Check(events.GetProperty("items").EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).SequenceEqual(first.Items.Select(h => h.Id)), "actual MCP history equals guarded Application");
        var relations = await reader.RelationsAsync(account, AgreementRelationKind.Building, "Duplicate lookup label");
        var finalRelations = await reader.RelationsAsync(account, AgreementRelationKind.Building, "Duplicate lookup label", 2);
        var relArgs = new Dictionary<string, object?> { ["accountId"] = account, ["kind"] = "Building", ["search"] = "Duplicate lookup label" };
        var relWire = await Call("agreements_relations", relArgs);
        Check(relations.Total == 28 && relations.Items.Length == 25 && finalRelations.Items.Length == 3 && relations.Items.Concat(finalRelations.Items).Select(r => r.Id).Distinct().Count() == 28, "large same-name lookup preserves all distinct IDs and paging");
        Check(relWire.GetProperty("totalCount").GetInt32() == relations.Total && relWire.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).SequenceEqual(relations.Items.Select(r => r.Id)), "MCP lookup matches actual authorized distinct set");
        Check(!(await reader.RelationsAsync(account, AgreementRelationKind.Building)).Items.Any(r => r.Id == hiddenBuilding || r.Id == foreignBuilding), "unlinked administrator catalog and foreign rows excluded");
        foreach (var kind in Enum.GetValues<AgreementRelationKind>()) await reader.RelationsAsync(account, kind);
        Check((await reader.RelationsAsync(account, AgreementRelationKind.Unit)).Total == 0, "null/unlinked references do not invent options");
        foreach (var text in new[] { wire.GetRawText(), events.GetRawText(), relWire.GetRawText() })
            Check(!new[] { "actorUser", "assignedUser", "ownerName", "ownerUser", "\"comment\"", "PRIVATE_", "storageKey", "recipient", "canEdit", "revision", "grants" }.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)), "projection excludes private and action fields");
        Guid foreignAgreement;
        await using (var db = admin.CreateDbContext()) foreignAgreement = await db.Agreements.Where(a => a.AccountId == foreign).Select(a => a.Id).FirstAsync();
        foreach (var tool in new[] { "agreements_details", "agreements_followup_history" })
        {
            var invalid = Args(); invalid["agreementId"] = Guid.NewGuid(); Check(Error(await client.CallToolAsync(tool, invalid)) == "RESOURCE_UNAVAILABLE", "unknown concrete resource safe error");
            invalid = Args(); invalid["agreementId"] = foreignAgreement; Check(Error(await client.CallToolAsync(tool, invalid)) == "RESOURCE_UNAVAILABLE", "foreign agreement under local account rejected");
            invalid = Args(); invalid["accountId"] = foreign; Check(Error(await client.CallToolAsync(tool, invalid)) == "RESOURCE_UNAVAILABLE", "foreign account rejected");
        }
        var spoof = Args(); spoof["actorUserId"] = owner;
        Check(Error(await client.CallToolAsync("agreements_followup_history", spoof)) == "INVALID_ARGUMENT", "history identity injection rejected");
        relArgs["kind"] = "Members"; Check(Error(await client.CallToolAsync("agreements_relations", relArgs)) == "INVALID_ARGUMENT", "no people/options dispatcher");
        relArgs["kind"] = "Building"; relArgs["accountId"] = foreign; Check(Error(await client.CallToolAsync("agreements_relations", relArgs)) == "RESOURCE_UNAVAILABLE", "foreign lookup rejected");
        await using (var db = admin.CreateDbContext())
        {
            Check((await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision == revision && await db.AgreementDeadlineHistory.CountAsync(h => h.DeadlineId == deadline) == 29, "reads leave revisions/history unchanged");
            var grant = await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user); grant.Level = AgreementAccessLevel.Edit; await db.SaveChangesAsync();
        }
        Check((await reader.DetailsAsync(account, agreement)).Business == direct.Business, "Edit does not broaden metadata");
        await using (var db = admin.CreateDbContext())
        {
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user)); await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_details", Args())) == "RESOURCE_UNAVAILABLE" && Error(await client.CallToolAsync("agreements_followup_history", Args())) == "RESOURCE_UNAVAILABLE", "same OAuth token respects revoked concrete grant");
        await using (var db = admin.CreateDbContext())
        {
            db.AgreementAccess.Add(new() { AccountId = account, AgreementId = agreement, UserId = user, Level = AgreementAccessLevel.Read });
            db.AgreementDeadlineHistory.RemoveRange(await db.AgreementDeadlineHistory.Where(h => h.DeadlineId == deadline).ToArrayAsync());
            db.AgreementDeadlines.Remove(await db.AgreementDeadlines.SingleAsync(d => d.Id == deadline));
            db.Agreements.RemoveRange(await db.Agreements.Where(a => createdAgreements.Contains(a.Id)).ToArrayAsync());
            await db.SaveChangesAsync();
            db.Buildings.RemoveRange(await db.Buildings.Where(b => buildings.Contains(b.Id) || b.Id == hiddenBuilding || b.Id == foreignBuilding).ToArrayAsync()); await db.SaveChangesAsync();
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"ActorUserId\" FROM agreement_deadline_history", "SELECT \"Comment\" FROM agreement_deadline_history", "SELECT \"ActorUserId\" FROM agreement_indices", "UPDATE agreement_deadline_history SET \"Kind\"=1 WHERE false" })
        {
            try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("private SQL accepted"); }
            catch (PostgresException e) when (e.SqlState == "42501") { Check(true, "least SQL and write guard retained"); }
        }
        Console.WriteLine("Agreement extensions Application/real OAuth-MCP-PostgreSQL checks passed.");
    }
    static JsonElement Data(CallToolResult r) => r.IsError == true ? throw new Exception(JsonSerializer.Serialize(r)) : r.StructuredContent!.Value;
    static string Error(CallToolResult r) => r.IsError == true ? ((TextContentBlock)r.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL extensions: " + message); Console.WriteLine("PASS extensions: " + message); }
}
