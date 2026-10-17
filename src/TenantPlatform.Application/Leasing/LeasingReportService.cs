using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingReportService(IAuthorizationService authorization,ILeasingReportData data,LeasingFollowupService followups,TimeProvider clock)
{
    private async Task<LeasingReportScope> Actor(Guid account,CancellationToken ct)
    {
        var actor=await authorization.GetAccountActorScopeAsync(account,ct);var scope=LeasingReader.Scope(actor);var export=LeasingUserAccess.Has(actor,Permissions.Leasing.ExportReports);
        return new(account,actor.UserId,scope.Administrator||LeasingUserAccess.Has(actor,Permissions.Leasing.ReadReports)||export,export);
    }
    private async Task Revalidate(LeasingReportScope before,bool export,CancellationToken ct)
    {
        var actor=await Actor(before.AccountId,ct);if(actor.UserId!=before.UserId||before.Elevated&&!actor.Elevated||export&&!actor.CanExport)throw new UnauthorizedAccessException();
    }
    public async Task<LeasingReportResult> GetLeasingReportAsync(Guid account,LeasingReportKind kind,LeasingReportFilter filter,int page=1,bool export=false,CancellationToken ct=default)
    {
        filter=JsonSerializer.Deserialize<LeasingReportFilter>(JsonSerializer.Serialize(filter))!;
        var actor=await Actor(account,ct);if(!Enum.IsDefined(kind)||!Enum.IsDefined(filter.DateField)||filter.From>filter.To||filter.Search.Length>200)throw new LeasingValidationException("LifeInvalidReport");
        var canExport=actor.CanExport;if(export&&!canExport)throw new UnauthorizedAccessException();
        var graph=await data.ReadAsync(actor,kind,filter,10001,ct);await Revalidate(actor,export,ct);
        var acquisitions=graph.Acquisitions;if(acquisitions.Count>10000)throw new LeasingValidationException("LifeNarrowReport");
        var states=graph.Lifecycles;var parties=graph.Organizations;var people=graph.People;var financing=graph.Financing;var today=AgreementReminderSchedule.Today(clock.GetUtcNow(),graph.TimeZoneId);
        var rows=new List<LeasingReportRow>();
        LeasingReportRow Base(LeasingAcquisition a){var l=states.GetValueOrDefault(a.Id)??LeasingLifecycleCalculator.Initial(a);var revision=LeasingPaymentService.FinancingAt(financing.Where(x=>x.AcquisitionId==a.Id),today);var party=kind==LeasingReportKind.Dimensions||revision==null?a.FinanceOrganizationId:JsonSerializer.Deserialize<LeasingFinancingSnapshot>(revision.SnapshotJson)!.FinanceOrganizationId;return new(){Id=a.Id,ParentId=a.Id,Link=$"/leasing/acquisitions/{a.Id}/lifecycle",Name=a.Name,Reference=a.Reference,FinanceId=party,OwnerId=l.OwnerUserId??a.OwnerUserId,Finance=parties.GetValueOrDefault(party,""),Owner=people.GetValueOrDefault(l.OwnerUserId??a.OwnerUserId,""),Currency=a.Currency,PurchaseDate=a.PurchaseDate,EndDate=l.AgreedEndDate,OriginalEndDate=l.OriginalEndDate,ClosedDate=l.ClosedDate,ClosureKind=l.ClosureKind.HasValue?$"LifeKind{l.ClosureKind}":"",NoticeDate=l.NoticeDate,ReturnDate=l.ReturnDueDate,Status=a.Status==LeasingAcquisitionStatus.Registered?$"LifeStatus{l.Status}":$"LeasingStatus{a.Status}"};}
        if(kind is LeasingReportKind.Agreements or LeasingReportKind.Capacity)
        {
            var frameRows=graph.Frameworks;if(frameRows.Count>10000)throw new LeasingValidationException("LifeNarrowReport");
            foreach(var f in frameRows){var cap=new LeasingCapacity(f.Limit,LeasingCalculator.Used(graph.CapacityAcquisitions.Where(x=>x.FrameworkId==f.Id),f.IncludesVat),graph.CapacityOrders.Where(x=>x.FrameworkId==f.Id).Sum(x=>LeasingOrderCalculator.Reserved(x,f.IncludesVat)));rows.Add(new(){Id=f.Id,ParentId=f.Id,Link=$"/leasing/frameworks/{f.Id}",Name=f.Name,Reference=f.Number,FinanceId=f.FinanceOrganizationId,OwnerId=f.OwnerUserId,Finance=parties.GetValueOrDefault(f.FinanceOrganizationId,""),Owner=people.GetValueOrDefault(f.OwnerUserId,""),Currency=f.Currency,From=f.AcquisitionFrom,To=f.AcquisitionTo,EndDate=f.AcquisitionTo,Status=$"LeasingStatus{f.Status}",Basis=f.IncludesVat?"LeasingGross":"LeasingNet",Limit=kind==LeasingReportKind.Capacity?cap.Limit:null,Used=kind==LeasingReportKind.Capacity?cap.Used:null,Reserved=kind==LeasingReportKind.Capacity?cap.Reserved:null,Available=kind==LeasingReportKind.Capacity?cap.Available:null});}
            if(kind==LeasingReportKind.Agreements)foreach(var a in acquisitions){var row=Base(a);row.Basis="LifePurchaseValue";row.Net=a.NetTotal;row.Vat=a.VatTotal;row.Gross=a.GrossTotal;rows.Add(row);}
        }
        else if(kind==LeasingReportKind.Dimensions)
        {
            var requiredDimensions=graph.RequiredDimensions;
            foreach(var a in acquisitions)
            {
                var items=a.Items;
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
                var plans=graph.Plans.Where(x=>x.AcquisitionId==a.Id).ToList();
                if(plans.Count==0){var r=Base(a);r.NeedsReview=true;r.Notes="PaymentNoPlan";r.Status="PaymentNoPlan";rows.Add(r);continue;}
                var obligations=graph.Installments.Where(x=>x.AcquisitionId==a.Id).ToDictionary(x=>x.Id);
                var obligationIds=obligations.Keys.ToArray();var allocations=graph.Allocations.Where(x=>obligationIds.Contains(x.InstallmentId)).ToList();
                var invoiceIds=allocations.Select(x=>x.InvoiceId).Distinct().ToArray();var docs=graph.Invoices.Where(x=>invoiceIds.Contains(x.Id)).ToDictionary(x=>x.Id);
                foreach(var p in plans)foreach(var term in p.Terms)
                {
                    var links=allocations.Where(x=>x.InstallmentId==term.InstallmentId&&docs.ContainsKey(x.InvoiceId)).ToList();
                    if(filter.DateField==LeasingReportDate.Invoice)links=links.Where(x=>(!filter.From.HasValue||docs[x.InvoiceId].InvoiceDate>=filter.From)&&(!filter.To.HasValue||docs[x.InvoiceId].InvoiceDate<=filter.To)).ToList();
                    decimal net=0,vat=0;foreach(var link in links){var sign=docs[link.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1;net+=sign*link.Net;vat+=sign*link.Vat;}
                    var r=Base(a);var basis=LeasingRentalInvoiceService.TermFinancing(a,term,financing.Where(x=>x.AcquisitionId==a.Id).ToList());r.FinanceId=basis.FinanceOrganizationId;r.Finance=parties.GetValueOrDefault(basis.FinanceOrganizationId,"");r.Id=term.InstallmentId;r.Reference=term.Reference;r.From=term.PeriodFrom;r.To=term.PeriodTo;r.DueDate=term.DueDate;r.Version=p.Version;r.Expected=term.Gross;r.Net=term.Net;r.Vat=term.Vat;r.Gross=term.Gross;r.NeedsReview=term.NeedsReview||term.Gross==null||states.GetValueOrDefault(a.Id)?.PaymentPlanReviewRequired==true;r.Basis="LifePlannedPayments";r.Status=kind==LeasingReportKind.Payments?"PaymentPlanActive":$"PaymentBilling{LeasingPaymentCalculator.Status(term,obligations[term.InstallmentId],net,vat,links.Count>0)}";r.Link=$"/leasing/acquisitions/{a.Id}/payments";
                    if(kind==LeasingReportKind.InvoiceControl){r.InvoiceDate=links.Select(x=>docs[x.InvoiceId].InvoiceDate).Max();r.Invoiced=net+vat;r.Difference=r.Invoiced-r.Expected;r.Notes=string.Join("; ",links.Select(x=>$"{docs[x.InvoiceId].Number}: {x.Net}/{x.Vat}"));}rows.Add(r);
                }
            }
            if(kind==LeasingReportKind.InvoiceControl)
            {
                // Unallocated amounts belong to the document, not every possible acquisition. Emit each visible invoice once.
                var docs=graph.UnallocatedInvoices;
                foreach(var invoice in docs){if(filter.Search!=""&&!invoice.Number.Contains(filter.Search,StringComparison.OrdinalIgnoreCase)||filter.Currency!=""&&invoice.Currency!=filter.Currency||filter.FinanceId.HasValue&&invoice.FinanceOrganizationId!=filter.FinanceId||filter.OwnerId.HasValue&&invoice.UploadedByUserId!=filter.OwnerId)continue;var links=graph.UnallocatedLinks.Where(x=>x.InvoiceId==invoice.Id).ToList();var rest=invoice.Net+invoice.Vat-links.Sum(x=>x.Net+x.Vat);if(rest==0)continue;var sign=invoice.Kind==LeasingInvoiceKind.CreditNote?-1:1;rows.Add(new(){Id=invoice.Id,Link=$"/leasing/rental-invoices/{invoice.Id}",Name=invoice.Number,Reference=invoice.Number,FinanceId=invoice.FinanceOrganizationId,OwnerId=invoice.UploadedByUserId,Finance=parties.GetValueOrDefault(invoice.FinanceOrganizationId??Guid.Empty,""),Currency=invoice.Currency,InvoiceDate=invoice.InvoiceDate,Unallocated=sign*rest,Status="LifeUnallocated",Basis="RentalUnallocated",Notes="LifeNoAcquisitionAllocation"});}
            }
        }
        else
        {
            var buildings=graph.Buildings;
            foreach(var a in acquisitions)
            {
                var units=graph.Equipment.Where(x=>x.AcquisitionId==a.Id).ToList();
                foreach(var unit in units){var r=Base(a);r.Id=unit.Id;r.Reference=unit.InternalId??unit.Description;r.Quantity=1;r.UnitReference=unit.Id.ToString();r.Serial=unit.SerialNumber??"";r.Location=buildings.GetValueOrDefault(unit.BuildingId??Guid.Empty,"");r.OwnerId=unit.OwnerUserId;r.Owner=people.GetValueOrDefault(unit.OwnerUserId??Guid.Empty,"");r.Status=$"LifeEquipment{unit.Status}";r.EventDate=unit.StatusDate??unit.RegisteredDate;r.Notes=unit.Notes;rows.Add(r);}
                foreach(var item in a.Items){var r=Base(a);r.Id=item.Id;r.Reference=item.Description;r.Quantity=item.Quantity;r.Notes=$"{item.Quantity} / {units.Count(x=>x.ItemId==item.Id)}";r.Basis="LifeQuantityTracked";rows.Add(r);}
                foreach(var e in graph.Events.Where(x=>x.AcquisitionId==a.Id)){var r=Base(a);r.Id=e.Id;var input=JsonSerializer.Deserialize<LifecycleEventInput>(e.InputJson)!;r.Quantity=input.ItemId.HasValue?input.Quantity:null;r.BuyoutAmount=input.BuyoutAmount;r.UnitReference=string.Join(", ",input.EquipmentIds.Select(id=>units.FirstOrDefault(u=>u.Id==id)?.InternalId??id.ToString()));r.Reference=e.Reference;r.EventDate=e.Date;r.Status=$"LifeDecision{e.Decision}";r.Basis=$"LifeKind{e.Kind}";r.Notes=e.Reason;rows.Add(r);}
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
        var original=await Actor(account,ct);
        var result=new Dictionary<LeasingReportKind,LeasingReportResult>();foreach(var kind in new[]{LeasingReportKind.Capacity,LeasingReportKind.Payments,LeasingReportKind.InvoiceControl})result[kind]=await GetLeasingReportAsync(account,kind,new(){From=kind==LeasingReportKind.Payments?from:null,To=kind==LeasingReportKind.Payments?to:null,DateField=LeasingReportDate.Due},ct:ct);
        var active=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){Status="LifeActive"},ct:ct);var extended=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){Status="LifeStatusExtended"},ct:ct);
        var expiring=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.End,From=from,To=to,AcquisitionsOnly=true,Status="LifeActive"},ct:ct);var notice=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.Notice,From=from,To=to,Status="LifeActive"},ct:ct);var returns=await GetLeasingReportAsync(account,LeasingReportKind.Agreements,new(){DateField=LeasingReportDate.Return,From=from,To=to,Status="LifeActive"},ct:ct);
        var tasks=await followups.ListLeasingFollowupsAsync(account,status:LeasingFollowupStatus.Open,to:to,ct:ct);var missing=await GetLeasingReportAsync(account,LeasingReportKind.Payments,new(){Status="PaymentNoPlan"},ct:ct);var variance=await GetLeasingReportAsync(account,LeasingReportKind.InvoiceControl,new(){Status="PaymentBillingVariance"},ct:ct);
        await Revalidate(original,false,ct);
        return new(result,active.Count,expiring.Count,notice.Count,returns.Count,tasks.Count,missing.Count,variance.Count);
    }
}
