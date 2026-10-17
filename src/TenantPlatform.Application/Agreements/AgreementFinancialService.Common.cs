using System.Security.Cryptography;
using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed partial class AgreementFinancialService
{
    private sealed class Session(IAgreementFinancialTransaction transaction, AgreementReadScope scope) : IAsyncDisposable
    {
        public AgreementFinancialState State => transaction.State;
        public AgreementReadScope Scope => scope;
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
    private async Task<Session> Open(Guid account, Guid agreement, bool write, bool manage, CancellationToken ct)
    {
        var initial = await Require(account, agreement, write, ct);
        if (manage && !initial.CanManageAccess) throw new UnauthorizedAccessException();
        var tx = await data.BeginAsync(account, agreement, write, ct);
        try
        {
            var scope = await Require(account, agreement, write, ct);
            if (scope.ListScope.UserId != initial.ListScope.UserId || manage && !scope.CanManageAccess) throw new UnauthorizedAccessException();
            return new(tx, scope);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    private async Task<Session> OpenBasis(Guid account, Guid basis, bool write, bool manage, CancellationToken ct)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        var agreement = await data.ReadBasisAgreementAsync(account, basis, ct) ?? throw new UnauthorizedAccessException();
        var session = await Open(account, agreement, write, manage, ct);
        if (session.Scope.ListScope.UserId != actor.UserId) { await session.DisposeAsync(); throw new UnauthorizedAccessException(); }
        return session;
    }
    private sealed record FinancialSource(Agreement Agreement, List<AgreementLineVersion> Versions, List<AgreementPriceVersion> Prices, Dictionary<Guid, List<AgreementAutomaticPrice>> AutomaticPrices);
    private static FinancialSource Source(AgreementFinancialState state)
    {
        var a = state.Agreement;
        var versions = state.Versions.Where(x => x.Status != AgreementLineStatus.Draft).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        foreach (var v in versions) v.Documents = v.Documents.OrderBy(x => x.DocumentId).ToList();
        var sequences = versions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        var prices = state.Prices.Where(x => x.Independent || sequences.Contains((x.LineId, x.Sequence))).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        return new(a, versions, prices, AgreementAutomaticIndexCalculator.Project(a.IndexSetupNeedsReview, versions, prices, state.Selections.OrderBy(x => x.Sequence).ToList(), state.Indices, state.IndexValues.Where(x => !x.Superseded).ToList()));
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static T ReadJson<T>(string value) => JsonSerializer.Deserialize<T>(value)!;
    private static string Fingerprint<T>(T value)
    {
        // PostgreSQL numeric preserves column scale (3 becomes 3.000000). Hash mathematical
        // values, not formatting, so persisted basis snapshots keep stable fingerprints.
        using var document = JsonDocument.Parse(Json(value));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var p in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(p.Value, writer); }
                    writer.WriteEndObject(); break;
                case JsonValueKind.Array:
                    writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); break;
                case JsonValueKind.Number when element.TryGetDecimal(out var number):
                    writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: element.WriteTo(writer); break;
            }
        }
    }
    private static object HeaderFingerprint(Agreement a) => new { a.Id, a.AccountId, a.Title, a.CounterpartyOrganizationId, a.Currency, a.Direction, a.StartDate, a.EndDate, a.Terms, a.Status, a.IsArchived };
}
