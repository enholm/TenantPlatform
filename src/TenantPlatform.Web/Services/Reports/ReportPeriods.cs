using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public sealed record ReportPeriodSelection(
    ReportPeriodRule Rule,
    DateOnly? CustomFrom = null,
    DateOnly? CustomTo = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Rule)) Invalid();
        if (Rule == ReportPeriodRule.Custom)
        {
            if (!CustomFrom.HasValue || !CustomTo.HasValue || CustomFrom > CustomTo) Invalid();
        }
        else if (CustomFrom.HasValue || CustomTo.HasValue) Invalid();
    }

    private static void Invalid() => throw new ReportPeriodValidationException("ReportPeriodInvalid");
}

public sealed record ReportDateRange
{
    public ReportDateRange(DateOnly from, DateOnly to)
    {
        if (from > to) throw new ReportPeriodValidationException("ReportPeriodInvalid");
        From = from;
        To = to;
    }

    public DateOnly From { get; }
    public DateOnly To { get; }
}

public sealed record ReportTemporalSelection(
    ReportPeriodSelection Period,
    ReportTimeGrouping? TimeGrouping = null)
{
    public void Validate()
    {
        if (Period is null) throw new ReportPeriodValidationException("ReportPeriodInvalid");
        Period.Validate();
        if (TimeGrouping.HasValue) ReportTimeBuckets.Validate(TimeGrouping.Value);
    }
}

public sealed class ReportPeriodValidationException(string resourceKey) : Exception(resourceKey);

public static class ReportPeriodResolver
{
    public static ReportDateRange Resolve(ReportPeriodSelection selection, DateOnly today)
    {
        if (selection is null) throw new ReportPeriodValidationException("ReportPeriodInvalid");
        selection.Validate();
        try
        {
            return selection.Rule switch
            {
                ReportPeriodRule.CurrentMonth => Calendar(today, 1, 0),
                ReportPeriodRule.PreviousMonth => Calendar(today, 1, -1),
                ReportPeriodRule.NextMonth => Calendar(today, 1, 1),
                ReportPeriodRule.CurrentQuarter => Calendar(today, 3, 0),
                ReportPeriodRule.PreviousQuarter => Calendar(today, 3, -1),
                ReportPeriodRule.NextQuarter => Calendar(today, 3, 1),
                ReportPeriodRule.CurrentTertial => Calendar(today, 4, 0),
                ReportPeriodRule.PreviousTertial => Calendar(today, 4, -1),
                ReportPeriodRule.NextTertial => Calendar(today, 4, 1),
                ReportPeriodRule.CurrentHalfYear => Calendar(today, 6, 0),
                ReportPeriodRule.PreviousHalfYear => Calendar(today, 6, -1),
                ReportPeriodRule.NextHalfYear => Calendar(today, 6, 1),
                ReportPeriodRule.CurrentYear => Calendar(today, 12, 0),
                ReportPeriodRule.PreviousYear => Calendar(today, 12, -1),
                ReportPeriodRule.NextYear => Calendar(today, 12, 1),
                ReportPeriodRule.YearToDate => new(new DateOnly(today.Year, 1, 1), today),
                ReportPeriodRule.Rolling12Months => new(today.AddMonths(-12).AddDays(1), today),
                ReportPeriodRule.Custom => new(selection.CustomFrom!.Value, selection.CustomTo!.Value),
                _ => throw new ReportPeriodValidationException("ReportPeriodInvalid")
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ReportPeriodValidationException("ReportPeriodInvalid");
        }
    }

    private static ReportDateRange Calendar(DateOnly today, int months, int offset)
    {
        var firstMonth = ((today.Month - 1) / months) * months + 1;
        var from = new DateOnly(today.Year, firstMonth, 1).AddMonths(offset * months);
        return new(from, from.AddMonths(months).AddDays(-1));
    }
}
