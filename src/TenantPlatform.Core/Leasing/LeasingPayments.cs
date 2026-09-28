namespace TenantPlatform.Core.Leasing;

public enum LeasingPlanStatus { Draft = 1, Active = 2, Replaced = 3 }
public enum LeasingPlanSource { Manual = 1, Imported = 2 }
public enum LeasingPaymentType { Rent = 1, AdvanceRent = 2, Fee = 3, ResidualObligation = 4, Other = 5 }
public enum LeasingBillingStatus { NotInvoiced = 1, Partial = 2, Reconciled = 3, Variance = 4, Accepted = 5 }
public enum LeasingPaymentTiming { Advance = 1, Arrears = 2 }
public enum LeasingRateLimitBasis { Reference = 1, TotalRate = 2 }

// Snapshots reuse the existing financing fields and LeasingTerms; the acquisition remains the original purchase record.
public sealed class LeasingFinancingSnapshot
{
    public Guid FinanceOrganizationId { get; set; }
    public string Currency { get; set; } = "NOK";
    public decimal FinancedAmount { get; set; }
    public LeasingTerms Terms { get; set; } = new();
    public static LeasingFinancingSnapshot From(LeasingAcquisition a) => new() { FinanceOrganizationId = a.FinanceOrganizationId, Currency = a.Currency, FinancedAmount = a.FinancedAmount, Terms = a.Terms.Copy() };
}
public sealed class LeasingFinancingRevision
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AcquisitionId { get; set; }
    // Null means legacy/initial information with UNKNOWN effective date, not purchase date.
    public DateOnly? EffectiveFrom { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset? RecordedUtc { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class LeasingPaymentPlan
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AcquisitionId { get; set; }
    public int Version { get; set; }
    public LeasingPlanStatus Status { get; set; }
    public LeasingPlanSource Source { get; set; }
    public string Currency { get; set; } = "NOK";
    public Guid? SourceDocumentId { get; set; }
    public string ContentHash { get; set; } = "";
    public Guid? BasedOnPlanId { get; set; }
    public Guid Revision { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid? CheckedByUserId { get; set; }
    public DateTimeOffset? CheckedUtc { get; set; }
    public string? Notes { get; set; }
    public List<LeasingPlanTerm> Terms { get; set; } = [];
}
// Stable obligation identity survives replacement of its plan snapshot and all allocation corrections.
public sealed class LeasingInstallment
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AcquisitionId { get; set; }
    public string Reference { get; set; } = "";
    public bool BillingComplete { get; set; }
    public bool VarianceAccepted { get; set; }
    public string? ControlReason { get; set; }
    public Guid? CheckedByUserId { get; set; }
    public DateTimeOffset? CheckedUtc { get; set; }
    public Guid Revision { get; set; }
}
public sealed class LeasingPlanTerm
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PlanId { get; set; }
    public Guid InstallmentId { get; set; }
    public string Reference { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly DueDate { get; set; }
    public LeasingPaymentType Type { get; set; } = LeasingPaymentType.Rent;
    public decimal? Net { get; set; }
    public decimal? Vat { get; set; }
    public decimal? Gross { get; set; }
    public decimal? CapitalComponent { get; set; }
    public decimal? InterestComponent { get; set; }
    public decimal? FeeComponent { get; set; }
    public bool ComponentsComplete { get; set; }
    public Guid? FinancingRevisionId { get; set; }
    public bool NeedsReview { get; set; }
    public string? ReviewReason { get; set; }
    public string? ObligationDocumentReference { get; set; }
}
public sealed class LeasingPaymentAllocation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InstallmentId { get; set; }
    public Guid? CreditedAllocationId { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public bool Reversed { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
}
public sealed class LeasingPaymentEvent
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? AcquisitionId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}
public static class LeasingPaymentCalculator
{
    // One cent on each of net, VAT and gross. No percentage tolerance or implicit price adjustment.
    public const decimal Tolerance = .01m;
    public static LeasingBillingStatus Status(LeasingPlanTerm term, LeasingInstallment obligation, decimal net, decimal vat, bool invoiced)
    {
        if (!invoiced && !obligation.BillingComplete) return LeasingBillingStatus.NotInvoiced;
        if (!obligation.BillingComplete) return LeasingBillingStatus.Partial;
        var matches = term.Net.HasValue && term.Vat.HasValue && term.Gross.HasValue && Math.Abs(net - term.Net.Value) <= Tolerance && Math.Abs(vat - term.Vat.Value) <= Tolerance && Math.Abs(net + vat - term.Gross.Value) <= Tolerance;
        return matches ? LeasingBillingStatus.Reconciled : obligation.VarianceAccepted ? LeasingBillingStatus.Accepted : LeasingBillingStatus.Variance;
    }
    // Dates are anchored to the FIRST date, never the previous generated date. Preserve month end when requested.
    public static DateOnly Anchored(DateOnly first, int months, bool monthEnd)
    {
        var target = new DateOnly(first.Year, first.Month, 1).AddMonths(months);
        return new(target.Year, target.Month, monthEnd ? DateTime.DaysInMonth(target.Year, target.Month) : Math.Min(first.Day, DateTime.DaysInMonth(target.Year, target.Month)));
    }
    public static List<LeasingPlanTerm> Series(DateOnly firstFrom, DateOnly firstTo, DateOnly firstDue, int count, LeasingPaymentFrequency frequency, bool dueAtMonthEnd, decimal? net, decimal? vat)
    {
        if (count is < 1 or > 600 || !Enum.IsDefined(frequency) || firstFrom == default || firstTo < firstFrom || firstDue == default) throw new ArgumentOutOfRangeException(nameof(count));
        var fromEnd = firstFrom.Day == DateTime.DaysInMonth(firstFrom.Year, firstFrom.Month);
        var toEnd = firstTo.Day == DateTime.DaysInMonth(firstTo.Year, firstTo.Month);
        return Enumerable.Range(0,count).Select(i => new LeasingPlanTerm { Reference = (i+1).ToString(), PeriodFrom = Anchored(firstFrom, i*(int)frequency, fromEnd), PeriodTo = Anchored(firstTo, i*(int)frequency, toEnd), DueDate = Anchored(firstDue, i*(int)frequency, dueAtMonthEnd), Net = net, Vat = vat, Gross = net + vat }).ToList();
    }
}
