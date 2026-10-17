using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingReportData(IDbContextFactory<TenantPlatformDbContext> factory):ILeasingReportData
{
    public async Task<LeasingReportDataSet> ReadAsync(LeasingReportScope scope,LeasingReportKind kind,LeasingReportFilter filter,int rootLimit,CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var account=scope.AccountId;var user=scope.UserId;db.ChangeTracker.QueryTrackingBehavior=QueryTrackingBehavior.NoTracking;
        var query=db.LeasingAcquisitions.Where(x=>x.AccountId==account&&(scope.Elevated||x.OwnerUserId==user||db.LeasingFrameworks.Any(f=>f.AccountId==account&&f.Id==x.FrameworkId&&f.OwnerUserId==user)));
        if(filter.Currency!="")query=query.Where(x=>x.Currency==filter.Currency);
        if(filter.Search!="")query=query.Where(x=>x.Name.ToLower().Contains(filter.Search.ToLower())||x.Reference.ToLower().Contains(filter.Search.ToLower()));
        if(kind is LeasingReportKind.Dimensions or LeasingReportKind.Equipment)query=query.Include(x=>x.Items).ThenInclude(x=>x.DimensionSelections).Include(x=>x.Items).ThenInclude(x=>x.AllocationDimensions).Include(x=>x.Items).ThenInclude(x=>x.AllocationRows).AsSplitQuery();
        var acquisitions=await query.OrderBy(x=>x.Id).Take(rootLimit).ToListAsync(ct);var ids=acquisitions.Select(x=>x.Id).ToArray();
        var frames=db.LeasingFrameworks.Where(x=>x.AccountId==account&&(scope.Elevated||x.OwnerUserId==user));
        if(filter.AcquisitionsOnly)frames=frames.Where(x=>false);if(filter.FinanceId.HasValue)frames=frames.Where(x=>x.FinanceOrganizationId==filter.FinanceId);if(filter.OwnerId.HasValue)frames=frames.Where(x=>x.OwnerUserId==filter.OwnerId);if(filter.Currency!="")frames=frames.Where(x=>x.Currency==filter.Currency);if(filter.Search!="")frames=frames.Where(x=>x.Name.ToLower().Contains(filter.Search.ToLower())||x.Number.ToLower().Contains(filter.Search.ToLower()));
        var frameRows=kind is LeasingReportKind.Agreements or LeasingReportKind.Capacity?await frames.OrderBy(x=>x.Id).Take(rootLimit).ToListAsync(ct):[];var frameIds=frameRows.Select(x=>x.Id).ToArray();
        var money=kind is LeasingReportKind.Payments or LeasingReportKind.InvoiceControl;
        var installments=money?await db.LeasingInstallments.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct):[];var installmentIds=installments.Select(x=>x.Id).ToArray();
        var allocations=money?await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&installmentIds.Contains(x.InstallmentId)&&!x.Reversed).ToListAsync(ct):[];var invoiceIds=allocations.Select(x=>x.InvoiceId).ToArray();
        var unallocated=kind==LeasingReportKind.InvoiceControl?await db.LeasingInvoices.Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&x.Status==LeasingInvoiceStatus.Approved&&(scope.Elevated||x.UploadedByUserId==user)).ToListAsync(ct):[];var unallocatedIds=unallocated.Select(x=>x.Id).ToArray();
        return new()
        {
            Acquisitions=acquisitions,Frameworks=frameRows,
            CapacityAcquisitions=await db.LeasingAcquisitions.Where(x=>x.AccountId==account&&x.FrameworkId.HasValue&&frameIds.Contains(x.FrameworkId.Value)&&x.Status==LeasingAcquisitionStatus.Registered).ToListAsync(ct),
            CapacityOrders=await db.LeasingOrders.Where(x=>x.AccountId==account&&frameIds.Contains(x.FrameworkId)&&x.Status==LeasingOrderStatus.Approved).Include(x=>x.Lines).ToListAsync(ct),
            Lifecycles=await db.LeasingLifecycles.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToDictionaryAsync(x=>x.AcquisitionId,ct),
            Organizations=await db.Organizations.Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct),
            People=await db.UserAccounts.Where(x=>x.AccountId==account).Select(x=>new{x.UserId,Name=x.User.FirstName+" "+x.User.LastName}).ToDictionaryAsync(x=>x.UserId,x=>x.Name,ct),
            Financing=await db.LeasingFinancingRevisions.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct),
            RequiredDimensions=kind==LeasingReportKind.Dimensions?await db.LeasingDimensionRules.Where(x=>x.AccountId==account&&x.IsEnabled&&x.IsRequired).Select(x=>x.DimensionId).ToListAsync(ct):[],
            Plans=money?await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)&&x.Status==LeasingPlanStatus.Active).Include(x=>x.Terms).ToListAsync(ct):[],
            Installments=installments,Allocations=allocations,
            Invoices=money?await db.LeasingInvoices.Where(x=>x.AccountId==account&&invoiceIds.Contains(x.Id)&&x.Status==LeasingInvoiceStatus.Approved).ToListAsync(ct):[],
            UnallocatedInvoices=unallocated,UnallocatedLinks=kind==LeasingReportKind.InvoiceControl?await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&unallocatedIds.Contains(x.InvoiceId)&&!x.Reversed).ToListAsync(ct):[],
            Equipment=kind==LeasingReportKind.Equipment?await db.LeasingEquipment.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct):[],
            Events=kind==LeasingReportKind.Equipment?await db.LeasingLifecycleEvents.Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct):[],
            Buildings=kind==LeasingReportKind.Equipment?await db.Buildings.Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct):[],
            TimeZoneId=await db.AgreementReminderSettings.Where(x=>x.AccountId==account).Select(x=>x.TimeZoneId).SingleOrDefaultAsync(ct)??"Europe/Oslo"
        };
    }
}
