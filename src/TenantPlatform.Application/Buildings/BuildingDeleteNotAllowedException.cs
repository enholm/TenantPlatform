namespace TenantPlatform.Application.Buildings;

public class BuildingDeleteNotAllowedException : Exception
{
    public BuildingDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}
