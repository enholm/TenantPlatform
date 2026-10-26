using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Mcp;

public sealed record FinanceLinesResult(string SchemaVersion, Guid AccountId, Guid AgreementId, string Coverage,
    bool SnapshotConsistent, FinanceLines Data);
public sealed record FinanceForecastResult(string SchemaVersion, Guid AccountId, Guid AgreementId, string Coverage,
    bool Complete, bool SnapshotConsistent, FinanceForecast Data);
public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> FinanceCatalog() => [
        Tool<FinanceLinesResult>("agreements_financial_lines", "Read stored financial line versions and price history for one authorized agreement. Two independently paged streams, 25 each; true totals, stable LineId/Sequence/Id. Optional exact lineId. LineCount is the agreement-wide line count, before this optional filter. EffectiveFrom and RecordedUtc differ; highest sequence is not automatically today's value. Draft/active/ended/deactivated are stored states. Independent prices apply independently of paired line sequence; forecasts exclude abandoned draft pairs. Quantity/unit price are not invoices or payments. Currency/direction belong to this agreement. No people, reasons, documents or action rights. Read only, no generation.",
            """{"type":"object","additionalProperties":false,"required":["accountId","agreementId"],"properties":{"accountId":{"type":"string","format":"uuid"},"agreementId":{"type":"string","format":"uuid"},"lineId":{"type":"string","format":"uuid"},"versionsPage":{"type":"integer","minimum":1,"maximum":10000},"pricesPage":{"type":"integer","minimum":1,"maximum":10000}}}"""),
        Tool<FinanceForecastResult>("agreements_forecast", "Calculate the existing agreement forecast for inclusive from/to (yyyy-MM-dd), at most 366 days difference. Computed preview, NOT invoices/payments or generated bases. Uses recorded non-draft effective line snapshots, applicable paired/independent prices and existing automatic index projection; Included rows have zero payable amount. Overlapping reference periods may extend outside requested dates; do not clip/recalculate amounts. Existing cumulative two-decimal AwayFromZero rounding. Separate Income/Cost, one agreement currency; no currency conversion, netting or added tax calculation. Missing setup/price fails CALCULATION_UNAVAILABLE, never a fabricated zero. Max250 line snapshots/500 prices/100 index selections/100 indices/1000 index values/500 calculation periods before overlap filtering, otherwise whole RESULT_TOO_LARGE. No persons/documents/raw snapshots. Read only; no initialization, proposals or persistence. No financial report-page adapter is provided.",
            """{"type":"object","additionalProperties":false,"required":["accountId","agreementId","from","to"],"properties":{"accountId":{"type":"string","format":"uuid"},"agreementId":{"type":"string","format":"uuid"},"from":{"type":"string","minLength":10,"maxLength":10},"to":{"type":"string","minLength":10,"maxLength":10}}}""")];
    private static async Task<object> FinanceAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var id = Reference(args, "agreementId") ?? throw new ArgumentException();
        var reader = services.GetRequiredService<AgreementFinanceReader>();
        if (name == "agreements_forecast")
            return new FinanceForecastResult("1", account, id, "CalculatedForecastNotActualTransactions", true, false,
                await reader.ForecastAsync(account, id, Date(args, "from") ?? throw new ArgumentException(), Date(args, "to") ?? throw new ArgumentException(), ct));
        int PageAt(string key) => args.TryGetValue(key, out var value) ? Page(new Dictionary<string, JsonElement> { ["page"] = value }) : 1;
        return new FinanceLinesResult("1", account, id, "StoredLineAndPriceHistory", false,
            await reader.LinesAsync(account, id, Reference(args, "lineId"), PageAt("versionsPage"), PageAt("pricesPage"), ct));
    }
}
