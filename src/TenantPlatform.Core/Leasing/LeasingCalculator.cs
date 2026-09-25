namespace TenantPlatform.Core.Leasing;

public sealed record LeasingTotals(decimal Net, decimal Vat)
{
    public decimal Gross => Net + Vat;
}

public static class LeasingCalculator
{
    // Same monetary rounding as agreement periods: two decimals, midpoint away from zero.
    // Round each line's net and VAT before summing. No negative credit lines in phase 1.
    public static LeasingTotals Line(LeasingItem item)
    {
        if (item.Quantity <= 0 || item.UnitPrice < 0 || item.VatPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(item));
        var net = decimal.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
        return new(net, decimal.Round(net * item.VatPercent / 100m, 2, MidpointRounding.AwayFromZero));
    }
    public static LeasingTotals Total(IEnumerable<LeasingItem> items)
    {
        var lines = items.Select(Line).ToList();
        return new(lines.Sum(x => x.Net), lines.Sum(x => x.Vat));
    }
    // Calendar-month anniversary, clamped to the last day of the destination month.
    // E.g. 29 February 2028 + 12 months = 28 February 2029. The end date is not reduced by a day.
    public static DateOnly EndDate(DateOnly purchaseDate, int months)
    {
        if (purchaseDate == default || months is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(months));
        return purchaseDate.AddMonths(months);
    }
    public static bool InPeriod(DateOnly purchaseDate, DateOnly from, DateOnly to) => purchaseDate >= from && purchaseDate <= to;
    public static decimal Used(IEnumerable<LeasingAcquisition> acquisitions, bool includesVat) =>
        acquisitions.Where(x => x.Status == LeasingAcquisitionStatus.Registered).Sum(x => includesVat ? x.GrossTotal : x.NetTotal);
}
