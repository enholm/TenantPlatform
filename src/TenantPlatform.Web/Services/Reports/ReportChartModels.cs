using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportChartKind { Bar, Line, Donut }

public sealed record ReportChartCategory(string Key, string Label);

public sealed record ReportChartSeries(
    string Key,
    string Label,
    string? Currency,
    IReadOnlyList<decimal?> Values);

public sealed record ReportChartModel(
    ReportChartKind Kind,
    IReadOnlyList<ReportChartCategory> Categories,
    IReadOnlyList<ReportChartSeries> Series,
    ReportValueUnit Unit,
    string AriaLabel,
    bool NonAdditive);

public sealed class ReportChartMappingException(string resourceKey) : Exception(resourceKey);

public static class ReportChartMapper
{
    private const string UnitSeriesKey = "__unit";

    public static ReportChartModel Map(
        ReportVisualization visualization,
        ReportExecutionResult result,
        Func<ReportExecutionLabel, string> label,
        Func<ReportTimeResult, string> timeLabel,
        string unitLabel,
        string ariaLabel)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(timeLabel);

        var kind = visualization switch
        {
            ReportVisualization.Bar => ReportChartKind.Bar,
            ReportVisualization.Line => ReportChartKind.Line,
            ReportVisualization.Donut => ReportChartKind.Donut,
            _ => throw new ReportChartMappingException("ReportChartUnsupported")
        };

        var points = result.TimeSeries.Count > 0
            ? result.TimeSeries.OrderBy(x => x.Year).ThenBy(x => x.Bucket)
                .Select(x => new Point(x.Key, timeLabel(x), x.Values)).ToList()
            : result.Groups.Select(x => new Point(x.Key, label(x.Label), x.Values)).ToList();

        if (kind == ReportChartKind.Line && result.TimeSeries.Count == 0)
            throw new ReportChartMappingException("ReportChartInvalid");
        if (kind == ReportChartKind.Donut)
        {
            if (result.TimeSeries.Count > 0 || result.Metadata.CurrencyRule != ReportCurrencyRule.None ||
                result.Metadata.MultipleGroupMembership || !result.Metadata.GroupTotalsAdditive ||
                points.SelectMany(x => x.Values).Any(x => x.Value < 0))
                throw new ReportChartMappingException("ReportChartInvalidDonut");
        }

        var seriesKeys = points.SelectMany(x => x.Values)
            .Select(x => SeriesKey(x.Currency)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var series = seriesKeys.Select(key => new ReportChartSeries(
            key,
            key == UnitSeriesKey ? unitLabel : key,
            key == UnitSeriesKey ? null : key,
            points.Select(point => Value(point.Values, key)).ToList())).ToList();

        if (kind == ReportChartKind.Donut && series.Count != 1)
            throw new ReportChartMappingException("ReportChartInvalidDonut");

        return new(kind,
            points.Select(x => new ReportChartCategory(x.Key, x.Label)).ToList(),
            series,
            result.Metadata.Unit,
            ariaLabel,
            result.Metadata.MultipleGroupMembership || !result.Metadata.GroupTotalsAdditive);
    }

    private static decimal? Value(IReadOnlyList<ReportExecutionValue> values, string key)
    {
        var matching = values.Where(x => SeriesKey(x.Currency) == key).ToList();
        if (matching.Count > 1) throw new ReportChartMappingException("ReportChartInvalid");
        return matching.Count == 0 ? null : matching[0].Value;
    }

    private static string SeriesKey(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? UnitSeriesKey : currency.Trim().ToUpperInvariant();

    private sealed record Point(string Key, string Label, IReadOnlyList<ReportExecutionValue> Values);
}
