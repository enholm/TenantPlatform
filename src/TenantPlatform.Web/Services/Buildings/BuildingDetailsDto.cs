
namespace TenantPlatform.Web.Services.Buildings;

public class BuildingDetailsDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? AddressLine1 { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string? CountryCode { get; init; }

    public int UnitCount { get; init; }

    public int TenantCount { get; init; }

    public int OpenRequestCount { get; init; }

    public List<BuildingUnitDto> Units { get; init; } = [];
}
