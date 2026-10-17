using TenantPlatform.Application.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
// Compatibility for presentation-only preview and pure file-format smoke checks.
public static class PaymentPlanImport
{
    public const int MaxRows = PaymentPlanImportRules.MaxRows;
    public static PaymentImportGrid Read(Stream stream, string fileName, string? sheetName = null) => new TenantPlatform.Infrastructure.Leasing.PaymentPlanSourceReader().Read(stream, fileName, sheetName);
    public static PaymentImportPreview Preview(PaymentImportGrid grid, PaymentColumnMap map, string currency) => PaymentPlanImportRules.Preview(grid, map, currency);
}
