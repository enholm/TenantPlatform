using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Mcp;

public sealed record AgreementQueryResult(string SchemaVersion, Guid AccountId, AgreementReportSelection Selection,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, AgreementReportRow[] Items);
public sealed record AgreementAggregateResult(string SchemaVersion, Guid AccountId, AgreementReportSelection Selection,
    string GroupBy, int TotalCount, AgreementReportGroup[] Groups, bool Complete, bool SnapshotConsistent,
    bool CountsUniqueAgreements, bool GroupsOverlap, string MissingKey);
public sealed record AgreementDetailsResult(string SchemaVersion, Guid AccountId, bool Complete, bool SnapshotConsistent, AgreementReportRow Agreement);

public sealed partial class ReadTools
{
    private const string ReportProperties = """
      "accountId":{"type":"string","format":"uuid"},"search":{"type":"string","maxLength":200},
      "status":{"type":"string","enum":["Draft","Active","Terminated","Expired"]},
      "type":{"type":"string","enum":["Lease","SoftwareLicense","ServiceMaintenance","Supplier","Other"]},
      "archive":{"type":"string","enum":["Include","Exclude","Only"],"default":"Include"},
      "buildingId":{"type":"string","format":"uuid"},"organizationElementId":{"type":"string","format":"uuid"},
      "geographicAreaId":{"type":"string","format":"uuid"},"unitId":{"type":"string","format":"uuid"},
      "missingBuilding":{"type":"boolean"},"endDateFrom":{"type":"string","minLength":10,"maxLength":10},
      "endDateTo":{"type":"string","minLength":10,"maxLength":10},"endsWithinMonths":{"type":"integer","minimum":1,"maximum":12}
      """;
    private static IReadOnlyList<Tool> ReportingCatalog() => [
        Tool<AgreementQueryResult>("agreements_query", "Read authorized agreements with account-bound building, unit, organization-element and geographic references. One value per dimension. Prefer this over agreements_list for reporting. All filters AND; exact dimension IDs, no descendants. Status is stored, Active is not a date calculation. EndDate bounds yyyy-MM-dd inclusive exclude null EndDate; endsWithinMonths uses account today through today.AddMonths(n), inclusive, and cannot combine with explicit dates. Archive Include by default. missingBuilding includes invalid foreign references without exposing them. Fixed pages of 25; real filtered total; no cross-call snapshot. Read only, no initialization.", ReportSchema("\"page\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"default\":1}")),
        Tool<AgreementAggregateResult>("agreements_aggregate", "Count unique authorized agreements grouped in SQL by Building or stored Status. Identical filters to agreements_query. Single-valued groups do not overlap; missing/foreign building is key missing with null name. Maximum 100 groups, otherwise RESULT_TOO_LARGE; never truncated. Complete refers to this filtered grouping, no snapshot across calls. Use this for totals instead of summing list pages. Read only, no initialization.", ReportSchema("\"groupBy\":{\"type\":\"string\",\"enum\":[\"Building\",\"Status\"]}", true)),
        Tool<AgreementDetailsResult>("agreements_details", "Read an authorized agreement's bounded summary: title, type/status, dates, archive and account-bound dimension references. No owner, grants, document data, financials, terms or internal fields. Complete means this explicit summary only. Unknown and inaccessible IDs give the same error. Read only, no initialization.", """{"type":"object","additionalProperties":false,"required":["accountId","agreementId"],"properties":{"accountId":{"type":"string","format":"uuid"},"agreementId":{"type":"string","format":"uuid"}}}""")];
    private static string ReportSchema(string extra, bool group = false) => "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"accountId\"" + (group ? ",\"groupBy\"" : "") + "],\"properties\":{" + ReportProperties + "," + extra + "}}";
    private static string[] ReportKeys(string name) => ["accountId", "search", "status", "type", "archive", "buildingId", "organizationElementId", "geographicAreaId", "unitId", "missingBuilding", "endDateFrom", "endDateTo", "endsWithinMonths", name == "agreements_query" ? "page" : "groupBy"];
    private static Guid? Reference(IDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var raw)) return null;
        if (raw.ValueKind != JsonValueKind.String || !Guid.TryParseExact(raw.GetString(), "D", out var id) || id == Guid.Empty) throw new ArgumentException();
        return id;
    }
    private static DateOnly? Date(IDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var raw)) return null;
        if (raw.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(raw.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException();
        return date;
    }
    private static async Task<object> ReportAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        if (name == "agreements_details")
        {
            var id = Reference(args, "agreementId") ?? throw new ArgumentException();
            return new AgreementDetailsResult("1", account, true, false, await services.GetRequiredService<IAgreementDetailsReader>().ReadSummaryAsync(account, id, ct));
        }
        var missing = false;
        if (args.TryGetValue("missingBuilding", out var rawMissing))
        {
            if (rawMissing.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException();
            missing = rawMissing.GetBoolean();
        }
        int? months = null;
        if (args.TryGetValue("endsWithinMonths", out var rawMonths))
        {
            if (rawMonths.ValueKind != JsonValueKind.Number || !rawMonths.TryGetInt32(out var value)) throw new ArgumentException();
            months = value;
        }
        var filter = new AgreementReportFilter(Search(args), EnumValue<AgreementStatus>(args, "status"), EnumValue<AgreementType>(args, "type"),
            EnumValue<AgreementArchiveSelection>(args, "archive") ?? AgreementArchiveSelection.Include,
            Reference(args, "buildingId"), Reference(args, "organizationElementId"), Reference(args, "geographicAreaId"), Reference(args, "unitId"),
            missing, Date(args, "endDateFrom"), Date(args, "endDateTo"), months);
        var reader = services.GetRequiredService<AgreementReportingReader>();
        if (name == "agreements_aggregate")
        {
            var grouping = EnumValue<AgreementReportGrouping>(args, "groupBy") ?? throw new ArgumentException();
            var (selection, counts) = await reader.CountAsync(account, filter, grouping, ct);
            return new AgreementAggregateResult("1", account, selection, grouping.ToString(), counts.TotalCount, counts.Groups, true, false, true, false, "missing");
        }
        var (selected, data) = await reader.ListAsync(account, filter, Page(args), ct);
        var more = (long)data.Page * data.PageSize < data.TotalCount;
        return new AgreementQueryResult("1", account, selected, data.RequestedPage, data.Page, data.PageSize, data.TotalCount, data.Items.Length,
            more, more && data.Page < 10000 ? data.Page + 1 : null, data.Items.Length == data.TotalCount, false, more && data.Page >= 10000, data.Items);
    }
}
