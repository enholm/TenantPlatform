using System.Text.Json;
using TenantPlatform.Web.Services.Reports;

static class ReportDisplayChecks
{
    public static void Run()
    {
        Check.That(ReportDisplaySettingsJson.Deserialize("{}").TitlePlacement == ReportTitlePlacement.Left,
            "an existing card without title placement defaults to left");

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
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.Deserialize("{\"titlePlacement\":\"unknown\"}"),
            "unknown explicit title placement is rejected");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.Deserialize("{\"titlePlacement\":2}"),
            "non-string title placement is rejected");
        Check.Throws<ReportDisplaySettingsException>(() =>
                ReportDisplaySettingsJson.SetTitlePlacement("{}", (ReportTitlePlacement)999),
            "unknown typed title placement is rejected before serialization");
    }
}
