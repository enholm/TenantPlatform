using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<LifecycleDetails> GetLifecycleAsync(Guid account,Guid id,CancellationToken ct=default)=>lifecycle.GetLifecycleAsync(account,id,ct);
    public Task SaveLifecycleTermsAsync(Guid account,Guid id,LeasingLifecycle input,string reason,Guid request,CancellationToken ct=default)=>lifecycle.SaveLifecycleTermsAsync(account,id,input,reason,request,ct);
    public Task<Guid> ProposeLifecycleEventAsync(Guid account,Guid id,LifecycleEventInput input,Guid request,CancellationToken ct=default)=>lifecycle.ProposeLifecycleEventAsync(account,id,input,request,ct);
    public Task DecideLifecycleEventAsync(Guid account,Guid id,Guid revision,bool approve,string reason,Guid request,CancellationToken ct=default)=>lifecycle.DecideLifecycleEventAsync(account,id,revision,approve,reason,request,ct);
    public Task ReviewLifecycleDocumentsAsync(Guid account,Guid id,Guid revision,bool plan,bool complete,string reason,Guid request,CancellationToken ct=default)=>lifecycle.ReviewLifecycleDocumentsAsync(account,id,revision,plan,complete,reason,request,ct);
}
