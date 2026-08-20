namespace TenantPlatform.Web.Services.ServiceCatalog;

public class ServiceRequestFieldInputModel
{
    public Guid FieldId { get; set; }

    public string? Value { get; set; }

    public bool BoolValue { get; set; }

    public List<string> SelectedValues { get; set; } = [];
}

