using System.Text.Json;
using System.Text.Json.Nodes;
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

static class BasisReadChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid agreement, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        AuthorizationService Auth(Guid u, Factory f) => new(new FrozenActor(u, account), new AuthorizationData(f), TimeProvider.System);
        var reader = new AgreementBasisReader(Auth(user, guarded), new AgreementBasisReadData(guarded));
        Guid owner, party, foreignAgreement, foreignParty; var line = Guid.NewGuid(); var version = Guid.NewGuid(); var price = Guid.NewGuid();
        var crossLine = Guid.NewGuid(); var crossVersion = Guid.NewGuid(); var crossPrice = Guid.NewGuid();
        var hiddenAgreement = Guid.NewGuid(); var foreignBasis = Guid.NewGuid(); var hiddenBasis = Guid.NewGuid(); var correction = Guid.NewGuid(); var cost = Guid.NewGuid();
        await using (var db = admin.CreateDbContext()) {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); owner = a.OwnerUserId; party = a.CounterpartyOrganizationId; a.Direction = AgreementDirection.Income; a.Currency = "NOK";
            var f = await db.Agreements.FirstAsync(a => a.AccountId == foreign); foreignAgreement = f.Id; foreignParty = f.CounterpartyOrganizationId;
            db.Agreements.Add(new() { Id = hiddenAgreement, AccountId = account, OwnerUserId = owner, CounterpartyOrganizationId = party, Title = "UNAVAILABLE_AGREEMENT", Type = AgreementType.Supplier, Status = AgreementStatus.Active, CreatedByUserId = owner, UpdatedByUserId = owner, StartDate = new(2026,1,1), CurrentPeriodStartDate = new(2026,1,1), Revision = Guid.NewGuid() });
            db.AgreementLines.Add(new() { Id = crossLine, AccountId = account, AgreementId = hiddenAgreement });
            db.AgreementLineVersions.Add(new() { Id = crossVersion, AccountId = account, AgreementId = hiddenAgreement, LineId = crossLine, Sequence = 1, Name = "PRIVATE_OTHER_LINE", ActorUserId = owner });
            db.AgreementPriceVersions.Add(new() { Id = crossPrice, AccountId = account, AgreementId = hiddenAgreement, LineId = crossLine, Sequence = 1, Quantity = 1, UnitPrice = 123, ActorUserId = owner });
            db.AgreementLines.Add(new() { Id = line, AccountId = account, AgreementId = agreement });
            db.AgreementLineVersions.Add(new() { Id = version, AccountId = account, AgreementId = agreement, LineId = line, Sequence = 1, Name = "Stored charge", Status = AgreementLineStatus.Active,
                StartDate = new(2026,1,1), EffectiveFrom = new(2026,1,1), FirstPayableDate = new(2026,1,1), Frequency = AgreementFrequency.Monthly, BillingTiming = AgreementBillingTiming.Advance, Anchor = AgreementAnchor.Calendar, AnchorDate = new(1,1,1), ActorUserId = owner });
            db.AgreementPriceVersions.Add(new() { Id = price, AccountId = account, AgreementId = agreement, LineId = line, Sequence = 1, EffectiveFrom = new(2026,1,1), Quantity = 1, UnitPrice = 100, ActorUserId = owner }); await db.SaveChangesAsync();
        }
        var originalService = new AgreementFinancialService(Auth(owner, admin), new AgreementFinancialData(admin), TimeProvider.System);
        var generated = await originalService.GenerateBasisAsync(account, agreement, AgreementDirection.Income, new(2026,1,1), new(2026,1,31));
        var basis = generated.Created.Single(); var baselineGui = await originalService.GetBasisAsync(account, basis);
        string json; DateTimeOffset stamp; var created = new List<Guid> { basis, correction, cost, foreignBasis, hiddenBasis }; var historicalSnapshot = Guid.NewGuid();
        await using (var db = admin.CreateDbContext()) {
            var b = await db.AgreementBasisRecords.SingleAsync(b => b.Id == basis); var snap = await db.AgreementBasisSnapshotRecords.SingleAsync(s => s.BasisId == basis); json = snap.DataJson; stamp = b.GeneratedUtc;
            b.Status = AgreementBasisStatus.Approved; b.Revision = 2;
            var updated = JsonNode.Parse(json)!.AsObject(); updated["Events"]![0]!["Amount"] = 150; updated["Events"]![0]!["TargetAmount"] = 150;
            db.AgreementBasisSnapshotRecords.Add(new() { Id = historicalSnapshot, AccountId = account, AgreementId = agreement, BasisId = basis, Revision = 2, DataJson = updated.ToJsonString(), ActorUserId = owner, Reason = "PRIVATE_REASON", Fingerprint = "PRIVATE_FINGERPRINT" });
            void Add(Guid id, Guid a, Guid ag, AgreementDirection direction, AgreementBasisStatus status, string payload, Guid? original = null) {
                db.AgreementBasisRecords.Add(new() { Id = id, AccountId = a, AgreementId = ag, Direction = direction, Status = status, InvoiceDate = new(2026,1,1), Revision = 1, GeneratedUtc = stamp, GeneratedByUserId = owner, OriginalBasisId = original, Reason = "PRIVATE_REASON" });
                db.AgreementBasisSnapshotRecords.Add(new() { Id = Guid.NewGuid(), AccountId = a, AgreementId = ag, BasisId = id, Revision = 1, DataJson = payload, ActorUserId = owner, Reason = "PRIVATE_REASON", Fingerprint = "PRIVATE_FINGERPRINT" });
            }
            for (var n = 0; n < 505; n++) { var id = Guid.NewGuid(); created.Add(id); Add(id, account, agreement, AgreementDirection.Income, (AgreementBasisStatus)(n % 3), json); }
            var c = JsonNode.Parse(json)!; c["Events"]![0]!["Amount"] = -50; c["Events"]![0]!["PreviousAmount"] = 150; c["Events"]![0]!["TargetAmount"] = 100; c["PreviousSnapshotIds"] = new JsonArray(JsonValue.Create(historicalSnapshot));
            Add(correction, account, agreement, AgreementDirection.Income, AgreementBasisStatus.Approved, c.ToJsonString(), basis);
            var k = JsonNode.Parse(json)!; k["Direction"] = (int)AgreementDirection.Cost; k["Events"]![0]!["Segments"]![0]!["Direction"] = (int)AgreementDirection.Cost;
            Add(cost, account, agreement, AgreementDirection.Cost, AgreementBasisStatus.Cancelled, k.ToJsonString());
            Add(foreignBasis, foreign, foreignAgreement, AgreementDirection.Income, AgreementBasisStatus.Draft, json);
            Add(hiddenBasis, account, hiddenAgreement, AgreementDirection.Income, AgreementBasisStatus.Draft, json);
            await db.SaveChangesAsync();
        }
        Guid revision; int auditCount, eventCount, snapshotCount;
        await using (var db = admin.CreateDbContext()) { revision = (await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision; auditCount = await db.AuditLogs.CountAsync(); eventCount = await db.AgreementBasisEventRecords.CountAsync(); snapshotCount = await db.AgreementBasisSnapshotRecords.CountAsync(); }
        var filter = new BasisReadFilter(AgreementDirection.Income, agreement);
        var first = await reader.ListAsync(account, filter); var last = await reader.ListAsync(account, filter, 10000);
        Check(first.TotalCount == 507 && first.Items.Length == 25 && last.Page == 21 && last.Items.Length == 7 && first.NextPage == 2 && !first.Complete, "more than500 true total and last page, no hidden truncation");
        var all = new List<StoredBasisSummary>(); for (var p = 1; p <= 21; p++) all.AddRange((await reader.ListAsync(account, filter, p)).Items);
        Check(all.Count == 507 && all.Select(b => b.Id).Distinct().Count() == 507 && all.Select(b => b.Id).SequenceEqual(all.Select(b => b.Id).Order()), "stable ID tie-break on equal GeneratedUtc, every row reachable");
        var detail = await reader.DetailAsync(account, basis); var old = await reader.DetailAsync(account, basis, 1);
        Check(detail.Summary == all.Single(b => b.Id == basis) && detail.Summary is { Amount: 150, Currency: "NOK", Direction: AgreementDirection.Income, CurrentRevision: 2, SnapshotRevision: 2, Status: AgreementBasisStatus.Approved, Freshness: BasisFreshness.NotEvaluated }, "list/detail match stored money/currency/direction/status/revision with explicit unassessed freshness");
        Check(old.Summary.Amount == 100 && old.Summary.CurrentRevision == 2 && old.Summary.SnapshotRevision == 1 && old.Summary.Status == AgreementBasisStatus.Approved && baselineGui.Snapshots.Count == 1, "exact historical snapshot does not fabricate historical header status");
        var delta = await reader.DetailAsync(account, correction);
        Check(delta.Summary.Amount == -50 && delta.Events.Single() is { PreviousAmount: 150, TargetAmount: 100 } && delta.Events.Single().Segments.Sum(s => s.Amount) == 100 && delta.Summary.OriginalBasisId == basis, "stored correction delta remains separate from target segments and original");
        Check(detail.Summary.PeriodFrom == new DateOnly(2026,1,1) && detail.Summary.PeriodTo == new DateOnly(2026,1,31), "stored segment period bounds");
        Check((await reader.ListAsync(account, new(AgreementDirection.Cost))).TotalCount == 1 && (await reader.ListAsync(account, filter with { From = new(2026,2,1) })).TotalCount == 0, "stored direction and inclusive invoice date filter, empty legitimate");
        Check((await reader.ListAsync(account, filter with { Status = AgreementBasisStatus.Cancelled })).Items.All(b => b.Status == AgreementBasisStatus.Cancelled), "typed stored status filter");
        await using var client = await connect();
        Dictionary<string,object?> ListArgs() => new() { ["accountId"] = account, ["direction"] = "Income", ["agreementId"] = agreement };
        Dictionary<string,object?> DetailArgs(Guid id) => new() { ["accountId"] = account, ["basisId"] = id };
        var wire = Data(await client.CallToolAsync("agreements_bases", ListArgs()));
        Check(wire.GetProperty("data").GetProperty("totalCount").GetInt32() == 507 && wire.GetProperty("data").GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).SequenceEqual(first.Items.Select(b => b.Id)), "real OAuth/MCP equals Application list");
        var dw = Data(await client.CallToolAsync("agreements_basis", DetailArgs(basis)));
        Check(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(detail, Json), dw.GetProperty("data")), "real MCP exact detail projection equals Application");
        Check(!new[] { "DataJson", "Fingerprint", "Actor", "Reason", "Document", "CanApprove", "Stale", "NeedsCorrection", "PRIVATE_", "EventKey", "PreviousSnapshotIds" }.Any(s => dw.GetRawText().Contains(s, StringComparison.OrdinalIgnoreCase)), "private fields and false freshness flags absent");
        foreach (var id in new[] { foreignBasis, hiddenBasis, Guid.NewGuid() }) Check(Error(await client.CallToolAsync("agreements_basis", DetailArgs(id))) == "RESOURCE_UNAVAILABLE", "foreign/hidden/unknown basis same safe error");
        var bad = ListArgs(); bad["agreementId"] = hiddenAgreement; Check(Error(await client.CallToolAsync("agreements_bases", bad)) == "RESOURCE_UNAVAILABLE", "concrete unavailable agreement cannot become a valid empty filter");
        bad = ListArgs(); bad["accountId"] = foreign; Check(Error(await client.CallToolAsync("agreements_bases", bad)) == "RESOURCE_UNAVAILABLE", "foreign account rejected");
        bad = DetailArgs(basis); bad["revision"] = 999; Check(Error(await client.CallToolAsync("agreements_basis", bad)) == "RESOURCE_UNAVAILABLE", "unknown revision safely rejected");
        var testId = created.First(id => id != basis && id != correction && id != cost && id != foreignBasis && id != hiddenBasis);
        async Task Replace(string payload) { await using var db = admin.CreateDbContext(); (await db.AgreementBasisSnapshotRecords.SingleAsync(s => s.BasisId == testId)).DataJson = payload; await db.SaveChangesAsync(); }
        foreach (var mutate in new Action<JsonNode>[] {
            n => n["AccountId"] = foreign, n => n["CounterpartyId"] = foreignParty, n => n["AgreementId"] = hiddenAgreement,
            n => n["SchemaVersion"] = 2, n => n.AsObject().Remove("Events"), n => n["Events"]![0]!["Amount"] = "not-a-number",
            n => { n["Events"]![0]!["LineId"] = Guid.NewGuid(); n["Events"]![0]!["Segments"]![0]!["LineId"] = n["Events"]![0]!["LineId"]!.DeepClone(); },
            n => { n["Events"]![0]!["LineId"] = crossLine; var s = n["Events"]![0]!["Segments"]![0]!; s["LineId"] = crossLine; s["LineVersionId"] = crossVersion; s["PriceVersionId"] = crossPrice; },
            n => n["PreviousSnapshotIds"] = new JsonArray(JsonValue.Create(Guid.NewGuid())) }) {
            var node = JsonNode.Parse(json)!; mutate(node); await Replace(node.ToJsonString());
            Check(Error(await client.CallToolAsync("agreements_basis", DetailArgs(testId))) == "SNAPSHOT_UNAVAILABLE", "invalid/unknown/cross-scope snapshot fails wholly, never zero");
        }
        var huge = JsonNode.Parse(json)!; huge["AccountName"] = new string('X', 70000); await Replace(huge.ToJsonString());
        Check(Error(await client.CallToolAsync("agreements_basis", DetailArgs(testId))) == "RESULT_TOO_LARGE", "oversized jsonb bounded before materialization");
        var privateFields = JsonNode.Parse(json)!;
        privateFields["Events"]![0]!["Adjustments"] = new JsonArray(new JsonObject { ["ActorUserId"] = owner, ["Reason"] = "PRIVATE_REASON", ["CalculationJson"] = "PRIVATE_MODEL_CONTENT" });
        await Replace(privateFields.ToJsonString());
        Check(!Data(await client.CallToolAsync("agreements_basis", DetailArgs(testId))).GetRawText().Contains("PRIVATE_"), "private embedded proposal content never becomes a transport field");
        var zero = JsonNode.Parse(json)!; zero["Events"]![0]!["Segments"] = new JsonArray(); zero["Events"]![0]!["Amount"] = 0; zero["Events"]![0]!["PreviousAmount"] = 0; zero["Events"]![0]!["TargetAmount"] = 0;
        await Replace(zero.ToJsonString());
        var zeroResult = Data(await client.CallToolAsync("agreements_basis", DetailArgs(testId))).GetProperty("data").GetProperty("summary");
        Check(zeroResult.GetProperty("amount").GetDecimal() == 0 && zeroResult.GetProperty("periodFrom").ValueKind == JsonValueKind.Null && zeroResult.GetProperty("freshness").GetString() == "NotEvaluated", "valid stored zero without segments has null period, unlike parse failure");
        await Replace(json);
        using (var cancellation = new CancellationTokenSource()) {
            cancellation.Cancel(); try { await reader.DetailAsync(account, basis, ct: cancellation.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { Check(true, "basis cancellation preserved"); }
        }
        await using (var db = admin.CreateDbContext()) Check(await db.AuditLogs.CountAsync() == auditCount && await db.AgreementBasisEventRecords.CountAsync() == eventCount && await db.AgreementBasisSnapshotRecords.CountAsync() == snapshotCount && (await db.Agreements.SingleAsync(a => a.Id == agreement)).Revision == revision && !(await db.Agreements.SingleAsync(a => a.Id == agreement)).DeadlinesInitialized, "no read audit/init/generation/revision changes");
        var revoking = new AgreementBasisReader(Auth(user, guarded), new RevokeDuringRead(new AgreementBasisReadData(guarded), async () => {
            await using var db = admin.CreateDbContext(); db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user)); await db.SaveChangesAsync();
        }));
        try { await revoking.ListAsync(account, filter); throw new Exception("revoked snapshot returned"); } catch (UnauthorizedAccessException) { Check(true, "grant revoked during read snapshot is rejected by fresh final visibility"); }
        await using (var db = admin.CreateDbContext()) { db.AgreementAccess.Add(new() { AccountId = account, AgreementId = agreement, UserId = user, Level = AgreementAccessLevel.Read }); await db.SaveChangesAsync(); }
        await using (var db = admin.CreateDbContext()) { db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user)); await db.SaveChangesAsync(); }
        Check(Error(await client.CallToolAsync("agreements_basis", DetailArgs(basis))) == "RESOURCE_UNAVAILABLE", "same token observes resource revocation");
        var global = ListArgs(); global.Remove("agreementId"); Check(Data(await client.CallToolAsync("agreements_bases", global)).GetProperty("data").GetProperty("totalCount").GetInt32() == 0, "revoked grant filters before totals and paging");
        await using (var db = admin.CreateDbContext()) {
            db.AgreementAccess.Add(new() { AccountId = account, AgreementId = agreement, UserId = user, Level = AgreementAccessLevel.Read });
            db.AgreementBasisEventRecords.RemoveRange(await db.AgreementBasisEventRecords.Where(e => created.Contains(e.BasisId)).ToArrayAsync());
            db.AgreementBasisSnapshotRecords.RemoveRange(await db.AgreementBasisSnapshotRecords.Where(s => created.Contains(s.BasisId)).ToArrayAsync()); await db.SaveChangesAsync();
            foreach (var b in await db.AgreementBasisRecords.Where(b => created.Contains(b.Id)).ToArrayAsync()) b.OriginalBasisId = null; await db.SaveChangesAsync();
            db.AgreementBasisRecords.RemoveRange(await db.AgreementBasisRecords.Where(b => created.Contains(b.Id)).ToArrayAsync()); await db.SaveChangesAsync();
            db.AgreementPriceVersions.RemoveRange(await db.AgreementPriceVersions.Where(p => p.Id == price || p.Id == crossPrice).ToArrayAsync()); db.AgreementLineVersions.RemoveRange(await db.AgreementLineVersions.Where(v => v.Id == version || v.Id == crossVersion).ToArrayAsync()); await db.SaveChangesAsync();
            db.AgreementLines.RemoveRange(await db.AgreementLines.Where(l => l.Id == line || l.Id == crossLine).ToArrayAsync()); db.Agreements.Remove(await db.Agreements.SingleAsync(a => a.Id == hiddenAgreement)); await db.SaveChangesAsync();
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"GeneratedByUserId\" FROM agreement_bases", "SELECT \"Fingerprint\" FROM agreement_basis_snapshots", "SELECT \"Reason\" FROM agreement_basis_snapshots", "UPDATE agreement_bases SET \"Status\"=1 WHERE false" }) {
            try { await using var command = new NpgsqlCommand(statement, sql); await command.ExecuteNonQueryAsync(); throw new Exception("private SQL accepted"); } catch(PostgresException e) when(e.SqlState == "42501") { Check(true, "least SELECT and write denial"); }
        }
        Console.WriteLine("Stored bases actual Application/OAuth/MCP/PostgreSQL checks passed.");
    }
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    static JsonElement Data(CallToolResult r) => r.IsError == true ? throw new Exception(JsonSerializer.Serialize(r)) : r.StructuredContent!.Value;
    static string Error(CallToolResult r) => r.IsError == true ? ((TextContentBlock)r.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL basis: " + message); Console.WriteLine("PASS basis: " + message); }
}

