namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyValidationException : Exception
{
    public OccupancyValidationException(string message)
        : base(message)
    {
    }
}

