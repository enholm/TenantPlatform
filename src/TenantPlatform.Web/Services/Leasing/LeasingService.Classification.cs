using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Dimensions;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService
{


    public Task<LeasingClassificationCatalog> ClassificationCatalogAsync(Guid account, CancellationToken ct = default) => classifications.ClassificationCatalogAsync(account, ct);
    public Task SaveDimensionRuleAsync(Guid account, LeasingDimensionRule input, CancellationToken ct = default) => classifications.SaveDimensionRuleAsync(account, input, ct);
    public static List<string> MissingDimensions(LeasingItem item, LeasingClassificationCatalog catalog) => LeasingClassificationService.MissingDimensions(item, catalog);

}
