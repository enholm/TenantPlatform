using TenantPlatform.Application.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<LeasingReportResult> GetLeasingReportAsync(Guid account,LeasingReportKind kind,LeasingReportFilter filter,int page=1,bool export=false,CancellationToken ct=default)=>reports.GetLeasingReportAsync(account,kind,filter,page,export,ct);
    public Task<LeasingDashboardResult> GetLeasingDashboardAsync(Guid account,DateOnly from,DateOnly to,CancellationToken ct=default)=>reports.GetLeasingDashboardAsync(account,from,to,ct);
}
