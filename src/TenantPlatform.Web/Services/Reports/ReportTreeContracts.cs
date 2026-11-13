using TenantPlatform.Core.Reports;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportTreeNodeType
{
    Account,
    Agreement,
    AgreementLine,
    LeasingFramework,
    LeasingAcquisition,
    LeasingItem,
    LeasingEquipment,
    Synthetic
}

public enum ReportTreeSyntheticNode { None, LeasingWithoutFramework }

public sealed record ReportTreeNodeId(
    ReportModule Module,
    ReportTreeNodeType Type,
    Guid? EntityId = null,
    ReportTreeSyntheticNode Synthetic = ReportTreeSyntheticNode.None)
{
    public string Key => Type == ReportTreeNodeType.Synthetic
        ? $"{(int)Module}:s:{(int)Synthetic}"
        : $"{(int)Module}:{(int)Type}:{EntityId:N}";

    public void Validate()
    {
        var isSynthetic = Type == ReportTreeNodeType.Synthetic;
        var validSynthetic = isSynthetic && Synthetic != ReportTreeSyntheticNode.None && EntityId is null;
        var validEntity = !isSynthetic && Synthetic == ReportTreeSyntheticNode.None &&
            EntityId.HasValue && EntityId.Value != Guid.Empty;
        if (!Enum.IsDefined(Module) || !Enum.IsDefined(Type) || !Enum.IsDefined(Synthetic) ||
            !(validSynthetic || validEntity))
            throw new ReportTreeException("ReportTreeNodeInvalid");
    }
}

public sealed record ReportTreePathNode(ReportTreeNodeId Id, string Name);

public sealed record ReportTreeNode(
    ReportTreeNodeId Id,
    string Name,
    string? Context,
    bool HasChildren,
    ReportTreeNodeId? ParentId);

public sealed record ReportTreeNodePage(
    IReadOnlyList<ReportTreeNode> Items,
    int Offset,
    int TotalCount,
    int? NextOffset);

public sealed record ReportTreeSearchHit(
    ReportTreeNodeId Id,
    ReportTreeNodeType Type,
    string Name,
    string? Context,
    IReadOnlyList<ReportTreePathNode> Ancestors);

public sealed record ReportTreeDetailField(string LabelResourceKey, string? Value = null,
    string? ValueResourceKey = null);

public sealed record ReportTreeNodeDetails(
    ReportTreeNode Node,
    IReadOnlyList<ReportTreePathNode> Ancestors,
    IReadOnlyList<ReportTreeDetailField> Fields,
    string? NavigationTarget);

public sealed class ReportTreeException(string resourceKey) : Exception(resourceKey);

public interface IReportTreeService
{
    Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition,
        CancellationToken ct = default);
    Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
        ReportTreeNodeId parentId, int offset = 0, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId, ReportCardDefinition definition,
        string query, int limit = 25, CancellationToken ct = default);
    Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
        ReportTreeNodeId nodeId, CancellationToken ct = default);
}
