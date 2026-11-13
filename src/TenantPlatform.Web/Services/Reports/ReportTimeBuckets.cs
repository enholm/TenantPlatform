using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public readonly record struct ReportTimeBucketKey(ReportTimeGrouping Grouping, int Year, int Bucket)
    : IComparable<ReportTimeBucketKey>
{
    public string MachineKey => $"{Prefix(Grouping)}:{Year:D4}:{Bucket:D2}";

    public int CompareTo(ReportTimeBucketKey other)
    {
        var grouping = Grouping.CompareTo(other.Grouping);
        if (grouping != 0) return grouping;
        var year = Year.CompareTo(other.Year);
        return year != 0 ? year : Bucket.CompareTo(other.Bucket);
    }

    private static string Prefix(ReportTimeGrouping grouping) => grouping switch
    {
        ReportTimeGrouping.Month => "month",
        ReportTimeGrouping.Quarter => "quarter",
        ReportTimeGrouping.Tertial => "tertial",
        ReportTimeGrouping.HalfYear => "half-year",
        ReportTimeGrouping.Year => "year",
        _ => throw new ReportPeriodValidationException("ReportTimeGroupingInvalid")
    };
}

public sealed record ReportTimeBucket(
    ReportTimeBucketKey Key,
    DateOnly FullFrom,
    DateOnly FullTo,
    DateOnly OverlapFrom,
    DateOnly OverlapTo)
{
    public bool IsPartial => FullFrom != OverlapFrom || FullTo != OverlapTo;
}

public static class ReportTimeBuckets
{
    public static IReadOnlyList<ReportTimeBucket> Create(ReportTimeGrouping grouping, ReportDateRange period)
    {
        Validate(grouping);
        if (period is null) throw new ReportPeriodValidationException("ReportPeriodInvalid");
        var result = new List<ReportTimeBucket>();
        var cursor = period.From;
        while (true)
        {
            var full = FullBucket(grouping, cursor);
            result.Add(new(full.Key, full.From, full.To,
                Max(full.From, period.From), Min(full.To, period.To)));
            if (full.To >= period.To) break;
            cursor = full.To.AddDays(1);
        }
        return result;
    }

    public static ReportTimeBucket Full(ReportTimeGrouping grouping, DateOnly date)
    {
        Validate(grouping);
        var full = FullBucket(grouping, date);
        return new(full.Key, full.From, full.To, full.From, full.To);
    }

    public static void Validate(ReportTimeGrouping grouping)
    {
        if (!Enum.IsDefined(grouping))
            throw new ReportPeriodValidationException("ReportTimeGroupingInvalid");
    }

    private static (ReportTimeBucketKey Key, DateOnly From, DateOnly To) FullBucket(
        ReportTimeGrouping grouping, DateOnly date)
    {
        var (months, bucket) = grouping switch
        {
            ReportTimeGrouping.Month => (1, date.Month),
            ReportTimeGrouping.Quarter => (3, (date.Month - 1) / 3 + 1),
            ReportTimeGrouping.Tertial => (4, (date.Month - 1) / 4 + 1),
            ReportTimeGrouping.HalfYear => (6, (date.Month - 1) / 6 + 1),
            ReportTimeGrouping.Year => (12, 1),
            _ => throw new ReportPeriodValidationException("ReportTimeGroupingInvalid")
        };
        var firstMonth = (bucket - 1) * months + 1;
        var from = new DateOnly(date.Year, firstMonth, 1);
        var to = from.AddMonths(months).AddDays(-1);
        return (new(grouping, date.Year, bucket), from, to);
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;
    private static DateOnly Min(DateOnly left, DateOnly right) => left <= right ? left : right;
}
