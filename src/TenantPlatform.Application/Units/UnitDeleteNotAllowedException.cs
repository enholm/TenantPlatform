namespace TenantPlatform.Application.Units;

public class UnitDeleteNotAllowedException : Exception
{
    public UnitDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

