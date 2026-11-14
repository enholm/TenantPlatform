namespace TenantPlatform.Web.Services.Reports;

public sealed record ReportGridItem(
    Guid ClientId,
    int Column,
    int Row,
    int Width,
    int Height,
    int SortOrder);

public sealed record ReportGridLayoutChange(
    Guid ClientId,
    int Column,
    int Row,
    int Width,
    int Height,
    long Generation);

public static class ReportLayoutEngine
{
    public const int Columns = 12;
    public const int MinimumInteractiveWidth = 3;
    public const int MinimumInteractiveHeight = 3;
    public const int MaximumInteractiveHeight = 20;

    public static IReadOnlyList<ReportGridItem> Normalize(
        IEnumerable<ReportGridItem> source,
        Guid? preferredId = null)
    {
        var input = source.ToList();
        var placed = new List<ReportGridItem>(input.Count);
        var ordered = input.OrderBy(x => x.ClientId == preferredId ? 0 : 1)
            .ThenBy(x => x.SortOrder).ThenBy(x => x.ClientId).ToList();

        foreach (var raw in ordered)
        {
            var width = Math.Clamp(raw.Width, 1, Columns);
            var height = Math.Max(1, raw.Height);
            var requestedColumn = Math.Clamp(raw.Column, 1, Columns - width + 1);
            var requestedRow = Math.Max(1, raw.Row);
            var candidate = FindFree(raw with
            {
                Column = requestedColumn,
                Row = requestedRow,
                Width = width,
                Height = height
            }, placed);
            placed.Add(candidate);
        }

        return placed.OrderBy(x => x.SortOrder).ThenBy(x => x.ClientId).ToList();
    }

    public static ReportGridItem ClampInteraction(ReportGridItem item) => item with
    {
        Width = Math.Clamp(item.Width, MinimumInteractiveWidth, Columns),
        Height = Math.Clamp(item.Height, MinimumInteractiveHeight, MaximumInteractiveHeight),
        Column = Math.Clamp(item.Column, 1,
            Columns - Math.Clamp(item.Width, MinimumInteractiveWidth, Columns) + 1),
        Row = Math.Max(1, item.Row)
    };

    private static ReportGridItem FindFree(ReportGridItem item, IReadOnlyList<ReportGridItem> placed)
    {
        for (var row = item.Row; ; row++)
        {
            foreach (var column in ColumnsFrom(item.Column, item.Width))
            {
                var candidate = item with { Column = column, Row = row };
                if (placed.All(other => !Overlaps(candidate, other))) return candidate;
            }
        }
    }

    private static IEnumerable<int> ColumnsFrom(int requested, int width)
    {
        yield return requested;
        for (var column = 1; column <= Columns - width + 1; column++)
            if (column != requested) yield return column;
    }

    private static bool Overlaps(ReportGridItem left, ReportGridItem right) =>
        left.Column < right.Column + right.Width && left.Column + left.Width > right.Column &&
        left.Row < right.Row + right.Height && left.Row + left.Height > right.Row;
}
