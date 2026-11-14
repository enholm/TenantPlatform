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

public sealed record ReportDisplaySettings(ReportTitlePlacement TitlePlacement, string? Subtitle);

public sealed class ReportDisplaySettingsException(string field = "display") : Exception
{
    public string Field { get; } = field;
}

public static class ReportDisplaySettingsJson
{
    public const int MaximumSubtitleLength = 200;
    private const string TitlePlacementProperty = "titlePlacement";
    private const string SubtitleProperty = "subtitle";

    public static ReportDisplaySettings Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ReportDisplaySettingsException();
            var placement = ReportTitlePlacement.Left;
            if (document.RootElement.TryGetProperty(TitlePlacementProperty, out var value))
            {
                if (value.ValueKind != JsonValueKind.String)
                    throw new ReportDisplaySettingsException("titlePlacement");
                placement = value.GetString() switch
                {
                    "hidden" => ReportTitlePlacement.Hidden,
                    "left" => ReportTitlePlacement.Left,
                    "center" => ReportTitlePlacement.Center,
                    "right" => ReportTitlePlacement.Right,
                    _ => throw new ReportDisplaySettingsException("titlePlacement")
                };
            }

            string? subtitle = null;
            if (document.RootElement.TryGetProperty(SubtitleProperty, out var subtitleValue) &&
                subtitleValue.ValueKind != JsonValueKind.Null)
            {
                if (subtitleValue.ValueKind != JsonValueKind.String)
                    throw new ReportDisplaySettingsException("subtitle");
                subtitle = NormalizeSubtitle(subtitleValue.GetString());
            }
            return new(placement, subtitle);
        }
        catch (JsonException)
        {
            throw new ReportDisplaySettingsException();
        }
    }

    public static string SetTitlePlacement(string json, ReportTitlePlacement placement)
    {
        if (!Enum.IsDefined(placement)) throw new ReportDisplaySettingsException("titlePlacement");
        if (Deserialize(json).TitlePlacement == placement) return json;
        var value = ParseObject(json);

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

    public static string SetSubtitle(string json, string? subtitle)
    {
        var normalized = NormalizeSubtitle(subtitle);
        if (Deserialize(json).Subtitle == normalized) return json;
        var value = ParseObject(json);
        if (normalized is null)
            value.Remove(SubtitleProperty);
        else
            value[SubtitleProperty] = normalized;
        return value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string? NormalizeSubtitle(string? subtitle)
    {
        if (subtitle is null) return null;
        if (subtitle.Length > MaximumSubtitleLength)
            throw new ReportDisplaySettingsException("subtitle");
        var normalized = subtitle.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? throw new ReportDisplaySettingsException();
        }
        catch (JsonException)
        {
            throw new ReportDisplaySettingsException();
        }
    }
}
