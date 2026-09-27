namespace TenantPlatform.Core.Leasing;

public enum LeasingInvoiceKind { Invoice = 1, CreditNote = 2 }
public enum LeasingInvoiceStatus { Review = 1, Approved = 2, Rejected = 3, Reversed = 4 }
public enum LeasingInvoiceProcessing { Uploaded = 1, Processing = 2, Ready = 3, Failed = 4 }
public sealed class LeasingInvoice
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? AcquisitionId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedUtc { get; set; }
    public string FileName { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string FileHash { get; set; } = "";
    public long Size { get; set; }
    public LeasingInvoiceProcessing Processing { get; set; } = LeasingInvoiceProcessing.Uploaded;
    public LeasingInvoiceStatus Status { get; set; } = LeasingInvoiceStatus.Review;
    public DateTimeOffset? ProcessingStartedUtc { get; set; }
    public Guid? ProcessingToken { get; set; }
    public string? ProcessingError { get; set; }
    public string ReviewJson { get; set; } = "{}";
    public string? ApprovedJson { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public LeasingInvoiceKind Kind { get; set; }
    public string SupplierIdentity { get; set; } = "";
    public string Number { get; set; } = "";
    public DateOnly? InvoiceDate { get; set; }
    public string Currency { get; set; } = "";
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public bool ReleasesLimit { get; set; }
    public Guid Revision { get; set; }
    public List<LeasingInvoiceLine> Lines { get; set; } = [];
}
public sealed class LeasingInvoiceLine
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? CreditedLineId { get; set; }
    public string SourceLineId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public bool CreatedItem { get; set; }
}
public sealed class LeasingInvoiceInterpretation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid InvoiceId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string Version { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public string ExtractedText { get; set; } = "";
    public string WarningsJson { get; set; } = "[]";
}
public sealed class LeasingInvoiceHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}
// Independent source/review shape: never deserialize documents into acquisition entities.
public sealed class InvoiceData
{
    public LeasingInvoiceKind Kind { get; set; } = LeasingInvoiceKind.Invoice;
    public string SupplierName { get; set; } = "";
    public string SupplierNumber { get; set; } = "";
    public string Number { get; set; } = "";
    public DateOnly? Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public string Currency { get; set; } = "";
    public string OriginalInvoiceReference { get; set; } = "";
    public string OrderReference { get; set; } = "";
    public string DeliveryReference { get; set; } = "";
    public decimal? Net { get; set; }
    public decimal? Vat { get; set; }
    public decimal? Gross { get; set; }
    public decimal? Prepaid { get; set; }
    public decimal? Rounding { get; set; }
    public decimal? Payable { get; set; }
    public List<InvoiceDataLine> Lines { get; set; } = [];
    public List<InvoiceTax> Taxes { get; set; } = [];
    public List<string> Uncertainties { get; set; } = [];
}
public sealed class InvoiceDataLine
{
    public Guid ReviewId { get; set; } = Guid.NewGuid();
    public string SourceId { get; set; } = "";
    public string Description { get; set; } = "";
    public string ItemNumber { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    public decimal BaseQuantity { get; set; } = 1;
    public decimal Allowance { get; set; }
    public decimal Charge { get; set; }
    public decimal? Net { get; set; }
    public decimal? VatPercent { get; set; }
    public string VatCategory { get; set; } = "";
    public string TaxExemptionReason { get; set; } = "";
    public bool DocumentAdjustment { get; set; }
    public bool IsAllowance { get; set; }
    public string Reference { get; set; } = "";
    public int? Page { get; set; }
    public string? BoundingBox { get; set; }
}
public sealed class InvoiceTax
{
    public string Category { get; set; } = "";
    public decimal Rate { get; set; }
    public decimal Taxable { get; set; }
    public decimal Amount { get; set; }
}
public sealed record InvoiceAmounts(decimal Net, decimal Vat, List<decimal> LineNet, List<decimal> LineVat, List<InvoiceTax> Taxes, List<string> Errors)
{
    public decimal Gross => Net + Vat;
}
public static class InvoiceCalculator
{
    public static decimal Money(decimal n) => decimal.Round(n, 2, MidpointRounding.AwayFromZero);
    public static InvoiceAmounts Calculate(InvoiceData data)
    {
        var errors = new List<string>(); var nets = new List<decimal>(); var vats = data.Lines.Select(_ => 0m).ToList();
        if (data.Lines.Count == 0 || data.Lines.Count > 500) errors.Add("InvoiceLinesRequired");
        foreach (var line in data.Lines)
        {
            if (line.Quantity is null or <= 0 or > 1000000000m || line.Price is null or < 0 or > 1000000000000m || line.BaseQuantity is < 0.0001m or > 1000000000m ||
                line.VatPercent is null or < 0 or > 100 || string.IsNullOrWhiteSpace(line.VatCategory) || line.Allowance is < 0 or > 1000000000000m || line.Charge is < 0 or > 1000000000000m ||
                string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500 || (line.ItemNumber?.Length ?? 0) > 100 || (line.SourceId?.Length ?? 0) > 200 ||
                decimal.Round(line.Quantity ?? 0, 4) != line.Quantity) { errors.Add("InvoiceInvalidLine"); nets.Add(0); continue; }
            var net = Money(line.Quantity.Value * line.Price.Value / line.BaseQuantity - line.Allowance + line.Charge) * (line.IsAllowance ? -1 : 1);
            if (Math.Abs(net) > 1000000000000m || (net < 0 && !line.DocumentAdjustment) || line.Net != net) errors.Add("InvoiceLineMismatch");
            nets.Add(net);
        }
        var taxes = new List<InvoiceTax>();
        foreach (var group in data.Lines.Select((l,i) => (l,i)).GroupBy(x => (x.l.VatCategory, Rate: x.l.VatPercent is >= 0 and <= 100 ? x.l.VatPercent.Value : 0)))
        {
            decimal cumulative = 0, previous = 0;
            foreach (var (line,index) in group) { cumulative += nets[index]; var tax = Money(cumulative * group.Key.Rate / 100); vats[index] = tax - previous; previous = tax; }
            taxes.Add(new() { Category = group.Key.VatCategory, Rate = group.Key.Rate, Taxable = cumulative, Amount = previous });
        }
        if (data.Taxes.Count > 0 && (data.Taxes.Count != taxes.Count || taxes.Any(t => !data.Taxes.Any(x => x.Category == t.Category && x.Rate == t.Rate && x.Taxable == t.Taxable && x.Amount == t.Amount)))) errors.Add("InvoiceTaxMismatch");
        var sum = nets.Sum(); var vat = vats.Sum();
        if (sum < 0 || vat < 0 || sum > 1000000000000m || sum != data.Net || vat != data.Vat || sum + vat != data.Gross) errors.Add("InvoiceTotalMismatch");
        if ((data.Prepaid ?? 0) < 0 || Money(data.Prepaid ?? 0) != (data.Prepaid ?? 0) || Math.Abs(data.Rounding ?? 0) > 1 || Money(data.Rounding ?? 0) != (data.Rounding ?? 0) ||
            data.Payable.HasValue && data.Payable != Money(sum + vat - (data.Prepaid ?? 0) + (data.Rounding ?? 0))) errors.Add("InvoicePayableMismatch");
        return new(sum, vat, nets, vats, taxes, errors.Distinct().ToList());
    }
}
