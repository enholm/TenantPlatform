using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LifecycleEventInput
{
    public LeasingLifecycleKind Kind {get;set;}=LeasingLifecycleKind.Return;
    public DateOnly Date {get;set;}=DateOnly.FromDateTime(DateTime.Today);
    public Guid? ItemId {get;set;}
    public Guid[] EquipmentIds {get;set;}=[];
    public decimal Quantity {get;set;}=1;
    public Guid? ReplacementEquipmentId {get;set;}
    public DateOnly? PreviousEndDate {get;set;}
    public DateOnly? NewEndDate {get;set;}
    public decimal? BuyoutAmount {get;set;}
    public bool PaymentsChanged {get;set;}
    public string TermsReference {get;set;}="";
    public string Reason {get;set;}="";
    public Guid? CounterpartyId {get;set;}
    public string Reference {get;set;}="";
    public Guid? SourceDocumentId {get;set;}
    public bool UnresolvedConfirmed {get;set;}
}
public sealed record LifecycleDetails(LeasingAcquisition Acquisition,LeasingLifecycle Lifecycle,List<LeasingEquipment> Equipment,List<LeasingLifecycleEvent> Events,List<LeasingDisposition> Dispositions,List<LeasingDocument> Documents,List<LeasingPaymentEvent> Changes,List<string> ClosureIssues,bool CanManage,bool CanApprove,bool CanMaintainEquipment);

// One concrete acquisition graph; event addressing is resolved under the account lock.
public sealed class LeasingLifecycleState
{
    public LeasingAcquisition? Acquisition { get; init; }
    public LeasingFramework? Framework { get; init; }
    public LeasingLifecycle? Lifecycle { get; set; }
    public List<LeasingEquipment> Equipment { get; init; } = [];
    public List<LeasingLifecycleEvent> Events { get; init; } = [];
    public List<LeasingDisposition> Dispositions { get; init; } = [];
    public List<LeasingDocument> Documents { get; init; } = [];
    public List<LeasingPaymentEvent> Changes { get; init; } = [];
    public List<LeasingFollowup> Followups { get; init; } = [];
    public List<LeasingPaymentPlan> Plans { get; init; } = [];
    public List<LeasingInstallment> Installments { get; init; } = [];
    public List<LeasingPaymentAllocation> Allocations { get; init; } = [];
    public List<LeasingInvoice> ApprovedInvoices { get; init; } = [];
    public List<LeasingFinancingRevision> Financing { get; init; } = [];
    public LeasingPaymentEvent? PreviousRequest { get; init; }
    public string TimeZoneId { get; init; } = "Europe/Oslo";
}
public sealed record LeasingLifecyclePartyInvoices(List<LeasingInvoice> Invoices, List<LeasingPaymentAllocation> Allocations);
public sealed record LeasingLifecycleReplacement(LeasingEquipment? Equipment, LeasingAcquisition? Acquisition, bool ReferencedByEquipment, List<string> ApprovedReplacementInputs);
public interface ILeasingLifecycleData
{
    Task<ILeasingLifecycleWork> OpenAsync(Guid account, Guid target, bool eventTarget, Guid? request, bool write, CancellationToken ct);
}
public interface ILeasingLifecycleWork : IAsyncDisposable
{
    LeasingLifecycleState State { get; }
    Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct);
    Task<bool> OrganizationExistsAsync(Guid organization, CancellationToken ct);
    Task<LeasingLifecycleReplacement> ReadReplacementAsync(Guid equipment, CancellationToken ct);
    Task<LeasingLifecyclePartyInvoices> ReadPartyInvoicesAsync(Guid[] financeOrganizations, string currency, CancellationToken ct);
    Task CommitAsync(LeasingPaymentEvent history, CancellationToken ct);
}
