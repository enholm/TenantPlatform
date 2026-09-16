namespace TenantPlatform.Core.Agreements;

public class AgreementAccess
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid UserId { get; set; }
    public AgreementAccessLevel Level { get; set; }
}
