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

static class FinanceReadChecks
{
    public static async Task Run(Factory admin, string runtime, Guid user, Guid account, Guid agreement, Guid foreign, Func<Task<McpClient>> connect)
    {
        var guarded = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).AddInterceptors(new ReadOnlyGuard()).Options);
        string adminConnection; await using (var context = admin.CreateDbContext()) adminConnection = context.Database.GetConnectionString()!;
        var adminGuard = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(adminConnection).AddInterceptors(new ReadOnlyGuard()).Options);
        AuthorizationService Auth(Guid actor, Factory factory) => new(new FrozenActor(actor, account), new AuthorizationData(factory), TimeProvider.System);
        AgreementFinanceReader Reader(Guid actor) => new(Auth(actor, guarded), new AgreementFinanceReadData(guarded));
        var reader = Reader(user); Guid owner, revision; var line = Guid.NewGuid(); var index = Guid.NewGuid();
        await using var client = await connect();
        Dictionary<string, object?> Args() => new() { ["accountId"] = account, ["agreementId"] = agreement };
        Dictionary<string, object?> ForecastArgs() { var a = Args(); a["from"] = "2026-01-01"; a["to"] = "2026-03-31"; return a; }
        await using (var db = admin.CreateDbContext())
        {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); owner = a.OwnerUserId; revision = a.Revision; a.Direction = AgreementDirection.Income; a.Currency = "NOK";
            db.AgreementLines.Add(new() { Id = line, AccountId = account, AgreementId = agreement, ActivatedUtc = DateTimeOffset.UtcNow });
            for (var n = 1; n <= 30; n++)
            {
                db.AgreementLineVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, LineId = line, Sequence = n,
                    EffectiveFrom = new(2026, n == 3 ? 3 : 1, 1), StartDate = new(2026, 1, 1), FirstPayableDate = new(2026, 1, 15),
                    Status = n is 1 or 3 ? AgreementLineStatus.Active : AgreementLineStatus.Draft, Name = "Rent", Description = "Stored line description",
                    Frequency = AgreementFrequency.Monthly, Anchor = AgreementAnchor.Calendar, AnchorDate = new(1, 1, 1), BillingTiming = AgreementBillingTiming.Advance,
                    IndexRegulated = true, ActorUserId = owner, Reason = "PRIVATE_REASON", RecordedUtc = DateTimeOffset.UtcNow });
                db.AgreementPriceVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, LineId = line, Sequence = n,
                    EffectiveFrom = new(2026, n == 3 ? 3 : 1, 1), Quantity = 1.3333m, UnitPrice = n == 1 ? 10.1234m : n == 3 ? 20m : 9999m,
                    ActorUserId = owner, Reason = "PRIVATE_REASON", RecordedUtc = DateTimeOffset.UtcNow });
            }
            db.AgreementIndexRecords.Add(new() { Id = index, AccountId = account, Code = "READ_TEST", Name = "Index", Resolution = AgreementIndexResolution.Month, ActorUserId = owner });
            db.AgreementIndexSelections.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, Sequence = 1, IndexId = index, EffectiveFrom = new(2026, 1, 1), ActorUserId = owner });
            foreach (var month in new[] { 1, 2 }) db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = account, IndexId = index,
                Period = new(2026, month, 1), PeriodKey = Guid.NewGuid(), Value = month == 1 ? 100 : 110, Revision = 1, ActorUserId = owner });
            await db.SaveChangesAsync();
        }
        var lines = await reader.LinesAsync(account, agreement);
        var last = await reader.LinesAsync(account, agreement, line, 999, 999);
        Check(lines.Versions.TotalCount == 30 && lines.Prices.TotalCount == 30 && lines.Versions.Items.Length == 25 && last.Versions.Items.Length == 5 && last.Prices.Page == 2, "independent bounded version/price pages and real totals");
        var wire = Data(await client.CallToolAsync("agreements_financial_lines", Args()));
        Check(wire.GetProperty("data").GetProperty("versions").GetProperty("items").EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).SequenceEqual(lines.Versions.Items.Select(v => v.Id)), "real MCP line projection equals Application");
        await using (var db = admin.CreateDbContext()) {
            db.AgreementPriceVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, LineId = line, Sequence = 31,
                Independent = true, EffectiveFrom = new(2026, 2, 1), Quantity = 2, UnitPrice = 5, ActorUserId = owner }); await db.SaveChangesAsync();
        }
        var forecast = await reader.ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 3, 31));
        var original = new AgreementFinancialService(Auth(user, adminGuard), new AgreementFinancialData(adminGuard), TimeProvider.System);
        var direct = await original.ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 3, 31));
        Check(forecast.Income == direct.Income && forecast.Cost == 0 && forecast.Currency == direct.Currency && forecast.Periods.Select(p => p.Amount).SequenceEqual(direct.Periods.Select(p => p.Amount)), "exact GUI forecast rules: draft exclusion, index, partial included periods and rounding");
        Check(forecast.Periods.Any(p => p.UnitPrice == 5) && forecast.Periods.Any(p => p.Included && p.Amount == 0) && forecast.Periods.All(p => p.UnitPrice < 100), "draft price pairs never become projected prices");
        var forecastWire = Data(await client.CallToolAsync("agreements_forecast", ForecastArgs()));
        Check(forecastWire.GetProperty("data").GetProperty("income").GetDecimal() == direct.Income && forecastWire.GetProperty("coverage").GetString() == "CalculatedForecastNotActualTransactions", "MCP forecast is computed evidence, not transactions");
        Check((await Reader(owner).ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 3, 31))).Income == direct.Income, "owner and Read have existing same financial read scope");
        var partial = await reader.ForecastAsync(account, agreement, new(2026, 1, 20), new(2026, 1, 21));
        Check(partial.Periods.Any(p => p.From < partial.From || p.To > partial.To), "requested range selects overlapping whole logical periods rather than clipping amounts");
        foreach (var payload in new[] { wire, forecastWire }) Check(!new[] { "actorUser", "reason", "document", "storage", "revision", "canEdit", "PRIVATE_" }.Any(x => payload.GetRawText().Contains(x, StringComparison.OrdinalIgnoreCase)), "no private/write metadata");
        var extraLines = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = admin.CreateDbContext()) {
            foreach (var id in extraLines) {
                db.AgreementLines.Add(new() { Id = id, AccountId = account, AgreementId = agreement });
                db.AgreementLineVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, LineId = id, Sequence = 1,
                    Name = "Output limit", Status = AgreementLineStatus.Active, EffectiveFrom = new(2026, 1, 1), StartDate = new(2026, 1, 1), FirstPayableDate = new(2026, 1, 1),
                    Frequency = AgreementFrequency.Monthly, Anchor = AgreementAnchor.Calendar, AnchorDate = new(1, 1, 1), BillingTiming = AgreementBillingTiming.Advance, ActorUserId = owner });
                db.AgreementPriceVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, LineId = id, Sequence = 1,
                    EffectiveFrom = new(2026, 1, 1), Quantity = 1, UnitPrice = 1, ActorUserId = owner });
            }
            await db.SaveChangesAsync();
        }
        var outputLimit = ForecastArgs(); outputLimit["to"] = "2026-12-31";
        Check(Error(await client.CallToolAsync("agreements_forecast", outputLimit)) == "RESULT_TOO_LARGE", "500 calculation-period bound rejects entire result");
        await using (var db = admin.CreateDbContext()) {
            db.AgreementLineVersions.RemoveRange(await db.AgreementLineVersions.Where(v => extraLines.Contains(v.LineId)).ToArrayAsync());
            db.AgreementPriceVersions.RemoveRange(await db.AgreementPriceVersions.Where(v => extraLines.Contains(v.LineId)).ToArrayAsync()); await db.SaveChangesAsync();
            db.AgreementLines.RemoveRange(await db.AgreementLines.Where(l => extraLines.Contains(l.Id)).ToArrayAsync());
            (await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user)).Level = AgreementAccessLevel.Edit; await db.SaveChangesAsync();
        }
        Check((await reader.ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 3, 31))).Income == direct.Income, "Edit and Read have the same existing financial read data");
        var invalid = ForecastArgs(); invalid["to"] = "2028-01-01";
        Check(Error(await client.CallToolAsync("agreements_forecast", invalid)) == "INVALID_ARGUMENT", "forecast horizon bounded");
        invalid = ForecastArgs(); invalid["accountId"] = foreign;
        Check(Error(await client.CallToolAsync("agreements_forecast", invalid)) == "RESOURCE_UNAVAILABLE", "foreign finance account rejected");
        invalid = Args(); invalid["lineId"] = Guid.NewGuid(); Check(Error(await client.CallToolAsync("agreements_financial_lines", invalid)) == "RESOURCE_UNAVAILABLE", "unknown or foreign line cannot widen agreement scope");
        using (var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); try { await reader.ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 2, 1), cancelled.Token); throw new Exception("cancel accepted"); }
            catch (OperationCanceledException) { Check(true, "finance cancellation preserved"); }
        }
        await using (var db = admin.CreateDbContext()) {
            var a = await db.Agreements.SingleAsync(a => a.Id == agreement); Check(a.Revision == revision && !a.DeadlinesInitialized && !await db.AgreementBasisRecords.AnyAsync(b => b.AgreementId == agreement) && !await db.AgreementAdjustmentProposalRecords.AnyAsync(p => p.AgreementId == agreement), "no revision/init/basis/proposal generation");
            a.Direction = AgreementDirection.Cost; await db.SaveChangesAsync();
        }
        var cost = await reader.ForecastAsync(account, agreement, new(2026, 1, 1), new(2026, 3, 31));
        Check(cost.Cost == direct.Income && cost.Income == 0, "one currency and separate directions without netting");
        await using (var db = admin.CreateDbContext()) { (await db.Agreements.SingleAsync(a => a.Id == agreement)).Currency = null; await db.SaveChangesAsync(); }
        Check(Error(await client.CallToolAsync("agreements_forecast", ForecastArgs())) == "CALCULATION_UNAVAILABLE", "missing setup is not a zero forecast");
        await using (var db = admin.CreateDbContext()) {
            (await db.Agreements.SingleAsync(a => a.Id == agreement)).Currency = "NOK";
            db.AgreementPriceVersions.RemoveRange(await db.AgreementPriceVersions.Where(p => p.LineId == line).ToArrayAsync()); await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_forecast", ForecastArgs())) == "CALCULATION_UNAVAILABLE", "missing applicable price is not zero");
        await using (var db = admin.CreateDbContext()) {
            for (var n = 31; n <= 251; n++) db.AgreementLineVersions.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement,
                LineId = line, Sequence = n, Name = "Bounded draft", Status = AgreementLineStatus.Draft, EffectiveFrom = new(2026, 1, 1), StartDate = new(2026, 1, 1),
                FirstPayableDate = new(2026, 1, 1), Frequency = AgreementFrequency.Monthly, Anchor = AgreementAnchor.Calendar, AnchorDate = new(1, 1, 1), ActorUserId = owner });
            await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_forecast", ForecastArgs())) == "RESULT_TOO_LARGE", "input snapshot bound rejects entire forecast before missing-price calculation");
        await using (var db = admin.CreateDbContext()) {
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(g => g.AgreementId == agreement && g.UserId == user)); await db.SaveChangesAsync();
        }
        Check(Error(await client.CallToolAsync("agreements_financial_lines", Args())) == "RESOURCE_UNAVAILABLE" && Error(await client.CallToolAsync("agreements_forecast", ForecastArgs())) == "RESOURCE_UNAVAILABLE", "same token sees revoked current grant");
        await using (var db = admin.CreateDbContext()) {
            db.AgreementAccess.Add(new() { AccountId = account, AgreementId = agreement, UserId = user, Level = AgreementAccessLevel.Read });
            db.AgreementLineVersions.RemoveRange(await db.AgreementLineVersions.Where(v => v.LineId == line).ToArrayAsync());
            db.AgreementIndexSelections.RemoveRange(await db.AgreementIndexSelections.Where(s => s.AgreementId == agreement).ToArrayAsync()); await db.SaveChangesAsync();
            db.AgreementLines.Remove(await db.AgreementLines.SingleAsync(l => l.Id == line));
            db.AgreementIndexValueRecords.RemoveRange(await db.AgreementIndexValueRecords.Where(v => v.IndexId == index).ToArrayAsync()); await db.SaveChangesAsync();
            db.AgreementIndexRecords.Remove(await db.AgreementIndexRecords.SingleAsync(i => i.Id == index)); await db.SaveChangesAsync();
        }
        await using var sql = new NpgsqlConnection(runtime); await sql.OpenAsync();
        foreach (var statement in new[] { "SELECT \"ActorUserId\" FROM agreement_line_versions", "SELECT \"Reason\" FROM agreement_price_versions", "SELECT * FROM agreement_line_documents", "UPDATE agreement_price_versions SET \"UnitPrice\"=1 WHERE false" }) {
            try { await using var cmd = new NpgsqlCommand(statement, sql); await cmd.ExecuteNonQueryAsync(); throw new Exception("private SQL accepted"); }
            catch (PostgresException e) when (e.SqlState == "42501") { Check(true, "least financial SELECT and no write"); }
        }
        Console.WriteLine("Finance read actual Application/OAuth/MCP/PostgreSQL checks passed.");
    }
    static JsonElement Data(CallToolResult r) => r.IsError == true ? throw new Exception(JsonSerializer.Serialize(r)) : r.StructuredContent!.Value;
    static string Error(CallToolResult r) => r.IsError == true ? ((TextContentBlock)r.Content[0]).Text : "not error";
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL finance: " + message); Console.WriteLine("PASS finance: " + message); }
}
