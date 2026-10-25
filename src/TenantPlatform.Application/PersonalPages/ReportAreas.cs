using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.ServiceRequests;

namespace TenantPlatform.Application.PersonalPages;

public enum ReportArea { Agreements, ServiceRequests }
// Bounded adapters own domain contracts; the runner only supplies a version-bound, personally authorized MCP call.
public interface IPageReportSource
{
    ReportArea Area { get; }
    string QueryTool { get; }
    string AggregateTool { get; }
    Task<PersonalPageResult> ReadAsync(PageDefinition definition, Guid account, int page,
        Func<string, string, Dictionary<string, object?>, Task<JsonElement>> call, DateTimeOffset retrievedUtc);
}
public static class ReportAreas
{
    public static IReadOnlyList<IPageReportSource> Sources { get; } = [new AgreementPageSource(), new ServiceRequestPageSource()];
    public static PageColumn[] Columns(ReportArea area) => area switch {
        ReportArea.Agreements => [PageColumn.Title, PageColumn.Type, PageColumn.Status, PageColumn.Building, PageColumn.StartDate, PageColumn.EndDate, PageColumn.NoticeDeadline, PageColumn.RenewalDate],
        ReportArea.ServiceRequests => [PageColumn.Title, PageColumn.Status, PageColumn.Building, PageColumn.Unit, PageColumn.CreatedAt, PageColumn.SubmittedAt, PageColumn.CompletedAt],
        _ => throw new PersonalPageException("Invalid") };
    public static IPageReportSource Get(ReportArea area) => Sources.SingleOrDefault(s => s.Area == area) ?? throw new PersonalPageException("Invalid");
}
public sealed record ServiceRequestPageResult(ServiceRequestReportFilter Filter, ServiceRequestAggregateResult? Aggregate, ServiceRequestQueryResult? Table);
internal sealed class ServiceRequestPageSource : IPageReportSource
{
    public ReportArea Area => ReportArea.ServiceRequests;
    public string QueryTool => "service_requests_query";
    public string AggregateTool => "service_requests_aggregate";
    public async Task<PersonalPageResult> ReadAsync(PageDefinition d, Guid account, int page, Func<string, string, Dictionary<string, object?>, Task<JsonElement>> call, DateTimeOffset retrievedUtc)
    {
        var filter = ServiceRequestReportingReader.Normalize(d.RequestFilter!);
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(filter, ReportJson.Options))!;
        args = args.Where(p => p.Value is JsonElement e && e.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Key, p => p.Value);
        args["accountId"] = account;
        ServiceRequestAggregateResult? aggregate = null; ServiceRequestQueryResult? list = null;
        if (PageDefinitions.NeedsAggregate(d))
        {
            args["groupBy"] = d.GroupBy.ToString();
            aggregate = (await call(AggregateTool, d.AggregateVersion, args)).Deserialize<ServiceRequestAggregateResult>(ReportJson.Options) ?? throw new PersonalPageException("Contract");
            if (aggregate.SchemaVersion != "1" || aggregate.AccountId != account || aggregate.Filter != filter || !aggregate.Complete || !aggregate.CountsUniqueRequests || aggregate.GroupsOverlap || aggregate.SnapshotConsistent ||
                aggregate.GroupBy != d.GroupBy.ToString() || aggregate.TotalCount < 0 || aggregate.Groups.Length > 100 || aggregate.Groups.Any(g => g.Count < 0) ||
                aggregate.Groups.Select(g => g.Key).Distinct().Count() != aggregate.Groups.Length || aggregate.Groups.Sum(g => (long)g.Count) != aggregate.TotalCount || aggregate.MissingKey != "missing") throw new PersonalPageException("Contract");
            args.Remove("groupBy");
        }
        if (PageDefinitions.NeedsQuery(d))
        {
            args["page"] = page;
            list = (await call(QueryTool, d.QueryVersion, args)).Deserialize<ServiceRequestQueryResult>(ReportJson.Options) ?? throw new PersonalPageException("Contract");
            if (list.SchemaVersion != "1" || list.AccountId != account || list.Filter != filter || list.SnapshotConsistent || list.PageSize != 25 || list.RequestedPage != page || list.Page is < 1 or > 10000 ||
                list.Items.Length != list.ReturnedCount || list.Items.Length > 25 || list.TotalCount < list.Items.Length || list.HasMore != ((long)list.Page * 25 < list.TotalCount) || list.Complete != (list.Items.Length == list.TotalCount)) throw new PersonalPageException("Contract");
        }
        return new(retrievedUtc, null, null, null, d.GroupBy, d.Columns!, d.Components, Area, new(filter, aggregate, list));
    }
}
internal static class ReportJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) } };
}
