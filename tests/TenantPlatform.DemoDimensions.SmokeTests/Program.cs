using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenantPlatform.Infrastructure.Initialization;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "demo-reference.json")));
var graph = MagidaDemoData.Create(Verify.User, Verify.Time);
EquivalenceChecks.Run(reference.RootElement, graph);
ReconciliationChecks.Run();
Console.WriteLine($"Demo dimension checks passed ({Verify.Count} assertions; production generation/reconciliation only; no DbContext, initializer, connection, SQL or application startup).");

static class Verify
{
    public static readonly Guid User = Guid.Parse("c0000000-0000-0000-0000-000000000123");
    public static readonly DateTimeOffset Time = new(2026, 11, 12, 15, 30, 0, TimeSpan.Zero);
    public static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    public static int Count { get; private set; }
    public static void That(bool condition, string context)
    { if (!condition) throw new Exception("FAIL: " + context); Count++; }
    public static void Section(string context) => Console.WriteLine("PASS: " + context);
    public static void Throws(Action action, string context)
    {
        try { action(); }
        catch (InvalidOperationException) { That(true, context); return; }
        throw new Exception("FAIL: expected rejection: " + context);
    }
    public static T One<T>(IEnumerable<T> source, Func<T, bool> predicate, string context)
    {
        var matches = source.Where(predicate).ToList();
        That(matches.Count == 1, context + ": expected exactly one row, actual " + matches.Count);
        return matches[0];
    }
    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Json);
    public static void Fields(JsonElement expected, object actual, string context, bool legacyReplaced = false, bool exact = true)
    {
        var properties = Element(actual);
        if (legacyReplaced)
            That(expected.TryGetProperty("OrganizationElementId", out _) && expected.TryGetProperty("GeographicAreaId", out _), context + ".frozen classification fields present");
        if (exact) That(properties.EnumerateObject().Count() == expected.EnumerateObject().Count() - (legacyReplaced ? 2 : 0), context + ".property count");
        foreach (var field in expected.EnumerateObject())
        {
            if (legacyReplaced && field.Name is "OrganizationElementId" or "GeographicAreaId")
            {
                That(!properties.TryGetProperty(field.Name, out _), context + "." + field.Name + " removed; mapping checked through explicit assignments");
                continue;
            }
            That(properties.TryGetProperty(field.Name, out var value), context + "." + field.Name + " exists");
            Equal(field.Value, value, context + "." + field.Name);
        }
    }
    public static void Equal(JsonElement expected, JsonElement actual, string context)
    {
        if (expected.ValueKind == JsonValueKind.String && expected.GetString() == "$resolvedMagidaUserId")
        { That(actual.GetGuid() == User, context + " resolved actor/owner"); return; }
        if (expected.ValueKind == JsonValueKind.String && expected.GetString() == "$recordedUtc")
        { That(actual.GetDateTimeOffset() == Time, context + " recorded instant"); return; }
        if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.String && decimal.TryParse(expected.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        { That(actual.GetDecimal() == number, context + " decimal value"); return; }
        That(expected.ValueKind == actual.ValueKind, $"{context}: expected kind {expected.ValueKind}, actual {actual.ValueKind}");
        if (expected.ValueKind == JsonValueKind.Array)
        {
            That(expected.GetArrayLength() == actual.GetArrayLength(), context + ".length");
            for (var i = 0; i < expected.GetArrayLength(); i++) Equal(expected[i], actual[i], context + $"[{i}]");
        }
        else if (expected.ValueKind == JsonValueKind.Object)
        {
            That(expected.EnumerateObject().Count() == actual.EnumerateObject().Count(), context + ".property count");
            foreach (var field in expected.EnumerateObject())
            {
                That(actual.TryGetProperty(field.Name, out var value), context + "." + field.Name);
                Equal(field.Value, value, context + "." + field.Name);
            }
        }
        else if (expected.ValueKind == JsonValueKind.Number)
            That(expected.GetDecimal() == actual.GetDecimal(), $"{context}: expected {expected}, actual {actual}");
        else That(expected.ToString() == actual.ToString(), $"{context}: expected {expected}, actual {actual}");
    }
}
