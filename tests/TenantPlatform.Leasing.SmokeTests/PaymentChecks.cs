using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Leasing;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Web.Services.Leasing.Invoices;

static class PaymentChecks
{
    public static async Task Run(IDbContextFactory<TenantPlatformDbContext> factory,Guid adminId,Guid ownerId,Guid outsiderId,Guid foreignAccount)
    {
        ImportAndDates();
        var account=Guid.NewGuid();var party=Guid.NewGuid();var otherParty=Guid.NewGuid();
        await using(var db=factory.CreateDbContext()){
            db.Accounts.Add(new(){Id=account,Name="Payment checks"});
            db.Organizations.AddRange(new Organization{Id=party,AccountId=account,Name="Finance"},new Organization{Id=otherParty,AccountId=account,Name="Other finance"});
            foreach(var u in new[]{adminId,ownerId,outsiderId}){var m=new UserAccount{Id=Guid.NewGuid(),AccountId=account,UserId=u};db.UserAccounts.Add(m);if(u==adminId)db.UserAccountRoles.Add(new(){Id=Guid.NewGuid(),UserAccountId=m.Id,Role=UserRole.AccountAdmin});}await db.SaveChangesAsync();
        }
        var storage=new PaymentStorage();
        LeasingService Service(Guid u){var c=new Context(u,account);return new(factory,c,new TenantAuthorizationService(factory,c),storage,new Clock(),NullLogger<LeasingService>.Instance);}
        var admin=Service(adminId);var owner=Service(ownerId);var outsider=Service(outsiderId);
        var frame=await admin.SaveFrameworkAsync(account,null,new(){Name="Payments frame",Number="PAY",OwnerUserId=ownerId,FinanceOrganizationId=party,AcquisitionFrom=new(2027,1,1),AcquisitionTo=new(2029,1,1),Limit=100000,Terms=new(){Months=60}},"");
        async Task<Guid> Acquisition(string currency="NOK",Guid? finance=null){var a=await admin.NewAcquisitionAsync(account,currency=="NOK"?frame:null);a.Name="Payment acquisition";a.Reference=Guid.NewGuid().ToString();a.OwnerUserId=ownerId;a.SupplierOrganizationId=party;a.FinanceOrganizationId=finance??party;a.Currency=currency;a.PurchaseDate=new(2028,1,1);a.Status=LeasingAcquisitionStatus.Registered;a.FinancedAmount=600;a.Items=[new(){Description="Equipment",Quantity=1,UnitPrice=1000,VatPercent=25}];return await admin.SaveAcquisitionAsync(account,null,a,"");}
        var a1=await Acquisition();var a2=await Acquisition();var eur=await Acquisition("EUR");var wrongParty=await Acquisition("EUR",otherParty);
        var order=await owner.SaveOrderAsync(account,null,new(){FrameworkId=frame,OwnerUserId=ownerId,SupplierOrganizationId=party,OrderDate=new(2028,1,1),Lines=[new(){Description="Reserved",Quantity=1,UnitPrice=2000}]},"",Guid.NewGuid());
        var od=await admin.GetOrderAsync(account,order);await admin.OrderActionAsync(account,order,od.Order.Revision,LeasingOrderStatus.Pending,"Review",Guid.NewGuid());od=await admin.GetOrderAsync(account,order);await admin.OrderActionAsync(account,order,od.Order.Revision,LeasingOrderStatus.Approved,"Reserve",Guid.NewGuid());
        var capacity=await admin.GetAsync(account,frame,true);
        async Task<PaymentDetails> Details(Guid a)=>await admin.GetPaymentsAsync(account,a);
        var baseline=(await Details(a1)).Financing.Single();Check(baseline.EffectiveFrom==null&&JsonSerializer.Deserialize<LeasingFinancingSnapshot>(baseline.SnapshotJson)!.FinancedAmount==600,"new acquisition retains financing with unknown effective date and no auto plan");
        async Task Revision(Guid a,DateOnly date,decimal rate=4){var d=await Details(a);var f=LeasingFinancingSnapshot.From(d.Acquisition);f.Terms.InterestKind=LeasingInterestKind.Reference;f.Terms.AnnualRatePercent=null;f.Terms.ReferenceRateName="NIBOR 3M";f.Terms.MarginPercentagePoints=2;f.Terms.ObservedReferenceRate=rate;f.Terms.ObservationDate=date;f.Terms.PaymentFrequency=LeasingPaymentFrequency.Monthly;f.Terms.FinanceReference="LEASE-A";f.Terms.AdvanceRent=100;f.Terms.EstablishmentFee=50;f.Terms.ResidualValue=500;await owner.AddFinancingRevisionAsync(account,a,d.Acquisition.Revision,date,f,"New agreed terms",Guid.NewGuid());}
        foreach(var a in new[]{a1,a2,eur,wrongParty})await Revision(a,new(2028,1,1));
        var initial=(await Details(a1)).Financing.Single(x=>x.EffectiveFrom.HasValue);
        var snap=JsonSerializer.Deserialize<LeasingFinancingSnapshot>(initial.SnapshotJson)!;
        await Reject(()=>owner.AddFinancingRevisionAsync(account,a1,Details(a1).Result.Acquisition.Revision,new(2028,1,1),snap,"Duplicate",Guid.NewGuid()),"PaymentEffectiveDuplicate");
        var terms=LeasingPaymentCalculator.Series(new(2028,1,1),new(2028,1,31),new(2028,1,31),3,LeasingPaymentFrequency.Monthly,true,100,25);
        terms[0].Net=80;terms[0].Vat=20;terms[0].Gross=100;terms[2].Net=120;terms[2].Vat=30;terms[2].Gross=150;
        foreach(var t in terms)t.FinancingRevisionId=initial.Id;
        var draftRequest=Guid.NewGuid();var plan=await owner.SavePaymentPlanAsync(account,a1,null,terms,"Agreed series",LeasingPlanSource.Manual,null,draftRequest);
        Check(await owner.SavePaymentPlanAsync(account,a1,null,terms,"Agreed series",LeasingPlanSource.Manual,null,draftRequest)==plan,"draft retry is idempotent");
        Check(await owner.SavePaymentPlanAsync(account,a1,null,terms,"Agreed series",LeasingPlanSource.Manual,null,Guid.NewGuid())==plan,"same content reimport does not duplicate plan or instalments");
        var p=(await Details(a1)).Plans.Single();await Deny(()=>owner.ActivatePaymentPlanAsync(account,plan,p.Revision,"No approval role",false,Guid.NewGuid()));await Deny(()=>outsider.GetPaymentsAsync(account,a1));await Deny(()=>admin.GetPaymentsAsync(foreignAccount,a1));
        await admin.ActivatePaymentPlanAsync(account,plan,p.Revision,"Checked agreement",false,Guid.NewGuid());
        // Import persists the original and returns only a draft; identical reimport is stable.
        var csv="Reference;From;To;Due;Net;VAT;Gross;Type\nIMP;01.01.2028;31.01.2028;05.02.2028;100;25;125;leie\n";
        await using var csvStream=new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var imported=await owner.UploadPaymentPlanSourceAsync(account,a2,"plan.csv",csvStream);
        var readGrid=await owner.ReadPaymentPlanSourceAsync(account,a2,imported.Document);
        var importedTerms=PaymentPlanImport.Preview(readGrid,new(),"NOK").Terms;
        importedTerms.ForEach(t=>t.FinancingRevisionId=(Details(a2).Result).Financing.Single(x=>x.EffectiveFrom.HasValue).Id);
        var importedId=await owner.SavePaymentPlanAsync(account,a2,null,importedTerms,"Imported",LeasingPlanSource.Imported,imported.Document,Guid.NewGuid());
        Check((await Details(a2)).Plans.Single().Status==LeasingPlanStatus.Draft&&(await Details(a2)).Terms.Count==0,"CSV source round-trips and import never activates a plan");
        Check(await owner.SavePaymentPlanAsync(account,a2,null,importedTerms,"Imported",LeasingPlanSource.Imported,imported.Document,Guid.NewGuid())==importedId,"reimport preserves draft identity");
        var unknown=Clone(terms);unknown[0].Net=null;
        var unknownId=await owner.SavePaymentPlanAsync(account,a1,plan,unknown,"Unknown",LeasingPlanSource.Manual,null,Guid.NewGuid());var unknownPlan=(await Details(a1)).Plans.Single(x=>x.Id==unknownId);
        await Reject(()=>admin.ActivatePaymentPlanAsync(account,unknownId,unknownPlan.Revision,"Reviewed",true,Guid.NewGuid()),"PaymentUnknownAmounts");
        var residual=Clone(terms);residual[0].Type=LeasingPaymentType.ResidualObligation;
        await Reject(()=>owner.SavePaymentPlanAsync(account,a1,plan,residual,"No obligation document",LeasingPlanSource.Manual,null,Guid.NewGuid()),"PaymentResidualDocumentation");
        var components=Clone(terms);components[0].ComponentsComplete=true;components[0].CapitalComponent=50;components[0].InterestComponent=10;components[0].FeeComponent=0;
        await Reject(()=>owner.SavePaymentPlanAsync(account,a1,plan,components,"Wrong components",LeasingPlanSource.Manual,null,Guid.NewGuid()),"PaymentComponentsMismatch");
        var outside=Clone(terms);outside[0].PeriodFrom=new(2027,12,1);outside[0].FinancingRevisionId=null;
        var outsideId=await owner.SavePaymentPlanAsync(account,a1,plan,outside,"Outside period",LeasingPlanSource.Manual,null,Guid.NewGuid());var outsidePlan=(await Details(a1)).Plans.Single(x=>x.Id==outsideId);
        await Reject(()=>admin.ActivatePaymentPlanAsync(account,outsideId,outsidePlan.Revision,"Checked",false,Guid.NewGuid()),"PaymentReviewRequired");
        await Deny(()=>outsider.ReadPaymentPlanSourceAsync(account,a2,imported.Document));
        var uploadedBytes=Encoding.UTF8.GetBytes("<Invoice>rental source</Invoice>");
        var upload=await owner.UploadRentalInvoiceAsync(account,"rental.xml",new MemoryStream(uploadedBytes));
        Check(await owner.UploadRentalInvoiceAsync(account,"same.xml",new MemoryStream(uploadedBytes))==upload,"rental document reupload is idempotent");
        var processor=new LeasingInvoiceProcessor(factory,new PaymentInterpreter(),new Clock());
        for(var attempt=0;attempt<50&&(await owner.GetRentalInvoiceAsync(account,upload)).Invoice.Processing==LeasingInvoiceProcessing.Uploaded;attempt++)await processor.ProcessOneAsync();
        var parsed=await owner.GetRentalInvoiceAsync(account,upload);
        Check(parsed.Interpretations.Count==1&&parsed.Review.Data.Number=="RENTAL-PARSED"&&parsed.Invoice.AcquisitionId==null&&parsed.Invoice.Status==LeasingInvoiceStatus.Review,"shared worker interprets rental source into separate editable review without equipment effects");
        await owner.DeleteRentalInvoiceAsync(account, upload, parsed.Invoice.Revision);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Check(!await db.LeasingInvoices.AnyAsync(x => x.AccountId == account && x.Id == upload) &&
                !await db.LeasingInvoiceInterpretations.AnyAsync(x => x.AccountId == account && x.InvoiceId == upload) &&
                !await db.LeasingInvoiceHistory.AnyAsync(x => x.AccountId == account && x.InvoiceId == upload), "deleting uploaded draft removes invoice, interpretations and history");
            Check(await db.LeasingPaymentEvents.AnyAsync(x => x.AccountId == account && x.Action == "RentalDeleted" && x.Reason == upload.ToString()), "draft deletion retains audit trail");
        }
        Check(!storage.Contains(parsed.Invoice.StorageKey), "draft deletion removes original file");
        await Deny(() => owner.GetRentalInvoiceAsync(account, upload));
        var deletable = await owner.NewRentalInvoiceAsync(account, Guid.NewGuid());
        var deletionDraft = await owner.GetRentalInvoiceAsync(account, deletable);
        await Deny(() => outsider.DeleteRentalInvoiceAsync(account, deletable, deletionDraft.Invoice.Revision));
        await Deny(() => owner.DeleteRentalInvoiceAsync(foreignAccount, deletable, deletionDraft.Invoice.Revision));
        await Reject(() => owner.DeleteRentalInvoiceAsync(account, deletable, Guid.NewGuid()), "LeasingConcurrency");
        await admin.DeleteRentalInvoiceAsync(account, deletable, deletionDraft.Invoice.Revision);
        Check(!(await admin.ListRentalInvoicesAsync(account)).Any(x => x.Invoice.Id == deletable), "account admin can delete a manual draft");
        var d1=await Details(a1);Check(d1.Terms.Count==3&&d1.Terms.Sum(x=>x.Term.Gross)==375,"advance rent, fees and residual are never auto-added; special first and last amounts retained");
        var ids=d1.Terms.Select(x=>x.Installment.Id).ToArray();
        async Task<Guid> Active(Guid a){var ds=await Details(a);var t=new LeasingPlanTerm{Reference="1",PeriodFrom=new(2028,1,1),PeriodTo=new(2028,1,31),DueDate=new(2028,2,5),Net=100,Vat=25,Gross=125,FinancingRevisionId=ds.Financing.Single(x=>x.EffectiveFrom.HasValue).Id};var id=await owner.SavePaymentPlanAsync(account,a,null,[t],"",LeasingPlanSource.Manual,null,Guid.NewGuid());var pp=(await Details(a)).Plans.Single(x=>x.Id==id);await admin.ActivatePaymentPlanAsync(account,id,pp.Revision,"Checked",false,Guid.NewGuid());return (await Details(a)).Terms.Single().Installment.Id;}
        var id2=await Active(a2);var eurId=await Active(eur);var wrongPartyId=await Active(wrongParty);
        async Task<Guid> DraftInvoice(decimal net,List<RentalAllocationInput> allocations,bool credit=false,Guid? original=null,string currency="NOK",Guid? finance=null){var id=await owner.NewRentalInvoiceAsync(account,Guid.NewGuid());var r=new RentalInvoiceReview{FinanceOrganizationId=finance??party,OriginalInvoiceId=original,ContentConfirmed=true,AgreementReference="LEASE-A",Data=new(){Kind=credit?LeasingInvoiceKind.CreditNote:LeasingInvoiceKind.Invoice,Number=id.ToString(),Date=new(2028,1,15),DueDate=new(2028,2,10),Currency=currency,Net=net,Vat=InvoiceCalculator.Money(net*.25m),Gross=net+InvoiceCalculator.Money(net*.25m),Lines=[new(){SourceId="1",Description="Rental",Quantity=1,Price=net,Net=net,VatPercent=25,VatCategory="S"}]},Allocations=allocations};var inv=await owner.GetRentalInvoiceAsync(account,id);await owner.SaveRentalReviewAsync(account,id,inv.Invoice.Revision,r);return id;}
        async Task Approve(Guid id){var d=await admin.GetRentalInvoiceAsync(account,id);await admin.ApproveRentalInvoiceAsync(account,id,d.Invoice.Revision);}
        var inv=await DraftInvoice(200,[new(){InstallmentId=ids[0],Net=40,Vat=10},new(){InstallmentId=ids[1],Net=50,Vat=12.5m},new(){InstallmentId=id2,Net=60,Vat=15}]);
        await Deny(()=>owner.ApproveRentalInvoiceAsync(account,inv,(owner.GetRentalInvoiceAsync(account,inv).Result).Invoice.Revision));
        var beforeApproval=(await admin.GetRentalInvoiceAsync(account,inv)).Invoice.Revision;
        await Task.WhenAll(admin.ApproveRentalInvoiceAsync(account,inv,beforeApproval),Service(adminId).ApproveRentalInvoiceAsync(account,inv,beforeApproval));
        var approved=await admin.GetRentalInvoiceAsync(account,inv);Check(approved.Allocations.Count==3&&approved.Allocations.Sum(x=>x.Net)==150,"concurrent approval creates allocations once across terms and acquisitions with unallocated remainder");
        await Reject(() => admin.DeleteRentalInvoiceAsync(account, inv, approved.Invoice.Revision), "RentalDeleteOnlyDraft");
        await Deny(()=>admin.GetInvoiceAsync(account,inv));Check(!(await admin.ListInvoicesAsync(account)).Any(x=>x.Id==inv),"rental documents never enter equipment invoice path");
        Check((await Details(a1)).Terms.First().Status==LeasingBillingStatus.Partial,"partial invoicing is not final variance");
        var second=await DraftInvoice(40,[new(){InstallmentId=ids[0],Net=40,Vat=10}]);await Approve(second);
        var term=(await Details(a1)).Terms.First();await admin.ReviewPaymentTermAsync(account,ids[0],term.Installment.Revision,true,false,false,"All invoices received",Guid.NewGuid());Check((await Details(a1)).Terms.First().Status==LeasingBillingStatus.Reconciled,"multiple invoices reconcile one completed instalment");
        term=(await Details(a1)).Terms[1];await admin.ReviewPaymentTermAsync(account,ids[1],term.Installment.Revision,true,false,false,"Final invoice",Guid.NewGuid());term=(await Details(a1)).Terms[1];Check(term.Status==LeasingBillingStatus.Variance,"underbilling becomes variance only after explicit completion");await admin.ReviewPaymentTermAsync(account,ids[1],term.Installment.Revision,true,true,false,"Documented agreed discount",Guid.NewGuid());term=(await Details(a1)).Terms[1];Check(term.Status==LeasingBillingStatus.Accepted&&term.Term.Net==100,"variance acceptance retains expected amount");
        await Revision(a1,new(2028,2,1),5);d1=await Details(a1);Check(d1.Terms[0].Term.NeedsReview==false&&d1.Terms.Skip(1).All(x=>x.Term.NeedsReview)&&d1.Terms.Sum(x=>x.Term.Net)==300,"future rate revision marks affected terms without recomputation");Check(LeasingService.FinancingAt(d1.Financing,new(2028,1,31))!.Id==initial.Id&&LeasingService.FinancingAt(d1.Financing,new(2028,2,1))!.Id!=initial.Id,"dated terms use unambiguous half-open periods");
        var changed=Clone(d1.Plans.Single(x=>x.Status==LeasingPlanStatus.Active).Terms);changed[0].Net=90;changed[0].Vat=22.5m;changed[0].Gross=112.5m;foreach(var t in changed){t.FinancingRevisionId=LeasingService.FinancingAt(d1.Financing,t.PeriodFrom)?.Id;t.ReviewReason="Checked revised agreement";}
        var revisionPlan=await owner.SavePaymentPlanAsync(account,a1,plan,changed,"Revised",LeasingPlanSource.Manual,null,Guid.NewGuid());p=(await Details(a1)).Plans.Single(x=>x.Id==revisionPlan);
        await Reject(()=>admin.ActivatePaymentPlanAsync(account,p.Id,p.Revision,"Reviewed",false,Guid.NewGuid()),"PaymentConfirmInvoicedChange");await admin.ActivatePaymentPlanAsync(account,p.Id,p.Revision,"Explicitly reviewed invoiced changes",true,Guid.NewGuid());d1=await Details(a1);Check(d1.Plans.Count(x=>x.Status==LeasingPlanStatus.Active)==1&&d1.Plans.Single(x=>x.Id==plan).Status==LeasingPlanStatus.Replaced&&d1.Terms.First().Installment.Id==ids[0]&&d1.Terms.First().InvoicedNet==80,"plan replacement preserves stable allocation identity and a single active expectation");
        var removed=Clone(d1.Plans.First(x=>x.Status==LeasingPlanStatus.Active).Terms);removed.RemoveAll(x=>x.InstallmentId==ids[0]);var removal=await owner.SavePaymentPlanAsync(account,a1,revisionPlan,removed,"Remove",LeasingPlanSource.Manual,null,Guid.NewGuid());p=(await Details(a1)).Plans.Single(x=>x.Id==removal);await Reject(()=>admin.ActivatePaymentPlanAsync(account,p.Id,p.Revision,"Remove",true,Guid.NewGuid()),"PaymentAllocatedRemoval");
        var over=await DraftInvoice(10,[new(){InstallmentId=ids[0],Net=11,Vat=2.5m}]);await Reject(()=>Approve(over),"PaymentOverAllocation");Check((await admin.GetRentalInvoiceAsync(account,over)).Invoice.Status==LeasingInvoiceStatus.Review,"over-allocation approval rolls back atomically");
        var foreignCurrency=await DraftInvoice(10,[new(){InstallmentId=eurId,Net=10,Vat=2.5m}]);await Reject(()=>Approve(foreignCurrency),"PaymentMatchPartyCurrency");var foreignParty=await DraftInvoice(10,[new(){InstallmentId=wrongPartyId,Net=10,Vat=2.5m}],currency:"EUR");await Reject(()=>Approve(foreignParty),"PaymentMatchPartyCurrency");
        var originalLink=approved.Allocations.Single(x=>x.InstallmentId==ids[0]);var credit=await DraftInvoice(20,[new(){InstallmentId=ids[0],Net=20,Vat=5,CreditedAllocationId=originalLink.Id}],true,inv);await Approve(credit);Check((await Details(a1)).Terms.First().InvoicedNet==60,"partial credit reduces only invoiced amounts");
        var overCredit=await DraftInvoice(25,[new(){InstallmentId=ids[0],Net=25,Vat=6.25m,CreditedAllocationId=originalLink.Id}],true,inv);await Reject(()=>Approve(overCredit),"InvoiceOverCredit");
        approved=await admin.GetRentalInvoiceAsync(account,inv);await Reject(()=>admin.ReverseRentalInvoiceAsync(account,inv,approved.Invoice.Revision,"Wrong",Guid.NewGuid()),"InvoiceReverseDependencies");
        var cr=(await admin.GetRentalInvoiceAsync(account,credit)).Invoice;await admin.ReverseRentalInvoiceAsync(account,credit,cr.Revision,"Incorrect credit",Guid.NewGuid());
        approved=await admin.GetRentalInvoiceAsync(account,inv);var matchRequest=Guid.NewGuid();var corrected=new List<RentalAllocationInput>{new(){InstallmentId=ids[0],Net=50,Vat=12.5m},new(){InstallmentId=id2,Net=100,Vat=25}};await admin.CorrectRentalAllocationsAsync(account,inv,approved.Invoice.Revision,corrected,"Correct source split",matchRequest);await admin.CorrectRentalAllocationsAsync(account,inv,approved.Invoice.Revision,corrected,"Retry",matchRequest);approved=await admin.GetRentalInvoiceAsync(account,inv);Check(approved.Allocations.Count(x=>x.Reversed)==3&&approved.Allocations.Count(x=>!x.Reversed)==2,"allocation correction retains reversed history and is idempotent");
        // Two competing corrections cannot both spend the same revision.
        var rev=approved.Invoice.Revision;async Task<bool> Correct(){try{await Service(adminId).CorrectRentalAllocationsAsync(account,inv,rev,corrected,"Concurrent correction",Guid.NewGuid());return true;}catch(LeasingValidationException e)when(e.Message=="LeasingConcurrency"){return false;}}
        Check((await Task.WhenAll(Correct(),Correct())).Count(x=>x)==1,"concurrent allocation corrections serialize and reject stale revision");
        approved=await admin.GetRentalInvoiceAsync(account,inv);await admin.ReverseRentalInvoiceAsync(account,inv,approved.Invoice.Revision,"Wrong document",Guid.NewGuid());Check((await Details(a1)).Terms.First().InvoicedNet==40,"reversal removes economic effect but retains documents and allocations");
        var after=await admin.GetAsync(account,frame,true);Check(after.Used==capacity.Used&&after.Reserved==capacity.Reserved,"all payment operations preserve purchased value, framework use and active reservations");foreach(var a in new[]{a1,a2}){var actual=(await admin.GetAsync(account,a,false)).Acquisition!;Check(actual.Items.Single().UnitPrice==1000&&actual.FinancedAmount==600,"purchase lines and original financed amount remain intact");}
        await using(var db=factory.CreateDbContext())
        {
            var foreignAcquisition=await db.LeasingAcquisitions.AsNoTracking().FirstAsync(x=>x.AccountId!=account);
            db.LeasingInstallments.Add(new(){Id=Guid.NewGuid(),AccountId=foreignAcquisition.AccountId,AcquisitionId=foreignAcquisition.Id,Reference="FOREIGN-TEST",Revision=Guid.NewGuid()});await db.SaveChangesAsync();
            var foreignTerm=await db.LeasingInstallments.AsNoTracking().FirstAsync(x=>x.AccountId!=account);
            var ownDraft=await owner.NewRentalInvoiceAsync(account,Guid.NewGuid());var ownReview=await owner.GetRentalInvoiceAsync(account,ownDraft);
            ownReview.Review.Allocations=[new(){InstallmentId=foreignTerm.Id,Net=1}];
            await Deny(()=>owner.SaveRentalReviewAsync(account,ownDraft,ownReview.Invoice.Revision,ownReview.Review));
        }
        var reversedInvoice = await admin.GetRentalInvoiceAsync(account, inv);
        await Reject(() => admin.DeleteRentalInvoiceAsync(account, inv, reversedInvoice.Invoice.Revision), "RentalDeleteOnlyDraft");
        // Dedicated roles can inspect options and act only in their own approval domain.
        await using(var db=factory.CreateDbContext()){var m=await db.UserAccounts.SingleAsync(x=>x.AccountId==account&&x.UserId==outsiderId);db.UserAccountRoles.Add(new(){Id=Guid.NewGuid(),UserAccountId=m.Id,Role=UserRole.LeasingPlanApprover});await db.SaveChangesAsync();}
        Check((await outsider.GetPaymentsAsync(account,a1)).Permissions.CanActivate&&!(await outsider.GetPaymentsAsync(account,a1)).Permissions.CanApproveInvoices,"plan approval role is separate from invoice and variance approval");await outsider.OptionsAsync(account);await Deny(()=>outsider.NewRentalInvoiceAsync(account,Guid.NewGuid()));
        await PaymentComponentChecks.Run(admin,account,adminId,a1,second);
        Console.WriteLine("All phase-five payment checks passed.");
    }
    static void ImportAndDates(){
        foreach(var f in Enum.GetValues<LeasingPaymentFrequency>()){var s=LeasingPaymentCalculator.Series(new(2028,1,1),new(2028,1,31),new(2028,1,31),4,f,true,10,2.5m);Check(s[3].DueDate==LeasingPaymentCalculator.Anchored(new(2028,1,31),3*(int)f,true),"series frequency "+f+" remains anchored");}
        var dates=LeasingPaymentCalculator.Series(new(2028,1,1),new(2028,1,31),new(2028,1,30),3,LeasingPaymentFrequency.Monthly,false,10,2.5m);Check(dates[1].DueDate==new DateOnly(2028,2,29)&&dates[2].DueDate==new DateOnly(2028,3,30),"leap February clamps without shifting March due day");Check(LeasingPaymentCalculator.Anchored(new(2028,2,29),12,true)==new DateOnly(2029,2,28),"leap month-end annual rule");
        var csv="Ref;Fra;Til;Forfall;Netto;Mva;Brutto;Type;Valuta\n1;01.01.2028;31.01.2028;05.02.2028;1 234,56;308,64;1.543,20;leie;NOK\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(csv));var grid=PaymentPlanImport.Read(stream,"plan.csv");var map=new PaymentColumnMap{Currency=8};var preview=PaymentPlanImport.Preview(grid,map,"NOK");Check(preview.Errors.Count==0&&preview.Terms.Single().Gross==1543.20m,"CSV Norwegian date decimal and thousands separators");
        var duplicate=new PaymentImportGrid(grid.Headers,[grid.Rows[0],grid.Rows[0]],false,false,[]);Check(PaymentPlanImport.Preview(duplicate,map,"NOK").Errors.Single().Key=="PaymentDuplicateTerm","duplicate CSV rows rejected");Check(PaymentPlanImport.Preview(grid,map,"EUR").Errors.Single().Key=="PaymentImportCurrency","import currency mismatch rejected");grid.Rows[0][6]="1";Check(PaymentPlanImport.Preview(grid,map,"NOK").Errors.Single().Key=="PaymentImportTotals","import totals mismatch rejected");grid.Rows[0][4]="";grid.Rows[0][5]="";grid.Rows[0][6]="";Check(PaymentPlanImport.Preview(grid,map,"NOK").Terms.Single().Net==null,"unknown imported amounts remain null");
        using var excel=Xlsx(false);var xp=PaymentPlanImport.Preview(PaymentPlanImport.Read(excel,"plan.xlsx"),new(),"NOK");Check(xp.Errors.Count==0&&xp.Terms.Single().PeriodFrom==new DateOnly(2028,1,1)&&xp.Terms.Single().Gross==125,"XLSX numeric dates and amounts supported");try{using var formula=Xlsx(true);PaymentPlanImport.Read(formula,"plan.xlsx");throw new Exception("Expected formula rejection");}catch(LeasingValidationException e)when(e.Message=="PaymentImportFormula"){Check(true,"XLSX formula rejected without evaluation");}
        var t=new LeasingPlanTerm{Net=100,Vat=25,Gross=125};var i=new LeasingInstallment{BillingComplete=true};Check(LeasingPaymentCalculator.Status(t,i,100.01m,25,true)==LeasingBillingStatus.Reconciled&&LeasingPaymentCalculator.Status(t,i,100.01m,25.01m,true)==LeasingBillingStatus.Variance,"cent tolerance checks gross as well as net and VAT");
    }
    static MemoryStream Xlsx(bool formula){var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){void Entry(string name,string text){using var w=new StreamWriter(zip.CreateEntry(name).Open());w.Write(text);}Entry("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Plan\" sheetId=\"1\" r:id=\"r1\"/></sheets></workbook>");Entry("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id=\"r1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");var header=string.Concat(Enumerable.Range(0,8).Select(i=>$"<c r=\"{(char)('A'+i)}1\" t=\"inlineStr\"><is><t>Header{i}</t></is></c>"));var serial=new DateTime(2028,1,1).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);var values=new[]{"1",serial,serial,serial,"100","25","125","1"};var cells=string.Concat(values.Select((v,i)=>$"<c r=\"{(char)('A'+i)}2\">{(formula&&i==4?"<f>50+50</f>":"")}<v>{v}</v></c>"));Entry("xl/worksheets/sheet1.xml",$"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row>{header}</row><row>{cells}</row></sheetData></worksheet>");}stream.Position=0;return stream;}
    sealed class PaymentStorage:IAgreementDocumentStorage
    {
        readonly Dictionary<string,byte[]> files=[];public long MaxFileSizeBytes=>1_000_000;
        public bool Contains(string key) => files.ContainsKey(key);
        public async Task<StoredAgreementFile> StoreAsync(Stream source,string fileName,CancellationToken ct=default){using var buffer=new MemoryStream();await source.CopyToAsync(buffer,ct);var key=Guid.NewGuid().ToString();files[key]=buffer.ToArray();return new(key,fileName,fileName.EndsWith(".csv")?"text/csv":"application/xml",buffer.Length);}
        public Task<Stream> OpenReadAsync(string key,CancellationToken ct=default)=>Task.FromResult<Stream>(new MemoryStream(files[key]));
        public Task DiscardUncommittedAsync(string key){files.Remove(key);return Task.CompletedTask;}
    }
    sealed class PaymentInterpreter:IInvoiceDocumentInterpreter {public Task<InvoiceInterpretationResult> InterpretAsync(LeasingInvoice invoice,CancellationToken ct)=>Task.FromResult(new InvoiceInterpretationResult(new(){Number="RENTAL-PARSED",Currency="NOK"},"test-rental","Original evidence",[]));}
    static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL payment: "+message);Console.WriteLine("PASS payment: "+message);}
    static async Task Reject(Func<Task> action,string key){try{await action();throw new Exception("Expected "+key);}catch(LeasingValidationException e)when(e.Message==key){Console.WriteLine("PASS payment rejection: "+key);}}
    static async Task Deny(Func<Task> action){try{await action();throw new Exception("Expected access denied");}catch(UnauthorizedAccessException){Console.WriteLine("PASS payment access denied");}}
}
