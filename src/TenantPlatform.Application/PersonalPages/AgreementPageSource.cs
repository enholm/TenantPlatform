using System.Text.Json;
using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Application.PersonalPages;
internal sealed class AgreementPageSource : IPageReportSource
{
    public ReportArea Area => ReportArea.Agreements;
    public string QueryTool => "agreements_query";
    public string AggregateTool => "agreements_aggregate";
    public async Task<PersonalPageResult> ReadAsync(PageDefinition definition, Guid account, int page, Func<string, string, Dictionary<string, object?>, Task<JsonElement>> call, DateTimeOffset retrievedUtc)
    {
            var args = Arguments(definition.Filter, account);
            if (definition.Period.Kind == PagePeriodKind.NextMonths) args["endsWithinMonths"] = definition.Period.Months;
            else if (definition.Period.Kind == PagePeriodKind.Fixed) { args["endDateFrom"] = definition.Period.From; args["endDateTo"] = definition.Period.To; }
            AgreementAggregateResult? aggregate = null;
            AgreementQueryResult? list = null;
            AgreementReportSelection? selection = null;
            if (PageDefinitions.NeedsAggregate(definition))
            {
                args["groupBy"] = definition.GroupBy.ToString();
                aggregate = (await call(AggregateTool, definition.AggregateVersion, args)).Deserialize<AgreementAggregateResult>(ReportJson.Options) ?? throw new PersonalPageException("Contract");
                if (aggregate.SchemaVersion != "1" || aggregate.AccountId != account || !aggregate.Complete || !aggregate.CountsUniqueAgreements || aggregate.GroupsOverlap || aggregate.SnapshotConsistent || aggregate.GroupBy != definition.GroupBy.ToString() ||
                    aggregate.TotalCount < 0 || aggregate.Groups.Length > 100 || aggregate.Groups.Any(g => g.Count < 0) || aggregate.Groups.Select(g => g.Key).Distinct().Count() != aggregate.Groups.Length || aggregate.Groups.Sum(g => (long)g.Count) != aggregate.TotalCount || aggregate.MissingKey != "missing") throw new PersonalPageException("Contract");
                selection = aggregate.Selection;
                ValidateSelection(selection);
                // A second call uses the target's concrete dates, even across midnight.
                args = Arguments(selection.Filter, account);
            }
            if (PageDefinitions.NeedsQuery(definition))
            {
                args["page"] = page;
                list = (await call(QueryTool, definition.QueryVersion, args)).Deserialize<AgreementQueryResult>(ReportJson.Options) ?? throw new PersonalPageException("Contract");
                if (list.SchemaVersion != "1" || list.AccountId != account || aggregate is not null && list.Selection.Filter != aggregate.Selection.Filter || list.SnapshotConsistent || list.PageSize != 25 || list.RequestedPage != page || list.Page is < 1 or > 10000 ||
                    list.Items.Length != list.ReturnedCount || list.Items.Length > 25 || list.TotalCount < list.Items.Length || list.HasMore != ((long)list.Page * 25 < list.TotalCount) || list.Complete != (list.Items.Length == list.TotalCount)) throw new PersonalPageException("Contract");
                if (aggregate is null) { selection = list.Selection; ValidateSelection(selection); }
            }
            void ValidateSelection(AgreementReportSelection actual)
            {
                var expected = definition.Period.Kind == PagePeriodKind.All ? definition.Filter :
                    definition.Period.Kind == PagePeriodKind.Fixed ? definition.Filter with { EndDateFrom = definition.Period.From, EndDateTo = definition.Period.To } :
                    definition.Filter with { EndDateFrom = actual.Today, EndDateTo = actual.Today.AddMonths(definition.Period.Months!.Value) };
                expected = expected with { Search = expected.Search?.Trim() };
                if (actual.Filter != expected) throw new PersonalPageException("Contract");
            }
        return new(retrievedUtc, selection ?? throw new PersonalPageException("Contract"), aggregate, list, definition.GroupBy, definition.Columns!, definition.Components);
    }
    private static Dictionary<string, object?> Arguments(AgreementReportFilter filter, Guid account)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(filter, ReportJson.Options))!;
        result = result.Where(p => p.Value is JsonElement element && element.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Key, p => p.Value);
        result["accountId"] = account; return result;
    }
}
