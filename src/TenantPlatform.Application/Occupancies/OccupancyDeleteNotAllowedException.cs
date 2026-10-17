namespace TenantPlatform.Application.Occupancies;

public class OccupancyDeleteNotAllowedException : Exception
{
    public OccupancyDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

