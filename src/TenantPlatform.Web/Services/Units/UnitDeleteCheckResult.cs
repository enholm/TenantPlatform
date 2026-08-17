namespace TenantPlatform.Web.Services.Units;

public class UnitDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static UnitDeleteCheckResult Allowed() =>
        new()
        {
            CanDelete = true
        };

    public static UnitDeleteCheckResult NotAllowed(
        string reason) =>
        new()
        {
            CanDelete = false,
            Reason = reason
        };
}

