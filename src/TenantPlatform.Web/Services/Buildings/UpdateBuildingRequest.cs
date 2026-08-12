namespace TenantPlatform.Web.Services.Buildings;

public class UpdateBuildingRequest
{
    public string Name { get; init; } = string.Empty;

    public string? AddressLine1 { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string CountryCode { get; init; } = "NO";
}
