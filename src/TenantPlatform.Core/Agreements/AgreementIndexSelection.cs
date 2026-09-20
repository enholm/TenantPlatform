namespace TenantPlatform.Core.Agreements;

// Append-only agreement-level index selection history; changes apply from the day of the switch.
public class AgreementIndexSelection
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid? IndexId { get; set; }
    public int Sequence { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public Guid ActorUserId { get; set; }
}
