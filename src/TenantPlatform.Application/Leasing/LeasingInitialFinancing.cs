using System.Text.Json;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public static class LeasingInitialFinancing
{
    public static LeasingFinancingRevision? Create(LeasingAcquisition acquisition, Guid actor, DateTimeOffset now, bool exists) => exists ? null : new()
    {
        Id = Guid.NewGuid(), AccountId = acquisition.AccountId, AcquisitionId = acquisition.Id,
        SnapshotJson = JsonSerializer.Serialize(LeasingFinancingSnapshot.From(acquisition)), ActorUserId = actor,
        RecordedUtc = now, Reason = "PaymentInitialUnknownDate"
    };
}
