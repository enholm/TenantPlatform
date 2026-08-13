namespace TenantPlatform.Web.Services.Buildings;

public class BuildingDeleteNotAllowedException : Exception
{
    public BuildingDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}
