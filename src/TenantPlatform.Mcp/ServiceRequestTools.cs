using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Mcp;

public sealed partial class ReadTools
{
    private const string RequestProperties = """
        "accountId":{"type":"string","format":"uuid"},"search":{"type":"string","maxLength":200},
        "status":{"type":"string","enum":["Draft","Submitted","Approved","InProgress","Completed","Rejected","Cancelled","Failed"]},
        "buildingId":{"type":"string","format":"uuid"},"missingBuilding":{"type":"boolean"}
        """;
    private static IReadOnlyList<Tool> ServiceRequestCatalog() => [
        Tool<ServiceRequestQueryResult>("service_requests_query", "Read visible service requests: requester, tenant, assigned provider or scoped administrator. Title-only case-insensitive substring search; null titles retained without search. Stored status, no calculated open/closed rules. Exact account-bound building ID or missingBuilding, all filters AND. Fixed pages of 25 ordered CreatedAt descending then ID; full filtered total, no cross-call snapshot. No comments, email, identities or workflow writes. Read only.", RequestSchema("\"page\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000}")),
        Tool<ServiceRequestAggregateResult>("service_requests_aggregate", "Count unique visible service requests grouped in SQL by stored Status or account-bound Building. Same filters/access as service_requests_query, applied before counting. Nonoverlapping complete partition; missing/foreign building has key missing and null name. At most 100 groups; overflow is RESULT_TOO_LARGE, never partial totals. No historical series or calculated status. Read only.", RequestSchema("\"groupBy\":{\"type\":\"string\",\"enum\":[\"Building\",\"Status\"]}", true))];
    private static string RequestSchema(string extra, bool aggregate = false) =>
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"accountId\"" + (aggregate ? ",\"groupBy\"" : "") + "],\"properties\":{" + RequestProperties + "," + extra + "}}";
    private static string[] RequestKeys(string name) => ["accountId", "search", "status", "buildingId", "missingBuilding", name == "service_requests_query" ? "page" : "groupBy"];
    private static async Task<object> RequestReportAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var missing = false;
        if (args.TryGetValue("missingBuilding", out var raw))
        {
            if (raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException();
            missing = raw.GetBoolean();
        }
        var filter = new ServiceRequestReportFilter(Search(args), EnumValue<ServiceRequestStatus>(args, "status"), Reference(args, "buildingId"), missing);
        var reader = services.GetRequiredService<ServiceRequestReportingReader>();
        return name == "service_requests_query" ? await reader.ListAsync(account, filter, Page(args), ct) :
            await reader.CountAsync(account, filter, EnumValue<ServiceRequestReportGrouping>(args, "groupBy") ?? throw new ArgumentException(), ct);
    }
}
