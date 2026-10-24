using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Mcp;
namespace TenantPlatform.Application.PersonalPages;

public sealed record PersonalPageResult(DateTimeOffset RetrievedUtc, AgreementReportSelection Selection, AgreementAggregateResult? Aggregate, AgreementQueryResult? Table,
    AgreementReportGrouping GroupBy, PageColumn[] Columns, PageComponent[] Components);
public sealed class PersonalPageRunner(PersonalPageService pages, McpRegistryService? registry, PersonalPageRuns runs, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public async Task<PersonalPageResult> RunAsync(PageDefinition definition, int page = 1, Guid? savedId = null, CancellationToken ct = default,
        Func<string, string, string, CancellationToken, Task<string>>? read = null)
    {
        var who = await pages.RequireAsync(ct);
        using var lease = runs.Start(who, ct, out var token);
        try
        {
            if (page is < 1 or > 10000) throw new PersonalPageException("Invalid");
            definition = PageDefinitions.Current(definition);
            await pages.ValidateSourceAsync(definition, token);
            if (savedId.HasValue) await CheckSaved();
            var args = Arguments(definition.Filter, who.AccountId!.Value);
            if (definition.Period.Kind == PagePeriodKind.NextMonths) args["endsWithinMonths"] = definition.Period.Months;
            else if (definition.Period.Kind == PagePeriodKind.Fixed) { args["endDateFrom"] = definition.Period.From; args["endDateTo"] = definition.Period.To; }
            AgreementAggregateResult? aggregate = null;
            AgreementQueryResult? list = null;
            AgreementReportSelection? selection = null;
            if (PageDefinitions.NeedsAggregate(definition))
            {
                args["groupBy"] = definition.GroupBy.ToString();
                aggregate = await Call<AgreementAggregateResult>("agreements_aggregate", definition.AggregateVersion, args);
                if (aggregate.SchemaVersion != "1" || aggregate.AccountId != who.AccountId || !aggregate.Complete || !aggregate.CountsUniqueAgreements || aggregate.GroupsOverlap || aggregate.SnapshotConsistent || aggregate.GroupBy != definition.GroupBy.ToString() ||
                    aggregate.TotalCount < 0 || aggregate.Groups.Length > 100 || aggregate.Groups.Any(g => g.Count < 0) || aggregate.Groups.Select(g => g.Key).Distinct().Count() != aggregate.Groups.Length || aggregate.Groups.Sum(g => (long)g.Count) != aggregate.TotalCount || aggregate.MissingKey != "missing") throw new PersonalPageException("Contract");
                selection = aggregate.Selection;
                ValidateSelection(selection);
                // A second call uses the target's concrete dates, even across midnight.
                args = Arguments(selection.Filter, who.AccountId.Value);
            }
            if (PageDefinitions.NeedsQuery(definition))
            {
                args["page"] = page;
                list = await Call<AgreementQueryResult>("agreements_query", definition.QueryVersion, args);
                if (list.SchemaVersion != "1" || list.AccountId != who.AccountId || aggregate is not null && list.Selection.Filter != aggregate.Selection.Filter || list.SnapshotConsistent || list.PageSize != 25 || list.RequestedPage != page || list.Page is < 1 or > 10000 ||
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
            await pages.ValidateSourceAsync(definition, token);
            if (await pages.RequireAsync(token) != who) throw new UnauthorizedAccessException();
            if (savedId.HasValue) await CheckSaved();
            token.ThrowIfCancellationRequested();
            return new(clock.GetUtcNow(), selection ?? throw new PersonalPageException("Contract"), aggregate, list, definition.GroupBy, definition.Columns!, definition.Components);
        }
        catch (OperationCanceledException) { throw new PersonalPageException("Cancelled"); }
        catch (KeyNotFoundException) { throw new PersonalPageException("Contract"); }
        catch (JsonException) { throw new PersonalPageException("Contract"); }
        catch (McpClientException ex) { throw new PersonalPageException(ex.Code is "RESULT_TOO_LARGE" ? "Limit" : ex.Code is "INVALID_ARGUMENT" ? "Contract" : ex.Code is "CANCELLED" ? "Cancelled" : "SourceUnavailable"); }

        async Task CheckSaved()
        {
            var saved = PageDefinitions.Current((await pages.ReadAsync(savedId!.Value, token)).Definition);
            if (JsonSerializer.Serialize(saved, Json) != JsonSerializer.Serialize(definition, Json)) throw new PersonalPageException("Conflict");
        }
        async Task<T> Call<T>(string name, string version, Dictionary<string, object?> args)
        {
            await pages.RequireAsync(token);
            if (registry is null) throw new PersonalPageException("SourceUnavailable");
            var arguments = JsonSerializer.Serialize(args, Json);
            var raw = read is null ? await registry.CallAsync(definition.ConnectionId, name, version, arguments, token) : await read(name, version, arguments, token);
            if (Encoding.UTF8.GetByteCount(raw) > 65536) throw new PersonalPageException("Limit");
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("structuredContent").Deserialize<T>(Json) ?? throw new PersonalPageException("Contract");
        }
    }
    private static Dictionary<string, object?> Arguments(AgreementReportFilter filter, Guid account)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(filter, Json))!;
        result = result.Where(p => p.Value is JsonElement element && element.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Key, p => p.Value);
        result["accountId"] = account; return result;
    }
}
