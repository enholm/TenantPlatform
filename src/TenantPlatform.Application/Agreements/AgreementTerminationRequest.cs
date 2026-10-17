using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public class AgreementTerminationRequest
{
    public Guid Revision { get; set; }
    public AgreementNoticeAction Action { get; set; } = AgreementNoticeAction.Registered;
    public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Comment { get; set; }
}

