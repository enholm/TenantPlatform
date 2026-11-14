using System.Text.Json;
using System.Text.Json.Nodes;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportTitlePlacement
{
    Hidden,
    Left,
    Center,
    Right
}

public sealed record ReportDisplaySettings(ReportTitlePlacement TitlePlacement);

public sealed class ReportDisplaySettingsException : Exception { }

public static class ReportDisplaySettingsJson
{
    private const string TitlePlacementProperty = "titlePlacement";

    public static ReportDisplaySettings Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ReportDisplaySettingsException();
            if (!document.RootElement.TryGetProperty(TitlePlacementProperty, out var value))
                return new(ReportTitlePlacement.Left);
            if (value.ValueKind != JsonValueKind.String)
                throw new ReportDisplaySettingsException();
            return new(value.GetString() switch
            {
                "hidden" => ReportTitlePlacement.Hidden,
                "left" => ReportTitlePlacement.Left,
                "center" => ReportTitlePlacement.Center,
                "right" => ReportTitlePlacement.Right,
                _ => throw new ReportDisplaySettingsException()
            });
        }
        catch (JsonException)
        {
            throw new ReportDisplaySettingsException();
        }
    }

    public static string SetTitlePlacement(string json, ReportTitlePlacement placement)
    {
        if (!Enum.IsDefined(placement)) throw new ReportDisplaySettingsException();
        if (Deserialize(json).TitlePlacement == placement) return json;
        JsonObject value;
        try
        {
            value = JsonNode.Parse(json) as JsonObject ?? throw new ReportDisplaySettingsException();
        }
        catch (JsonException)
        {
            throw new ReportDisplaySettingsException();
        }

        if (placement == ReportTitlePlacement.Left)
            value.Remove(TitlePlacementProperty);
        else
            value[TitlePlacementProperty] = placement switch
            {
                ReportTitlePlacement.Hidden => "hidden",
                ReportTitlePlacement.Center => "center",
                ReportTitlePlacement.Right => "right",
                _ => throw new ReportDisplaySettingsException()
            };
        return value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
