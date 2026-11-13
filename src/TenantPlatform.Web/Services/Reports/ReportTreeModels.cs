namespace TenantPlatform.Web.Services.Reports;

public sealed record ReportTreeChartNode(
    string Key,
    string Name,
    string? Context,
    bool Selected,
    IReadOnlyList<ReportTreeChartNode> Children);

public sealed record ReportTreeChartModel(ReportTreeChartNode Root, string AriaLabel);
