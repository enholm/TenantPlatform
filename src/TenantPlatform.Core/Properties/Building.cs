namespace TenantPlatform.Core.Properties;

public class Building
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string CountryCode { get; set; } = "NO";

    public bool IsActive { get; set; } = true;
}
