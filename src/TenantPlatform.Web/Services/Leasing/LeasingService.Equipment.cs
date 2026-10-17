using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<List<LeasingOption>> EquipmentBuildingsAsync(Guid account, CancellationToken ct = default) => equipment.EquipmentBuildingsAsync(account, ct);
    public Task<EquipmentPage> ListEquipmentAsync(Guid account, string? search = null, LeasingEquipmentStatus? status = null, Guid? acquisition = null, int page = 1, CancellationToken ct = default) => equipment.ListEquipmentAsync(account, search, status, acquisition, page, ct);
    public Task SetCountableAsync(Guid account, Guid acquisition, Guid item, bool countable, string reason, CancellationToken ct = default) => equipment.SetCountableAsync(account, acquisition, item, countable, reason, ct);
    public Task<List<Guid>> SaveEquipmentAsync(Guid account, Guid acquisition, List<LeasingEquipment> inputs, bool duplicateSerialConfirmed, string reason, Guid request, CancellationToken ct = default) => equipment.SaveEquipmentAsync(account, acquisition, inputs, duplicateSerialConfirmed, reason, request, ct);
    public Task ReverseLifecycleEventAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)=>lifecycle.ReverseLifecycleEventAsync(account,id,revision,reason,request,ct);
}
