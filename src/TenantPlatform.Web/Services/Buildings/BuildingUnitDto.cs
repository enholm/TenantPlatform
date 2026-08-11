namespace TenantPlatform.Web.Services.Buildings;

public class BuildingUnitDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;
}
