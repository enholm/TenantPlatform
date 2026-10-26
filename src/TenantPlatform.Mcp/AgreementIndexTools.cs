using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Mcp;

public sealed record IndexRegisterResult(string SchemaVersion, Guid AccountId, string Coverage, bool SnapshotConsistent, FinancePage<IndexEntry> Data);
public sealed record IndexValuesResult(string SchemaVersion, Guid AccountId, Guid IndexId, IndexValueView View, DateOnly? From, DateOnly? To, string Coverage, bool SnapshotConsistent, FinancePage<IndexValueEntry> Data);
public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> IndexCatalog() => [
        Tool<IndexRegisterResult>("agreements_indices", "Find account index register entries. Any active member has the existing account-wide index read access, independent of agreement ownership/grants. Code/name search is literal case-sensitive substring, optional resolution Month/Quarter/Year. Fixed pages25, real filtered totals. Code, name, description, resolution and sanitized SourceOrigin only: HTTP(S) origin without userinfo; text/path/query/fragment withheld, null means withheld or absent. No fetching/import. Read only; no people, administration flags or report adapter.",
            """{"type":"object","additionalProperties":false,"required":["accountId"],"properties":{"accountId":{"type":"string","format":"uuid"},"search":{"type":"string","maxLength":200},"resolution":{"type":"string","enum":["Month","Quarter","Year"]},"page":{"type":"integer","minimum":1,"maximum":10000}}}"""),
        Tool<IndexValuesResult>("agreements_index_values", "Read stored index values for one account index. view Current(default) selects highest non-superseded revision per PeriodKey BEFORE optional inclusive from/to period filters; History returns all stored revisions including superseded. Period is resolution-aligned index period; PublishedDate is optional publication date, NOT an as-of cutoff. A correction can move Period while retaining PeriodKey. Stable Period descending/Revision descending/Id pages25 with real totals. No future estimates, filled gaps or historical as-of reconstruction. Existing forecast uses current revisions and latest Period<=calculation date, not PublishedDate; this tool does not calculate forecasts. No actor/reason. Read only, no import/fetch or initialization.",
            """{"type":"object","additionalProperties":false,"required":["accountId","indexId"],"properties":{"accountId":{"type":"string","format":"uuid"},"indexId":{"type":"string","format":"uuid"},"view":{"type":"string","enum":["Current","History"]},"from":{"type":"string","minLength":10,"maxLength":10},"to":{"type":"string","minLength":10,"maxLength":10},"page":{"type":"integer","minimum":1,"maximum":10000}}}""")];
    private static async Task<object> IndexAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var reader = services.GetRequiredService<AgreementIndexReader>();
        if (name == "agreements_indices") return new IndexRegisterResult("1", account, "AccountIndexRegister", false,
            await reader.RegisterAsync(account, Search(args), EnumValue<AgreementIndexResolution>(args, "resolution"), Page(args), ct));
        var index = Reference(args, "indexId") ?? throw new ArgumentException();
        var view = EnumValue<IndexValueView>(args, "view") ?? IndexValueView.Current; var from = Date(args, "from"); var to = Date(args, "to");
        return new IndexValuesResult("1", account, index, view, from, to, view == IndexValueView.Current ? "CurrentStoredRevisions" : "StoredRevisionHistory", false,
            await reader.ValuesAsync(account, index, view, from, to, Page(args), ct));
    }
}
