using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Buildings;

namespace TenantPlatform.Mcp;

public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> BuildingCatalog() => [
        Tool<BuildingQueryResult>("buildings_query", "Read the authorized account building LIST, including the existing broad PropertyAdmin list exception. Does not authorize details. Only ID/name/address/postal code/city. Case-insensitive name substring search, trimmed, max200 characters, no control characters. SQL pages25 ordered Name/Id, real total, no cross-query/cross-call snapshot guarantee. No units, occupancy, detail counts or report creation. Read only.", BuildingSchema("\"page\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"default\":1}")),
        Tool<BuildingAggregateResult>("buildings_aggregate", "Count unique buildings with exactly the same account/list access and name filter as buildings_query. Optional groupBy City: exact stored value with C collation; case and whitespace preserved, only NULL/empty string share key missing. Other keys are city: plus stored value. No geographical normalization. Max100 groups, overflow rejects entire result. Without groupBy returns total with no groups. Complete counts, not a cross-call snapshot. No report adapter. Read only.", BuildingSchema("\"groupBy\":{\"type\":\"string\",\"enum\":[\"City\"]}"))];
    private static string BuildingSchema(string extra) => "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"accountId\"],\"properties\":{\"accountId\":{\"type\":\"string\",\"format\":\"uuid\"},\"search\":{\"type\":\"string\",\"maxLength\":200}," + extra + "}}";
    private static async Task<object> BuildingAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var reader = services.GetRequiredService<BuildingReportingReader>();
        var filter = new BuildingReportFilter(Search(args));
        if (name == "buildings_query") return await reader.ListAsync(account, filter, Page(args), ct);
        var city = args.TryGetValue("groupBy", out var raw);
        if (city && (raw.ValueKind != JsonValueKind.String || raw.GetString() != "City")) throw new ArgumentException();
        return await reader.CountAsync(account, filter, city, ct);
    }
}
