namespace TenantPlatform.Web.Services.Buildings;

public class CreateBuildingRequest
{
    public string Name { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string CountryCode { get; set; } = "NO";
}
