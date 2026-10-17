namespace TenantPlatform.Application.Occupancies;

public class OccupancyValidationException : Exception
{
    public OccupancyValidationException(string message)
        : base(message)
    {
    }
}

