using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Mcp;

public sealed record AccountDeadlineEntry(Guid Id, Guid AgreementId, string AgreementTitle, string Counterparty,
    string AgreementType, string Kind, DateOnly DueDate, int DaysLeft, string Status, string State, DateTimeOffset UpdatedUtc);
public sealed record DeadlineCategories(int Overdue, int Next30, int MissingResponsible, bool BeforeListFilters, bool GroupsOverlap);
public sealed record AccountDeadlineResult(string SchemaVersion, Guid AccountId, int RequestedPage, int Page, int PageSize,
    int TotalCount, int ReturnedCount, bool HasMore, int? NextPage, bool Complete, bool SnapshotConsistent,
    bool LimitReached, string DataCoverage, DeadlineCategories Categories, AccountDeadlineEntry[] Items);

public sealed partial class ReadTools
{
    private static string[] DeadlineKeys => ["accountId", "mine", "type", "kind", "status", "state", "includeCompleted", "horizonDays", "customDates", "from", "to", "search", "page"];
    private static Tool DeadlineTool() => Tool<AccountDeadlineResult>("agreements_deadlines",
        "Read stored deadlines across visible agreements without initialization. Empty may mean uninitialized; never infer no planned deadlines. " +
        "Defaults: Current state, exclude Completed, horizon 90 days with NO lower bound (includes overdue). " +
        "mine uses only your authenticated identity and includes explicit assignee or inherited agreement owner. " +
        "Explicit status overrides includeCompleted. state All includes all stored states. " +
        "customDates uses inclusive optional from/to yyyy-MM-dd; neither date means all dates. " +
        "Categories are BEFORE ALL list filters: Current, not Completed, nonarchived; Overdue before account today, " +
        "Next30 today through today+30 inclusive, MissingResponsible uses existing assignment eligibility. " +
        "Categories can overlap; never sum as unique deadlines. List may include archived agreements. " +
        "Title/counterparty substring search, DueDate/ID order, fixed pages25. No person IDs, names or owner catalog. Read only.",
        """
        {"type":"object","additionalProperties":false,"required":["accountId"],"properties":{
          "accountId":{"type":"string","format":"uuid"},"mine":{"type":"boolean"},
          "type":{"type":"string","enum":["Lease","SoftwareLicense","ServiceMaintenance","Supplier","Other"]},
          "kind":{"type":"string","enum":["Notice","Expiry","Renewal","Cessation"]},
          "status":{"type":"string","enum":["Untreated","InReview","Completed"]},
          "state":{"type":"string","enum":["Current","Replaced","Withdrawn","All"]},"includeCompleted":{"type":"boolean"},
          "horizonDays":{"type":"integer","enum":[30,60,90]},"customDates":{"type":"boolean"},
          "from":{"type":"string","minLength":10,"maxLength":10},"to":{"type":"string","minLength":10,"maxLength":10},
          "search":{"type":"string","maxLength":200},"page":{"type":"integer","minimum":1,"maximum":10000}}}
        """);
    private static bool Flag(IDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var raw)) return false;
        if (raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException();
        return raw.GetBoolean();
    }
    private static async Task<AccountDeadlineResult> DeadlinesAsync(IServiceProvider services, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var custom = Flag(args, "customDates");
        var from = Date(args, "from"); var to = Date(args, "to");
        if (!custom && (from.HasValue || to.HasValue) || custom && args.ContainsKey("horizonDays") || from > to) throw new ArgumentException();
        var horizon = 90;
        if (args.TryGetValue("horizonDays", out var raw) && (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt32(out horizon) || horizon is not (30 or 60 or 90))) throw new ArgumentException();
        AgreementDeadlineState? state = AgreementDeadlineState.Current;
        if (args.TryGetValue("state", out var rawState))
            state = rawState.ValueKind == JsonValueKind.String && rawState.GetString() == "All" ? null : EnumValue<AgreementDeadlineState>(args, "state");
        var requested = Page(args);
        var filter = new AgreementDeadlineFilter { Mine = Flag(args, "mine"), AgreementType = EnumValue<AgreementType>(args, "type"),
            Kind = EnumValue<AgreementDeadlineKind>(args, "kind"), Status = EnumValue<AgreementFollowupStatus>(args, "status"), State = state,
            IncludeCompleted = Flag(args, "includeCompleted"), CustomDates = custom, From = from, To = to, HorizonDays = horizon, Search = Search(args), Page = requested };
        var result = await services.GetRequiredService<IAgreementDeadlineReader>().ReadAsync(account, filter, ct);
        var more = (long)result.Page * result.PageSize < result.Total;
        return new("1", account, requested, result.Page, result.PageSize, result.Total, result.Items.Count,
            more, more && result.Page < 10000 ? result.Page + 1 : null, result.Items.Count == result.Total, false,
            more && result.Page >= 10000, "StoredOccurrencesOnly", new(result.Overdue, result.Next30, result.MissingOwner, true, true),
            result.Items.Select(d => new AccountDeadlineEntry(d.Id, d.AgreementId, d.AgreementTitle, d.Counterparty,
                d.AgreementType.ToString(), d.Kind.ToString(), d.DueDate, d.DaysLeft, d.Status.ToString(), d.State.ToString(), d.UpdatedUtc)).ToArray());
    }
}
