namespace TenantPlatform.Web.Services.Buildings;

public class BuildingDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static BuildingDeleteCheckResult Allowed() =>
        new()
        {
            CanDelete = true
        };

    public static BuildingDeleteCheckResult NotAllowed(string reason) =>
        new()
        {
            CanDelete = false,
            Reason = reason
        };
}
