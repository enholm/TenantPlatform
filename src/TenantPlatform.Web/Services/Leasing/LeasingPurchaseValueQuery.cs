using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;

internal sealed class LeasingPurchaseValueQueryRow
{
    public required LeasingAcquisition Acquisition { get; init; }
    public decimal OriginalNet { get; init; }
    public decimal OriginalVat { get; init; }
    public decimal OriginalGross { get; init; }
    public decimal AdjustedNet { get; init; }
    public decimal AdjustedVat { get; init; }
    public decimal AdjustedGross { get; init; }
}

internal static class LeasingPurchaseValueQuery
{
    internal static IQueryable<LeasingPurchaseValueQueryRow> Project(IQueryable<LeasingAcquisition> source) =>
        source.Select(x => new LeasingPurchaseValueQueryRow
        {
            Acquisition = x,
            OriginalNet = x.NetTotal,
            OriginalVat = x.VatTotal,
            OriginalGross = x.GrossTotal,
            AdjustedNet = x.NetTotal - x.CreditNetTotal - x.ReversedNetTotal,
            AdjustedVat = x.VatTotal - x.CreditVatTotal - x.ReversedVatTotal,
            AdjustedGross = x.GrossTotal - x.CreditNetTotal - x.CreditVatTotal -
                x.ReversedNetTotal - x.ReversedVatTotal
        });
}
