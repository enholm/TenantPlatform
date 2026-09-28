using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
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
public sealed partial class LeasingService
{
    private async Task<bool> ReportReader(CancellationToken ct)=>await authorization.CanReadLeasingReportsAsync(ct)||await authorization.CanExportLeasingReportsAsync(ct);
    public async Task<LeasingReportResult> GetLeasingReportAsync(Guid account,LeasingReportKind kind,LeasingReportFilter filter,int page=1,bool export=false,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!Enum.IsDefined(kind)||!Enum.IsDefined(filter.DateField)||filter.From>filter.To||filter.Search.Length>200)throw new LeasingValidationException("LifeInvalidReport");
        var canExport=await authorization.CanExportLeasingReportsAsync(ct);if(export&&!canExport)throw new UnauthorizedAccessException();
        // Account report roles see account-wide data; ordinary responsible users see their own objects only.
        var elevated=admin||await authorization.CanReadLeasingReportsAsync(ct)||canExport;
        var query=Acquisitions(db,account,user,elevated).AsNoTracking();if(filter.Currency!="")query=query.Where(x=>x.Currency==filter.Currency);
        if(filter.Search!=""){var search=filter.Search.ToLower();query=query.Where(x=>x.Name.ToLower().Contains(search)||x.Reference.ToLower().Contains(search));}
        var acquisitions=await query.OrderBy(x=>x.Id).Take(10001).ToListAsync(ct);if(acquisitions.Count>10000)throw new LeasingValidationException("LifeNarrowReport");
        var ids=acquisitions.Select(x=>x.Id).ToArray();var states=await db.LeasingLifecycles.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToDictionaryAsync(x=>x.AcquisitionId,ct);
        var parties=await db.Organizations.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);var people=await db.UserAccounts.AsNoTracking().Where(x=>x.AccountId==account).Select(x=>new{x.UserId,Name=x.User.FirstName+" "+x.User.LastName}).ToDictionaryAsync(x=>x.UserId,x=>x.Name,ct);
        var financing=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct);var today=await OrderToday(db,account,ct);
        var rows=new List<LeasingReportRow>();
        LeasingReportRow Base(LeasingAcquisition a){var l=states.GetValueOrDefault(a.Id)??LeasingLifecycleCalculator.Initial(a);var revision=FinancingAt(financing.Where(x=>x.AcquisitionId==a.Id),today);var party=kind==LeasingReportKind.Dimensions||revision==null?a.FinanceOrganizationId:JsonSerializer.Deserialize<LeasingFinancingSnapshot>(revision.SnapshotJson)!.FinanceOrganizationId;return new(){Id=a.Id,ParentId=a.Id,Link=$"/leasing/acquisitions/{a.Id}/lifecycle",Name=a.Name,Reference=a.Reference,FinanceId=party,OwnerId=l.OwnerUserId??a.OwnerUserId,Finance=parties.GetValueOrDefault(party,""),Owner=people.GetValueOrDefault(l.OwnerUserId??a.OwnerUserId,""),Currency=a.Currency,PurchaseDate=a.PurchaseDate,EndDate=l.AgreedEndDate,OriginalEndDate=l.OriginalEndDate,ClosedDate=l.ClosedDate,ClosureKind=l.ClosureKind.HasValue?$"LifeKind{l.ClosureKind}":"",NoticeDate=l.NoticeDate,ReturnDate=l.ReturnDueDate,Status=a.Status==LeasingAcquisitionStatus.Registered?$"LifeStatus{l.Status}":$"LeasingStatus{a.Status}"};}
        if(kind is LeasingReportKind.Agreements or LeasingReportKind.Capacity)
        {
            var frames=Frameworks(db,account,user,elevated).AsNoTracking();if(filter.AcquisitionsOnly)frames=frames.Where(x=>false);if(filter.FinanceId.HasValue)frames=frames.Where(x=>x.FinanceOrganizationId==filter.FinanceId);if(filter.OwnerId.HasValue)frames=frames.Where(x=>x.OwnerUserId==filter.OwnerId);if(filter.Currency!="")frames=frames.Where(x=>x.Currency==filter.Currency);if(filter.Search!="")frames=frames.Where(x=>x.Name.ToLower().Contains(filter.Search.ToLower())||x.Number.ToLower().Contains(filter.Search.ToLower()));
            var frameRows=await frames.OrderBy(x=>x.Id).Take(10001).ToListAsync(ct);if(frameRows.Count>10000)throw new LeasingValidationException("LifeNarrowReport");
            foreach(var f in frameRows){var cap=await Capacity(db,f,ct);rows.Add(new(){Id=f.Id,ParentId=f.Id,Link=$"/leasing/frameworks/{f.Id}",Name=f.Name,Reference=f.Number,FinanceId=f.FinanceOrganizationId,OwnerId=f.OwnerUserId,Finance=parties.GetValueOrDefault(f.FinanceOrganizationId,""),Owner=people.GetValueOrDefault(f.OwnerUserId,""),Currency=f.Currency,From=f.AcquisitionFrom,To=f.AcquisitionTo,EndDate=f.AcquisitionTo,Status=$"LeasingStatus{f.Status}",Basis=f.IncludesVat?"LeasingGross":"LeasingNet",Limit=kind==LeasingReportKind.Capacity?cap.Limit:null,Used=kind==LeasingReportKind.Capacity?cap.Used:null,Reserved=kind==LeasingReportKind.Capacity?cap.Reserved:null,Available=kind==LeasingReportKind.Capacity?cap.Available:null});}
            if(kind==LeasingReportKind.Agreements)foreach(var a in acquisitions){var row=Base(a);row.Basis="LifePurchaseValue";row.Net=a.NetTotal;row.Vat=a.VatTotal;row.Gross=a.GrossTotal;rows.Add(row);}
        }
        else if(kind==LeasingReportKind.Dimensions)
        {
            var requiredDimensions=await db.LeasingDimensionRules.AsNoTracking().Where(x=>x.AccountId==account&&x.IsEnabled&&x.IsRequired).Select(x=>x.DimensionId).ToListAsync(ct);
            foreach(var a in acquisitions)
            {
                var items=await db.LeasingItems.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).Include(x=>x.DimensionSelections).Include(x=>x.AllocationDimensions).Include(x=>x.AllocationRows).ToListAsync(ct);
                foreach(var item in items)
                {
                    var allocation=LeasingAllocationCalculator.Calculate(item);var amounts=allocation.Complete?allocation.Rows:[new LeasingAllocatedAmount(null,null,null,LeasingCalculator.Line(item).Net,LeasingCalculator.Line(item).Vat)];
                    foreach(var amount in amounts)
                    {
                        var selections=item.DimensionSelections.Where(x=>x.AllocationRowId==null||x.AllocationRowId==amount.RowId).ToList();if(filter.DimensionValueId.HasValue&&!selections.Any(x=>x.ValueId==filter.DimensionValueId))continue;
                        var r=Base(a);r.Id=amount.RowId??item.Id;r.Reference=item.Description;r.Net=amount.Net;r.Vat=amount.Vat;r.Gross=amount.Gross;r.Basis="LifePurchaseValue";r.Dimensions=string.Join("; ",selections.Select(x=>$"{x.DimensionName}: {x.ValuePath}"));r.NeedsReview=!allocation.Complete||selections.Count==0||requiredDimensions.Any(d=>!selections.Any(s=>s.DimensionId==d))||item.AllocationDimensions.Any(d=>!selections.Any(s=>s.DimensionId==d.DimensionId));r.Notes=r.NeedsReview?"LifeUnclassified":"LifeHistoricalDimensionNames";rows.Add(r);
                    }
                }
                // Purchase credits/reversals have item links, but no reliable split-row allocation. Never invent a proportional distribution.
                if(a.CreditNetTotal+a.CreditVatTotal+a.ReversedNetTotal+a.ReversedVatTotal!=0){var r=Base(a);r.Reference="LifeUnallocatedPurchaseAdjustments";r.Net=-a.CreditNetTotal-a.ReversedNetTotal;r.Vat=-a.CreditVatTotal-a.ReversedVatTotal;r.Gross=r.Net+r.Vat;r.Basis="LifeUnallocatedPurchaseAdjustments";r.NeedsReview=true;r.Notes="LifeCreditDimensionMissing";if(!filter.DimensionValueId.HasValue)rows.Add(r);}
            }
        }
        else if(kind is LeasingReportKind.Payments or LeasingReportKind.InvoiceControl)
        {
            foreach(var a in acquisitions.Where(x=>x.Status==LeasingAcquisitionStatus.Registered))
            {
                var plans=await db.LeasingPaymentPlans.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active).Include(x=>x.Terms).ToListAsync(ct);
                if(plans.Count==0){var r=Base(a);r.NeedsReview=true;r.Notes="PaymentNoPlan";r.Status="PaymentNoPlan";rows.Add(r);continue;}
                var obligations=await db.LeasingInstallments.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToDictionaryAsync(x=>x.Id,ct);
                var obligationIds=obligations.Keys.ToArray();var allocations=await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&obligationIds.Contains(x.InstallmentId)&&!x.Reversed).ToListAsync(ct);
                var invoiceIds=allocations.Select(x=>x.InvoiceId).Distinct().ToArray();var docs=await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&invoiceIds.Contains(x.Id)&&x.Status==LeasingInvoiceStatus.Approved).ToDictionaryAsync(x=>x.Id,ct);
                foreach(var p in plans)foreach(var term in p.Terms)
                {
                    var links=allocations.Where(x=>x.InstallmentId==term.InstallmentId&&docs.ContainsKey(x.InvoiceId)).ToList();
                    if(filter.DateField==LeasingReportDate.Invoice)links=links.Where(x=>(!filter.From.HasValue||docs[x.InvoiceId].InvoiceDate>=filter.From)&&(!filter.To.HasValue||docs[x.InvoiceId].InvoiceDate<=filter.To)).ToList();
                    decimal net=0,vat=0;foreach(var link in links){var sign=docs[link.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1;net+=sign*link.Net;vat+=sign*link.Vat;}
                    var r=Base(a);var basis=TermFinancing(a,term,financing.Where(x=>x.AcquisitionId==a.Id).ToList());r.FinanceId=basis.FinanceOrganizationId;r.Finance=parties.GetValueOrDefault(basis.FinanceOrganizationId,"");r.Id=term.InstallmentId;r.Reference=term.Reference;r.From=term.PeriodFrom;r.To=term.PeriodTo;r.DueDate=term.DueDate;r.Version=p.Version;r.Expected=term.Gross;r.Net=term.Net;r.Vat=term.Vat;r.Gross=term.Gross;r.NeedsReview=term.NeedsReview||term.Gross==null||states.GetValueOrDefault(a.Id)?.PaymentPlanReviewRequired==true;r.Basis="LifePlannedPayments";r.Status=kind==LeasingReportKind.Payments?"PaymentPlanActive":$"PaymentBilling{LeasingPaymentCalculator.Status(term,obligations[term.InstallmentId],net,vat,links.Count>0)}";r.Link=$"/leasing/acquisitions/{a.Id}/payments";
                    if(kind==LeasingReportKind.InvoiceControl){r.InvoiceDate=links.Select(x=>docs[x.InvoiceId].InvoiceDate).Max();r.Invoiced=net+vat;r.Difference=r.Invoiced-r.Expected;r.Notes=string.Join("; ",links.Select(x=>$"{docs[x.InvoiceId].Number}: {x.Net}/{x.Vat}"));}rows.Add(r);
                }
            }
            if(kind==LeasingReportKind.InvoiceControl)
            {
                // Unallocated amounts belong to the document, not every possible acquisition. Emit each visible invoice once.
                var docs=await RentalInvoices(db,account,user,elevated).AsNoTracking().Where(x=>x.Status==LeasingInvoiceStatus.Approved).ToListAsync(ct);
                foreach(var invoice in docs){if(filter.Search!=""&&!invoice.Number.Contains(filter.Search,StringComparison.OrdinalIgnoreCase)||filter.Currency!=""&&invoice.Currency!=filter.Currency||filter.FinanceId.HasValue&&invoice.FinanceOrganizationId!=filter.FinanceId||filter.OwnerId.HasValue&&invoice.UploadedByUserId!=filter.OwnerId)continue;var links=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&x.InvoiceId==invoice.Id&&!x.Reversed).ToListAsync(ct);var rest=invoice.Net+invoice.Vat-links.Sum(x=>x.Net+x.Vat);if(rest==0)continue;var sign=invoice.Kind==LeasingInvoiceKind.CreditNote?-1:1;rows.Add(new(){Id=invoice.Id,Link=$"/leasing/rental-invoices/{invoice.Id}",Name=invoice.Number,Reference=invoice.Number,FinanceId=invoice.FinanceOrganizationId,OwnerId=invoice.UploadedByUserId,Finance=parties.GetValueOrDefault(invoice.FinanceOrganizationId??Guid.Empty,""),Currency=invoice.Currency,InvoiceDate=invoice.InvoiceDate,Unallocated=sign*rest,Status="LifeUnallocated",Basis="RentalUnallocated",Notes="LifeNoAcquisitionAllocation"});}
            }
        }
        else
        {
            var buildings=await db.Buildings.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
            foreach(var a in acquisitions)
            {
                var units=await db.LeasingEquipment.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct);
                foreach(var unit in units){var r=Base(a);r.Id=unit.Id;r.Reference=unit.InternalId??unit.Description;r.Quantity=1;r.UnitReference=unit.Id.ToString();r.Serial=unit.SerialNumber??"";r.Location=buildings.GetValueOrDefault(unit.BuildingId??Guid.Empty,"");r.OwnerId=unit.OwnerUserId;r.Owner=people.GetValueOrDefault(unit.OwnerUserId??Guid.Empty,"");r.Status=$"LifeEquipment{unit.Status}";r.EventDate=unit.StatusDate??unit.RegisteredDate;r.Notes=unit.Notes;rows.Add(r);}
                foreach(var item in await db.LeasingItems.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct)){var r=Base(a);r.Id=item.Id;r.Reference=item.Description;r.Quantity=item.Quantity;r.Notes=$"{item.Quantity} / {units.Count(x=>x.ItemId==item.Id)}";r.Basis="LifeQuantityTracked";rows.Add(r);}
                foreach(var e in await db.LeasingLifecycleEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct)){var r=Base(a);r.Id=e.Id;var input=JsonSerializer.Deserialize<LifecycleEventInput>(e.InputJson)!;r.Quantity=input.ItemId.HasValue?input.Quantity:null;r.BuyoutAmount=input.BuyoutAmount;r.UnitReference=string.Join(", ",input.EquipmentIds.Select(id=>units.FirstOrDefault(u=>u.Id==id)?.InternalId??id.ToString()));r.Reference=e.Reference;r.EventDate=e.Date;r.Status=$"LifeDecision{e.Decision}";r.Basis=$"LifeKind{e.Kind}";r.Notes=e.Reason;rows.Add(r);}
            }
        }
        DateOnly? Date(LeasingReportRow r)=>filter.DateField switch{LeasingReportDate.Purchase=>r.PurchaseDate,LeasingReportDate.End=>r.EndDate,LeasingReportDate.Due=>r.DueDate,LeasingReportDate.Invoice=>r.InvoiceDate,LeasingReportDate.Event=>r.EventDate,LeasingReportDate.Notice=>r.NoticeDate,_=>r.ReturnDate};
        rows=rows.Where(r=>(!filter.FinanceId.HasValue||r.FinanceId==filter.FinanceId)&&(!filter.OwnerId.HasValue||r.OwnerId==filter.OwnerId)&&(filter.Status==""||r.Status==filter.Status||filter.Status=="LifeActive"&&(r.Status=="LifeStatusActive"||r.Status=="LifeStatusExtended"||r.Status=="LifeStatusClosing"))&&(!filter.From.HasValue||Date(r)>=filter.From)&&(!filter.To.HasValue||Date(r)<=filter.To)).OrderBy(r=>Date(r)).ThenBy(r=>r.Name).ThenBy(r=>r.Id).ToList();
        if(rows.Count>100000)throw new LeasingValidationException("LifeNarrowReport");
        var totals=rows.GroupBy(x=>new{x.Currency,Basis=kind==LeasingReportKind.Capacity?x.Basis:""}).Select(g=>new LeasingReportTotal(g.Key.Currency,g.Sum(x=>x.Net??0),g.Sum(x=>x.Vat??0),g.Sum(x=>x.Gross??0),g.Sum(x=>x.Limit??0),g.Sum(x=>x.Used??0),g.Sum(x=>x.Reserved??0),g.Sum(x=>x.Available??0),g.Sum(x=>x.Expected??0),g.Sum(x=>x.Invoiced??0),g.Sum(x=>x.Unallocated??0),g.Count(x=>x.NeedsReview),g.Key.Basis)).ToList();var count=rows.Count;page=Math.Clamp(page,1,Math.Max(1,(count+49)/50));
        return new(kind,export?rows:rows.Skip((page-1)*50).Take(50).ToList(),count,page,totals,clock.GetUtcNow(),canExport);
    }
    public async Task<LeasingDashboardResult> GetLeasingDashboardAsync(Guid account,DateOnly from,DateOnly to,CancellationToken ct=default)
    {
        var result=new Dictionary<LeasingReportKind,LeasingReportResult>();foreach(var kind in new[]{LeasingReportKind.Capacity,LeasingReportKind.Payments,LeasingReportKind.InvoiceControl})result[kind]=await GetLeasingReportAsync(account,kind,new(){From=kind==LeasingReportKind.Payments?from:null,To=kind==LeasingReportKind.Payments?to:null,DateField=LeasingReportDate.Due},ct:ct);
        var active=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){Status="LifeActive"},ct:ct);var extended=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){Status="LifeStatusExtended"},ct:ct);
        var expiring=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.End,From=from,To=to,AcquisitionsOnly=true,Status="LifeActive"},ct:ct);var notice=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.Notice,From=from,To=to,Status="LifeActive"},ct:ct);var returns=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.Return,From=from,To=to,Status="LifeActive"},ct:ct);
        var tasks=await ListLeasingFollowupsAsync(account,status:LeasingFollowupStatus.Open,to:to,ct:ct);var missing=await GetLeasingReportAsync(account,LeasingReportKind.Payments,new(){Status="PaymentNoPlan"},ct:ct);var variance=await GetLeasingReportAsync(account,LeasingReportKind.InvoiceControl,new(){Status="PaymentBillingVariance"},ct:ct);
        return new(result,active.Count,expiring.Count,notice.Count,returns.Count,tasks.Count,missing.Count,variance.Count);
    }
}
