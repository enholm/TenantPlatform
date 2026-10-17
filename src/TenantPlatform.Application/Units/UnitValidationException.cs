namespace TenantPlatform.Application.Units;

public class UnitValidationException : Exception
{
    public UnitValidationException(string message)
        : base(message)
    {
    }
}

