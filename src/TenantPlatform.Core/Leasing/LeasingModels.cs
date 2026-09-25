namespace TenantPlatform.Core.Leasing;

public enum LeasingFrameworkStatus { Open = 1, Closed = 2, Finished = 3 }
public enum LeasingAcquisitionStatus { Draft = 1, Registered = 2, Cancelled = 3 }
public enum LeasingInterestKind { Fixed = 1, Floating = 2, Reference = 3 }
public enum LeasingPaymentFrequency { Monthly = 1, Quarterly = 3, HalfYearly = 6, Yearly = 12 }

public sealed class LeasingTerms
{
    public int Months { get; set; } = 60;
    public LeasingInterestKind InterestKind { get; set; } = LeasingInterestKind.Fixed;
    public decimal? AnnualRatePercent { get; set; } = 0;
    public string? ReferenceRateName { get; set; }
    public decimal? MarginPercentagePoints { get; set; }
    public LeasingPaymentFrequency PaymentFrequency { get; set; } = LeasingPaymentFrequency.Monthly;
    public LeasingTerms Copy() => new()
    {
        Months = Months, InterestKind = InterestKind, AnnualRatePercent = AnnualRatePercent,
        ReferenceRateName = ReferenceRateName, MarginPercentagePoints = MarginPercentagePoints, PaymentFrequency = PaymentFrequency
    };
}

public sealed class LeasingFramework
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public Guid FinanceOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateOnly AcquisitionFrom { get; set; }
    public DateOnly AcquisitionTo { get; set; }
    public decimal Limit { get; set; }
    public string Currency { get; set; } = "NOK";
    public bool IncludesVat { get; set; }
    public LeasingTerms Terms { get; set; } = new();
    public string? Notes { get; set; }
    public LeasingFrameworkStatus Status { get; set; } = LeasingFrameworkStatus.Open;
    public Guid Revision { get; set; }
}

public sealed class LeasingAcquisition
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? FrameworkId { get; set; }
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
    public Guid SupplierOrganizationId { get; set; }
    public Guid FinanceOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = "NOK";
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal FinancedAmount { get; set; }
    public LeasingTerms Terms { get; set; } = new();
    public string? Notes { get; set; }
    public LeasingAcquisitionStatus Status { get; set; } = LeasingAcquisitionStatus.Draft;
    public Guid Revision { get; set; }
    public List<LeasingItem> Items { get; set; } = [];
}

public sealed class LeasingItem
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AcquisitionId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public string? ItemNumber { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal VatPercent { get; set; } = 25;
}

public sealed class LeasingDocument
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? FrameworkId { get; set; }
    public Guid? AcquisitionId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long Size { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedUtc { get; set; }
}

public sealed class LeasingHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? FrameworkId { get; set; }
    public Guid? AcquisitionId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}
