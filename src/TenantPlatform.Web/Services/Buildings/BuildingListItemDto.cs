namespace TenantPlatform.Web.Services.Buildings;

public class BuildingListItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? AddressLine1 { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }
}
