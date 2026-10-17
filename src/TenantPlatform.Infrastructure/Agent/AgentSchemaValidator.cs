using System.Text.Json;
using Json.Schema;
using TenantPlatform.Application.Agent;

namespace TenantPlatform.Infrastructure.Agent;

// Deliberately bounded first-release subset. Reject unsupported contracts; never rewrite an external schema.
public sealed class AgentSchemaValidator : IAgentSchemaValidator
{
    private static readonly HashSet<string> Keywords = ["type", "format", "properties", "required", "additionalProperties", "items", "enum", "const",
        "description", "title", "default", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "minItems", "maxItems", "uniqueItems", "minProperties", "maxProperties", "$schema"];
    private static BuildOptions Build() => new() { SchemaRegistry = new SchemaRegistry { Fetch = (_, _) => null! } };
    public bool Supported(string schema)
    {
        try
        {
            if (schema.Length > 16000) return false;
            using var doc = JsonDocument.Parse(schema, new() { MaxDepth = 16 });
            var root = doc.RootElement;
            if (!Unique(root) || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "object" || !Node(root, 0)) return false;
            _ = JsonSchema.FromText(schema, Build()); return true;
        }
        catch (Exception) { return false; }
    }
    public bool Valid(string schema, string arguments)
    {
        try
        {
            if (!Supported(schema) || arguments.Length > 16384) return false;
            using var doc = JsonDocument.Parse(arguments, new() { MaxDepth = 16 });
            return Unique(doc.RootElement) && JsonSchema.FromText(schema, Build()).Evaluate(doc.RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid;
        }
        catch (Exception) { return false; }
    }
    private static bool Node(JsonElement value, int depth)
    {
        if (depth > 12) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (!Keywords.Contains(property.Name)) return false; // Includes all references, regex and extension vocabularies.
            if (property.Name == "format" && property.Value.GetString() != "uuid") return false;
            if (property.Name == "$schema" && property.Value.GetString() != "https://json-schema.org/draft/2020-12/schema") return false;
            if (property.Name == "properties" && (property.Value.ValueKind != JsonValueKind.Object || property.Value.EnumerateObject().Count() > 64 ||
                property.Value.EnumerateObject().Any(p => !Node(p.Value, depth + 1)))) return false;
            if (property.Name is "items" or "additionalProperties" && !Node(property.Value, depth + 1)) return false;
        }
        return true;
    }
    private static bool Unique(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count() && value.EnumerateObject().All(p => Unique(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(Unique), _ => true
    };
}
