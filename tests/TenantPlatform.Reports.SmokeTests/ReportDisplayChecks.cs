using System.Text.Json;
using TenantPlatform.Web.Services.Reports;

static class ReportDisplayChecks
{
    public static void Run()
    {
        var defaults = ReportDisplaySettingsJson.Deserialize("{}");
        Check.That(defaults.TitlePlacement == ReportTitlePlacement.Left && defaults.Subtitle is null,
            "an existing card without display settings defaults to a left title and empty subtitle");

        foreach (var placement in Enum.GetValues<ReportTitlePlacement>())
        {
            var json = ReportDisplaySettingsJson.SetTitlePlacement(
                "{\"showLegend\":false,\"titlePlacement\":\"right\"}", placement);
            Check.That(ReportDisplaySettingsJson.Deserialize(json).TitlePlacement == placement,
                $"title placement {placement} has a typed serialization roundtrip");
            using var document = JsonDocument.Parse(json);
            Check.That(document.RootElement.TryGetProperty("showLegend", out var legend) && !legend.GetBoolean(),
                $"title placement {placement} preserves unrelated display settings");
        }

        var unchanged = "{ \"showLegend\": false }";
        Check.That(ReportDisplaySettingsJson.SetTitlePlacement(unchanged, ReportTitlePlacement.Left) == unchanged,
            "default left placement preserves the original display JSON without dirtying an unchanged card");
        var subtitleJson = ReportDisplaySettingsJson.SetSubtitle(
            "{\"showLegend\":false,\"titlePlacement\":\"right\"}", "  A custom subtitle  ");
        var subtitle = ReportDisplaySettingsJson.Deserialize(subtitleJson);
        Check.That(subtitle.Subtitle == "A custom subtitle" && subtitle.TitlePlacement == ReportTitlePlacement.Right,
            "subtitle has a trimmed typed serialization roundtrip without changing title placement");
        using (var document = JsonDocument.Parse(subtitleJson))
            Check.That(document.RootElement.TryGetProperty("showLegend", out var legend) && !legend.GetBoolean(),
                "subtitle serialization preserves unrelated display settings");
        var emptySubtitleJson = ReportDisplaySettingsJson.SetSubtitle(subtitleJson, "   ");
        using (var emptyDocument = JsonDocument.Parse(emptySubtitleJson))
            Check.That(ReportDisplaySettingsJson.Deserialize(emptySubtitleJson).Subtitle is null &&
                !emptyDocument.RootElement.TryGetProperty("subtitle", out _),
                "empty or whitespace-only subtitle is stored as no subtitle");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.Deserialize("{\"titlePlacement\":\"unknown\"}"),
            "unknown explicit title placement is rejected");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.Deserialize("{\"titlePlacement\":2}"),
            "non-string title placement is rejected");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.SetTitlePlacement("{}", (ReportTitlePlacement)999),
            "unknown typed title placement is rejected before serialization");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.Deserialize("{\"subtitle\":42}"),
            "non-string subtitle is rejected");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.SetSubtitle("{}", new string('x', 201)),
            "subtitle exceeding the shared maximum length is rejected");
    }
}
