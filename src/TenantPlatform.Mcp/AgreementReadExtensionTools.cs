using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;

namespace TenantPlatform.Mcp;

public sealed record AgreementBusinessResult(string SchemaVersion, Guid AccountId, bool Complete, bool SnapshotConsistent,
    AgreementReportRow Agreement, AgreementBusinessFields Business);
public sealed record AgreementHistoryResult(string SchemaVersion, Guid AccountId, Guid AgreementId, string Coverage,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, FollowupEvent[] Items);
public sealed record AgreementRelationsResult(string SchemaVersion, Guid AccountId, string Kind, string Coverage,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, AgreementRelation[] Items);

public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> ExtensionCatalog() => [
        Tool<AgreementHistoryResult>("agreements_followup_history", "Read stored follow-up events for one authorized agreement. Coverage is deadline follow-up ONLY, not a complete agreement audit trail. Time and event kind only, no people/comments/payloads. No before/after status or dates are stored in these events; do not invent them or treat today's deadline state as historical state. Ordered CreatedUtc, ID; fixed pages of 25, true total, no cross-call snapshot. Read only, no initialization.",
            """{"type":"object","additionalProperties":false,"required":["accountId","agreementId"],"properties":{"accountId":{"type":"string","format":"uuid"},"agreementId":{"type":"string","format":"uuid"},"page":{"type":"integer","minimum":1,"maximum":10000}}}"""),
        Tool<AgreementRelationsResult>("agreements_relations", "Find exact relation IDs used by your currently visible agreements, including archived agreements and inactive linked relations. Not the administrator/write-editor catalog, not permission to open registers. Select one relation kind; literal case-insensitive name search, fixed pages of 25 and full distinct total. Same names may have different IDs: ask the user to disambiguate, never guess. Exact linked nodes only, no implicit hierarchy descendants/ancestors. Read only.",
            """{"type":"object","additionalProperties":false,"required":["accountId","kind"],"properties":{"accountId":{"type":"string","format":"uuid"},"kind":{"type":"string","enum":["Counterparty","Building","Unit","OrganizationElement","GeographicArea"]},"search":{"type":"string","maxLength":200},"page":{"type":"integer","minimum":1,"maximum":10000}}}""")];

    private static async Task<object> ExtensionAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var reader = services.GetRequiredService<AgreementReadExtensions>();
        var requested = Page(args);
        if (name == "agreements_followup_history")
        {
            var id = Reference(args, "agreementId") ?? throw new ArgumentException();
            var data = await reader.HistoryAsync(account, id, requested, ct);
            var more = (long)data.Page * 25 < data.Total;
            return new AgreementHistoryResult("1", account, id, "StoredDeadlineFollowupEventsOnly", requested, data.Page, 25,
                data.Total, data.Items.Length, more, more && data.Page < 10000 ? data.Page + 1 : null,
                data.Total == data.Items.Length, false, more && data.Page >= 10000, data.Items);
        }
        var kind = EnumValue<AgreementRelationKind>(args, "kind") ?? throw new ArgumentException();
        var result = await reader.RelationsAsync(account, kind, Search(args), requested, ct);
        var hasMore = (long)result.Page * 25 < result.Total;
        return new AgreementRelationsResult("1", account, kind.ToString(), "RelationsUsedByVisibleAgreementsOnly", requested, result.Page, 25,
            result.Total, result.Items.Length, hasMore, hasMore && result.Page < 10000 ? result.Page + 1 : null,
            result.Total == result.Items.Length, false, hasMore && result.Page >= 10000, result.Items);
    }
}
