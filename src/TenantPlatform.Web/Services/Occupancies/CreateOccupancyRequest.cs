namespace TenantPlatform.Web.Services.Occupancies;

public class CreateOccupancyRequest
{
    public Guid TenantOrganizationId { get; init; }

    public Guid UnitId { get; init; }

    public DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }
}

