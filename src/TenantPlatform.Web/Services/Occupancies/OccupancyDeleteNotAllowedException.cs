namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyDeleteNotAllowedException : Exception
{
    public OccupancyDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

