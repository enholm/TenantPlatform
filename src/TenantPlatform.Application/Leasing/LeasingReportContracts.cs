using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public enum LeasingReportKind { Agreements=1,Capacity=2,Dimensions=3,Payments=4,InvoiceControl=5,Equipment=6 }
public enum LeasingReportDate { Purchase=1,End=2,Due=3,Invoice=4,Event=5,Notice=6,Return=7 }
public sealed class LeasingReportFilter
{
    public bool AcquisitionsOnly {get;set;}
    public DateOnly? From {get;set;}public DateOnly? To {get;set;}public LeasingReportDate DateField {get;set;}=LeasingReportDate.Purchase;
    public Guid? FinanceId {get;set;}public Guid? OwnerId {get;set;}public string Currency {get;set;}="";public string Search {get;set;}="";public string Status {get;set;}="";public Guid? DimensionValueId {get;set;}
}
public sealed class LeasingReportRow
{
    public Guid? FinanceId {get;set;}public Guid? OwnerId {get;set;}
    public Guid Id {get;set;}public Guid ParentId {get;set;}public string Link {get;set;}="";public string Name {get;set;}="";public string Reference {get;set;}="";public string Finance {get;set;}="";public string Owner {get;set;}="";public string Currency {get;set;}="";public string Status {get;set;}="";public string Basis {get;set;}="";
    public DateOnly? PurchaseDate {get;set;}public DateOnly? EndDate {get;set;}public DateOnly? NoticeDate {get;set;}public DateOnly? ReturnDate {get;set;}public DateOnly? From {get;set;}public DateOnly? To {get;set;}public DateOnly? DueDate {get;set;}public DateOnly? InvoiceDate {get;set;}public DateOnly? EventDate {get;set;}
    public decimal? Net {get;set;}public decimal? Vat {get;set;}public decimal? Gross {get;set;}public decimal? Limit {get;set;}public decimal? Used {get;set;}public decimal? Reserved {get;set;}public decimal? Available {get;set;}public decimal? Expected {get;set;}public decimal? Invoiced {get;set;}public decimal? Difference {get;set;}public decimal? Unallocated {get;set;}
    public DateOnly? OriginalEndDate {get;set;}public DateOnly? ClosedDate {get;set;}public string ClosureKind {get;set;}="";public decimal? Quantity {get;set;}public decimal? BuyoutAmount {get;set;}public string UnitReference {get;set;}="";
    public int? Version {get;set;}public bool NeedsReview {get;set;}public string Dimensions {get;set;}="";public string Serial {get;set;}="";public string Location {get;set;}="";public string Notes {get;set;}="";
}
public sealed record LeasingReportTotal(string Currency,decimal Net,decimal Vat,decimal Gross,decimal Limit,decimal Used,decimal Reserved,decimal Available,decimal Expected,decimal Invoiced,decimal Unallocated,int Incomplete,string Basis="");
public sealed record LeasingReportResult(LeasingReportKind Kind,List<LeasingReportRow> Rows,int Count,int Page,List<LeasingReportTotal> Totals,DateTimeOffset GeneratedUtc,bool CanExport);
public sealed record LeasingDashboardResult(Dictionary<LeasingReportKind,LeasingReportResult> Reports,int Active,int Expiring,int Notices,int Returns,int OpenTasks,int MissingPlans,int Variances);

public sealed record LeasingReportScope(Guid AccountId,Guid UserId,bool Elevated,bool CanExport);
public sealed class LeasingReportDataSet
{
    public List<LeasingAcquisition> Acquisitions {get;init;}=[];
    public List<LeasingFramework> Frameworks {get;init;}=[];
    public List<LeasingAcquisition> CapacityAcquisitions {get;init;}=[];
    public List<LeasingOrder> CapacityOrders {get;init;}=[];
    public Dictionary<Guid,LeasingLifecycle> Lifecycles {get;init;}=[];
    public Dictionary<Guid,string> Organizations {get;init;}=[];
    public Dictionary<Guid,string> People {get;init;}=[];
    public Dictionary<Guid,string> Buildings {get;init;}=[];
    public List<LeasingFinancingRevision> Financing {get;init;}=[];
    public List<Guid> RequiredDimensions {get;init;}=[];
    public List<LeasingPaymentPlan> Plans {get;init;}=[];
    public List<LeasingInstallment> Installments {get;init;}=[];
    public List<LeasingPaymentAllocation> Allocations {get;init;}=[];
    public List<LeasingInvoice> Invoices {get;init;}=[];
    public List<LeasingInvoice> UnallocatedInvoices {get;init;}=[];
    public List<LeasingPaymentAllocation> UnallocatedLinks {get;init;}=[];
    public List<LeasingEquipment> Equipment {get;init;}=[];
    public List<LeasingLifecycleEvent> Events {get;init;}=[];
    public string TimeZoneId {get;init;}="Europe/Oslo";
}
public interface ILeasingReportData
{
    Task<LeasingReportDataSet> ReadAsync(LeasingReportScope scope,LeasingReportKind kind,LeasingReportFilter filter,int rootLimit,CancellationToken ct);
}
