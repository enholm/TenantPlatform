namespace TenantPlatform.Core.Agreements;

public class Agreement
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AgreementType Type { get; set; }
    public Guid CounterpartyOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public AgreementStatus Status { get; set; } = AgreementStatus.Draft;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? NoticeDeadline { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalMonths { get; set; }
    public string? Terms { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? UnitId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid Revision { get; set; }
}
