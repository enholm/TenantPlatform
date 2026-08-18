namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static OccupancyDeleteCheckResult Allowed() =>
        new()
        {
            CanDelete = true
        };

    public static OccupancyDeleteCheckResult NotAllowed(string reason) =>
        new()
        {
            CanDelete = false,
            Reason = reason
        };
}

