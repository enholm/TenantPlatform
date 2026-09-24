using System.Text.Json;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public static class ContractAnalysisSchema
{
    public const string Version = "contract-v1";
    public static JsonElement Schema { get; } = Load();
    private static JsonElement Load()
    {
        using var stream = typeof(ContractAnalysisSchema).Assembly.GetManifestResourceStream(
            "TenantPlatform.Web.Services.Agreements.contract-analysis.schema.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.Clone();
    }
    public static ContractAnalysisResult Parse(string json, IReadOnlyCollection<Guid> files)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            Validate(document.RootElement, Schema);
            var result = document.RootElement.Deserialize<ContractAnalysisResult>(ContractAnalysisJson.Options)!;
            if (result.Documents.Count != files.Count || result.Documents.Select(x => x.DocumentId).Distinct().Count() != files.Count ||
                result.Documents.Any(x => !files.Contains(x.DocumentId)) ||
                Enum.GetValues<AgreementFindingCategory>().Any(c => !result.Findings.Any(f => f.Category == c))) Invalid();
            foreach (var finding in result.Findings)
            {
                if (finding.Status == AgreementFindingStatus.Found && (string.IsNullOrWhiteSpace(finding.Value) || finding.Sources.Count == 0)) Invalid();
                if (finding.Status == AgreementFindingStatus.Unclear && string.IsNullOrWhiteSpace(finding.Explanation)) Invalid();
                if (finding.Sources.Any(s => !files.Contains(s.DocumentId) || string.IsNullOrWhiteSpace(s.Quote))) Invalid();
            }
            if (result.Documents.Any(x => !x.Processed && string.IsNullOrWhiteSpace(x.Explanation))) Invalid();
            if (!result.Counterparty.RequiresClarification && string.IsNullOrWhiteSpace(result.Counterparty.Name)) Invalid();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or OverflowException)
        { throw new AgreementValidationException("AnalysisInvalidResponse"); }
    }
    private static void Invalid() => throw new AgreementValidationException("AnalysisInvalidResponse");

    // Validates the complete deliberately small schema vocabulary used by our strict API schema.
    private static void Validate(JsonElement value, JsonElement schema)
    {
        var type = schema.GetProperty("type");
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(x => x.GetString()).ToArray() : [type.GetString()];
        var kind = value.ValueKind switch
        {
            JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.String => "string",
            JsonValueKind.Number when value.TryGetInt64(out _) => "integer",
            JsonValueKind.True or JsonValueKind.False => "boolean", JsonValueKind.Null => "null", _ => "invalid"
        };
        if (!types.Contains(kind)) Invalid();
        if (kind == "object")
        {
            var props = schema.GetProperty("properties");
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!value.TryGetProperty(required.GetString()!, out _)) Invalid();
            var seen = new HashSet<string>();
            foreach (var prop in value.EnumerateObject())
            {
                if (!seen.Add(prop.Name) || !props.TryGetProperty(prop.Name, out _)) Invalid();
                Validate(prop.Value, props.GetProperty(prop.Name));
            }
        }
        if (kind == "array")
        {
            if (value.GetArrayLength() > schema.GetProperty("maxItems").GetInt32()) Invalid();
            foreach (var item in value.EnumerateArray()) Validate(item, schema.GetProperty("items"));
        }
        if (kind == "string")
        {
            if (schema.TryGetProperty("maxLength", out var max) && value.GetString()!.Length > max.GetInt32()) Invalid();
            if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(x => x.GetString() == value.GetString())) Invalid();
        }
        if (kind == "integer" && schema.TryGetProperty("minimum", out var min) && value.GetInt64() < min.GetInt64()) Invalid();
    }
}
