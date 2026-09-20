using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public class SaveAgreementLineRequest
{
    public Guid Revision { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly FirstPayableDate { get; set; }
    public Guid? PayableSourceLineId { get; set; }
    public int? PayableOffsetMonths { get; set; }
    public AgreementFrequency Frequency { get; set; } = AgreementFrequency.Monthly;
    public AgreementAnchor Anchor { get; set; } = AgreementAnchor.Calendar;
    public DateOnly AnchorDate { get; set; }
    public AgreementBillingTiming BillingTiming { get; set; } = AgreementBillingTiming.Advance;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public bool Activate { get; set; }
    public string? Reason { get; set; }
    public bool IndexRegulated { get; set; }
    public List<Guid> DocumentIds { get; set; } = [];
}

public record AgreementLineDetails(AgreementLine Line, List<AgreementLineVersion> Versions, List<AgreementPriceVersion> Prices);
public record AgreementLinesDto(Guid Revision, bool CanEdit, AgreementDirection? Direction, string? Currency,
    List<AgreementLineDetails> Lines, List<AgreementDocumentDto> Documents,
    Dictionary<Guid, string> ActorNames);
public record AgreementForecastDto(List<AgreementCalculatedPeriod> Periods, decimal Income, decimal Cost, string Currency);
