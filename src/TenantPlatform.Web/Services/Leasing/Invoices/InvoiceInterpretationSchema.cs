using System.Text.Json;
namespace TenantPlatform.Web.Services.Leasing.Invoices;

internal static class InvoiceInterpretationSchema
{
    public static readonly JsonElement Schema = Build();
    private static object Value(string type, bool nullable = false) => new Dictionary<string, object> { ["type"] = nullable ? new[] { type, "null" } : new[] { type } };
    private static object Object(Dictionary<string, object> properties) => new { type = "object", properties, required = properties.Keys.ToArray(), additionalProperties = false };
    private static JsonElement Build()
    {
        var line = new Dictionary<string, object>();
        foreach (var key in new[] { "SourceId", "Description", "ItemNumber", "Unit", "VatCategory", "TaxExemptionReason", "Reference" }) line[key] = Value("string");
        foreach (var key in new[] { "Quantity", "Price", "Net", "VatPercent" }) line[key] = Value("number", true);
        foreach (var key in new[] { "BaseQuantity", "Allowance", "Charge" }) line[key] = Value("number");
        foreach (var key in new[] { "DocumentAdjustment", "IsAllowance" }) line[key] = Value("boolean");
        line["Page"] = Value("integer", true); line["BoundingBox"] = Value("string", true);
        var tax = new Dictionary<string, object> { ["Category"] = Value("string"), ["Rate"] = Value("number"), ["Taxable"] = Value("number"), ["Amount"] = Value("number") };
        var root = new Dictionary<string, object> { ["Kind"] = new Dictionary<string, object> { ["type"] = "integer", ["enum"] = new[] { 1, 2 } } };
        foreach (var key in new[] { "SupplierName", "SupplierNumber", "Number", "Currency", "OriginalInvoiceReference", "OrderReference", "DeliveryReference" }) root[key] = Value("string");
        foreach (var key in new[] { "Date", "DueDate", "DeliveryDate" }) root[key] = Value("string", true);
        foreach (var key in new[] { "Net", "Vat", "Gross", "Prepaid", "Rounding", "Payable" }) root[key] = Value("number", true);
        root["Lines"] = new { type = "array", items = Object(line) }; root["Taxes"] = new { type = "array", items = Object(tax) };
        root["Uncertainties"] = new { type = "array", items = Value("string") };
        return JsonSerializer.SerializeToElement(Object(root));
    }
}
