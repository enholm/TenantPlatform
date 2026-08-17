namespace TenantPlatform.Web.Services.Units;

public class UnitValidationException : Exception
{
    public UnitValidationException(string message)
        : base(message)
    {
    }
}

