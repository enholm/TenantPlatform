using System.Globalization;
using System.Text.Json;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

internal sealed record BasisParsedSnapshot(StoredBasisDetail Detail, Guid[] PreviousSnapshots);
internal static class BasisSnapshotProjection
{
    // Existing writer serializes this unversioned PascalCase shape. It is an explicitly
    // supported legacy format, not a license to deserialize future schemas or entities.
    public static BasisParsedSnapshot Parse(Guid account, BasisStorageRow row, CancellationToken ct)
    {
        if (row.SnapshotRevision is null) throw new AgreementBasisSnapshotException();
        if (row.DataJson is null) throw new AgreementFinanceLimitException();
        if (row.DataJson.Length > AgreementBasisReader.MaxSnapshotCharacters) throw new AgreementFinanceLimitException();
        try
        {
            if (!Enum.IsDefined(row.Status) || !Enum.IsDefined(row.Direction) || row.CurrentRevision < 1 || row.SnapshotRevision is not > 0 || row.SnapshotRevision > row.CurrentRevision) throw new JsonException();
            using var document = JsonDocument.Parse(row.DataJson, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
            var root = document.RootElement;
            Object(root, "AccountId", "AccountName", "AgreementId", "AgreementTitle", "CounterpartyId", "CounterpartyName", "Direction", "Currency", "TaxTreatment", "Events", "PreviousSnapshotIds");
            if (Id(root, "AccountId") != account || Id(root, "AgreementId") != row.AgreementId || root.GetProperty("Direction").GetInt32() != (int)row.Direction) throw new JsonException();
            var title = Text(root, "AgreementTitle"); var party = Id(root, "CounterpartyId"); var partyName = Text(root, "CounterpartyName");
            var currency = Text(root, "Currency"); var tax = Text(root, "TaxTreatment");
            if (!AgreementPeriodCalculator.Currencies.Contains(currency) || tax != "ProcessingTaxExclusive") throw new JsonException();
            var input = Array(root, "Events", AgreementBasisReader.MaxEvents); var events = new List<StoredBasisEvent>(); var segmentCount = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in input.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested(); Object(e, "EventKey", "LineId", "Segments", "Amount", "PreviousAmount", "TargetAmount", "Adjustments");
                var key = Text(e, "EventKey"); if (!keys.Add(key)) throw new JsonException();
                var line = Id(e, "LineId"); var segments = new List<StoredBasisSegment>();
                foreach (var s in Array(e, "Segments", AgreementBasisReader.MaxSegments).EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested(); if (++segmentCount > AgreementBasisReader.MaxSegments) throw new AgreementFinanceLimitException();
                    Object(s, "AgreementId", "LineId", "LineName", "Direction", "Currency", "ReferenceFrom", "ReferenceTo", "From", "To", "InvoiceDate", "Quantity", "UnitPrice", "ActiveDays", "ReferenceDays", "Amount", "Included", "LineVersionId", "PriceVersionId", "EventKey", "DocumentIds", "IndexAdjustments", "Fraction", "ExplanationKey");
                    if (Id(s, "AgreementId") != row.AgreementId || Id(s, "LineId") != line || s.GetProperty("Direction").GetInt32() != (int)row.Direction || Text(s, "Currency") != currency || Text(s, "EventKey") != key) throw new JsonException();
                    var rf = Date(s, "ReferenceFrom"); var rt = Date(s, "ReferenceTo"); var from = Date(s, "From"); var to = Date(s, "To");
                    var active = s.GetProperty("ActiveDays").GetInt32(); var days = s.GetProperty("ReferenceDays").GetInt32();
                    if (rf > rt || from > to || active < 0 || days <= 0) throw new JsonException();
                    segments.Add(new(line, Text(s, "LineName"), Id(s, "LineVersionId"), Id(s, "PriceVersionId"), rf, rt, from, to, Date(s, "InvoiceDate"),
                        Money(s, "Quantity"), Money(s, "UnitPrice"), active, days, Money(s, "Amount"), s.GetProperty("Included").GetBoolean()));
                }
                // Correction Amount is a stored delta; it need not equal the target segments' sum.
                events.Add(new(line, Money(e, "Amount"), Money(e, "PreviousAmount"), Money(e, "TargetAmount"), segments.ToArray()));
            }
            var previous = Array(root, "PreviousSnapshotIds", AgreementBasisReader.MaxPreviousSnapshots).EnumerateArray().Select(v => v.GetGuid()).ToArray();
            if (previous.Contains(Guid.Empty)) throw new JsonException();
            var all = events.SelectMany(e => e.Segments).ToArray();
            var summary = new StoredBasisSummary(row.Id, row.AgreementId, row.OriginalBasisId, row.Direction, row.InvoiceDate, row.Status, row.CurrentRevision, row.SnapshotRevision.Value,
                row.GeneratedUtc, title, party, partyName, currency, tax, events.Sum(e => e.Amount), all.Length == 0 ? null : all.Min(s => s.From), all.Length == 0 ? null : all.Max(s => s.To), events.Count, all.Length, BasisFreshness.NotEvaluated);
            return new(new(summary, events.ToArray()), previous);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or ArgumentException)
        { throw new AgreementBasisSnapshotException(); }
    }
    private static void Object(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal))) throw new JsonException();
    }
    private static JsonElement Array(JsonElement parent, string key, int max)
    {
        var v = parent.GetProperty(key); if (v.ValueKind != JsonValueKind.Array) throw new JsonException();
        if (v.GetArrayLength() > max) throw new AgreementFinanceLimitException(); return v;
    }
    private static string Text(JsonElement value, string key)
    {
        var text = value.GetProperty(key).GetString(); if (text is null || text.Length > 4000) throw new JsonException(); return text;
    }
    private static Guid Id(JsonElement value, string key) { var id = value.GetProperty(key).GetGuid(); if (id == Guid.Empty) throw new JsonException(); return id; }
    private static decimal Money(JsonElement value, string key) => value.GetProperty(key).GetDecimal();
    private static DateOnly Date(JsonElement value, string key) => DateOnly.ParseExact(Text(value, key), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
