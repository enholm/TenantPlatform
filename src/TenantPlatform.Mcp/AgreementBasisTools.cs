using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Mcp;

public sealed record StoredBasesResult(string SchemaVersion, Guid AccountId, string Coverage, bool SnapshotConsistent, FinancePage<StoredBasisSummary> Data);
public sealed record StoredBasisResult(string SchemaVersion, Guid AccountId, string Coverage, bool SnapshotConsistent, StoredBasisDetail Data);
public sealed partial class ReadTools
{
    private static IReadOnlyList<Tool> BasisCatalog() => [
        Tool<StoredBasesResult>("agreements_bases", "Find stored calculation bases in authorized agreements. Required direction Income/Cost filters stored basis direction, optional agreementId/status Draft/Approved/Cancelled and inclusive from/to INVOICE dates. Fixed pages25, real total before paging, no hidden500 cap; GeneratedUtc descending/Id ascending. Snapshot amount/currency/title/counterparty and period bounds come from each current stored revision, not today's forecast. Amount is sum of stored event deltas, including corrections; never sum currencies or revisions indiscriminately. Freshness NotEvaluated: no Stale/NeedsCorrection assessment. Status is current stored header approval status, not permission or evidence of an invoice/payment. No generation, people, documents or report adapter. Read only.",
            """{"type":"object","additionalProperties":false,"required":["accountId","direction"],"properties":{"accountId":{"type":"string","format":"uuid"},"direction":{"type":"string","enum":["Income","Cost"]},"agreementId":{"type":"string","format":"uuid"},"status":{"type":"string","enum":["Draft","Approved","Cancelled"]},"from":{"type":"string","minLength":10,"maxLength":10},"to":{"type":"string","minLength":10,"maxLength":10},"page":{"type":"integer","minimum":1,"maximum":10000}}}"""),
        Tool<StoredBasisResult>("agreements_basis", "Read one stored calculation basis snapshot. Optional positive revision selects an exact historical snapshot; default current revision. Header Status/CurrentRevision are current, NOT historical status at selected SnapshotRevision. Stored events expose Amount (delta), PreviousAmount and TargetAmount separately and their stored target segments; corrections need not equal segment sum. No recalculation, Stale or NeedsCorrection: Freshness is NotEvaluated, even for Approved/Cancelled. No generation, approval, correction, invoice or payment. No raw JSON/fingerprints/actors/reasons/documents/addresses. Whole selected snapshot only: max65536 characters, depth32,100 events/250 segments/100 previous references; invalid/unknown format gives SNAPSHOT_UNAVAILABLE, oversized gives whole RESULT_TOO_LARGE, never fabricated zero. Read only.",
            """{"type":"object","additionalProperties":false,"required":["accountId","basisId"],"properties":{"accountId":{"type":"string","format":"uuid"},"basisId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":1,"maximum":2147483647}}}""")];
    private static async Task<object> BasisAsync(IServiceProvider services, string name, Guid account, IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var reader = services.GetRequiredService<AgreementBasisReader>();
        if (name == "agreements_bases") return new StoredBasesResult("1", account, "StoredCalculationBases", false,
            await reader.ListAsync(account, new(EnumValue<AgreementDirection>(args, "direction") ?? throw new ArgumentException(), Reference(args, "agreementId"), EnumValue<AgreementBasisStatus>(args, "status"), Date(args, "from"), Date(args, "to")), Page(args), ct));
        int? revision = null;
        if (args.TryGetValue("revision", out var value)) { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n < 1) throw new ArgumentException(); revision = n; }
        return new StoredBasisResult("1", account, "StoredCalculationBasisSnapshot", false,
            await reader.DetailAsync(account, Reference(args, "basisId") ?? throw new ArgumentException(), revision, ct));
    }
}
