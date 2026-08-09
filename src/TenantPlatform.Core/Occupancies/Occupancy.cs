namespace TenantPlatform.Core.Occupancies;

public class Occupancy
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TenantOrganizationId { get; set; }

    public Guid UnitId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }
}
