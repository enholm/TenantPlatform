using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;

namespace TenantPlatform.Mcp;

public sealed record PriorityEntry(Guid Id, string Title, string Type, DateOnly? EndDate);
public sealed record PriorityResult(string SchemaVersion, Guid AccountId, DateOnly Today, int SelectionLimit,
    int ReturnedCount, bool SelectionComplete, bool IsCompleteAgreementList, bool SnapshotConsistent, PriorityEntry[] Items);
public sealed record InsightEntry(string Key, string? Name, string? Type, string? Status, int Count, bool Direct);
public sealed record InsightNode(Guid Id, int Depth, string Path);
public sealed record InsightsResult(string SchemaVersion, Guid AccountId, string Dimension, Guid? Selected,
    string DataScope, int TotalCount, bool Complete, bool GroupsOverlap, bool SnapshotConsistent, InsightEntry[] Groups, InsightNode[] Options);

public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> OverviewCatalog() => [
        Tool<PriorityResult>("agreements_prioritized", "Read the existing prioritized selection of at most FIVE visible, nonarchived agreements. " +
            "Expired candidates first (latest past end date first), then notice candidates by notice deadline; stable title/ID ties. " +
            "Uses existing Attention rules and account today. This is the complete priority selection, NOT a complete agreement list or total. " +
            "Use agreements_query/aggregate for exhaustive lists/counts. No initialization. Read only.",
            """{"type":"object","additionalProperties":false,"required":["accountId"],"properties":{"accountId":{"type":"string","format":"uuid"}}}"""),
        Tool<InsightsResult>("agreements_insights", "Read existing nonarchived visible agreement insights by Type, stored Status, Counterparty, Geography or Organization hierarchy. " +
            "No list filters. Each agreement has one stored value per dimension; groups form a disjoint partition at this level. " +
            "Without selected: hierarchy roots plus missing. With selected: direct assignments to that node (Direct=true) and child subtrees; total covers only that selected subtree. " +
            "Never sum results from multiple hierarchy levels. Options contain only Application-authorized account nodes. " +
            "Missing/invalid relations are key missing with null name. No person/equity ownership identities. At most100 groups AND100 options; overflow fails wholly, no partial total. Read only, no initialization.",
            """{"type":"object","additionalProperties":false,"required":["accountId","dimension"],"properties":{"accountId":{"type":"string","format":"uuid"},"dimension":{"type":"string","enum":["Type","Status","Counterparty","Geography","Organization"]},"selected":{"type":"string","format":"uuid"}}}""")];
    private static async Task<PriorityResult> PriorityAsync(IServiceProvider services, Guid account, CancellationToken ct)
    {
        var result = await services.GetRequiredService<IAgreementDashboardService>().GetDashboardContractsAsync(account, ct);
        return new("1", account, result.Today, AgreementPriorityRules.Limit, result.Items.Count, true, false, false,
            result.Items.Select(i => new PriorityEntry(i.Id, i.Title, i.Type.ToString(), i.EndDate)).ToArray());
    }
    private static async Task<InsightsResult> InsightsAsync(IServiceProvider services, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var dimension = EnumValue<AgreementInsightDimension>(args, "dimension") ?? throw new ArgumentException();
        // The existing GUI also supports person grouping; this transport deliberately does not.
        if (dimension == AgreementInsightDimension.Owner) throw new ArgumentException();
        var selected = Reference(args, "selected");
        var result = await services.GetRequiredService<IAgreementInsightsService>().GetInsightsAsync(account, dimension, selected, ct);
        if (result.Groups.Count > 100 || result.Options.Count > 100) throw new AgreementReportLimitException();
        return new("1", account, dimension.ToString(), selected, "NonArchivedVisibleAgreements", result.Total, true, false, false,
            result.Groups.Select(g => new InsightEntry(g.Key, g.Name, g.Type?.ToString(), g.Status?.ToString(), g.Count, g.Direct)).ToArray(),
            result.Options.Select(n => new InsightNode(n.Id, n.Depth, n.Path)).ToArray());
    }
}
