namespace TenantPlatform.Web.Services.Units;

public class UnitDeleteNotAllowedException : Exception
{
    public UnitDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