// TEST-only interposition mutates a disposable grant after the real SQL snapshot was read.
sealed class RevokeDuringRead(IAgreementBasisReadData inner, Func<Task> revoke) : IAgreementBasisReadData
{
    public Task<Guid?> FindAgreementAsync(Guid account, Guid basis, CancellationToken ct) => inner.FindAgreementAsync(account, basis, ct);
    public Task<Guid[]> VisibleAsync(AgreementListScope scope, Guid[] agreements, CancellationToken ct) => inner.VisibleAsync(scope, agreements, ct);
    public async Task<IAgreementBasisReadSession> BeginAsync(AgreementListScope scope, CancellationToken ct) => new Session(await inner.BeginAsync(scope, ct), revoke);
    sealed class Session(IAgreementBasisReadSession inner, Func<Task> revoke) : IAgreementBasisReadSession
    {
        public async Task<FinancePage<BasisStorageRow>> ListAsync(BasisReadFilter filter, int page, CancellationToken ct) { var rows = await inner.ListAsync(filter, page, ct); await revoke(); return rows; }
        public Task<BasisStorageRow> DetailAsync(Guid basis, int? revision, CancellationToken ct) => inner.DetailAsync(basis, revision, ct);
        public Task<BasisReferenceFacts> ReferencesAsync(BasisReferenceRequest request, CancellationToken ct) => inner.ReferencesAsync(request, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
