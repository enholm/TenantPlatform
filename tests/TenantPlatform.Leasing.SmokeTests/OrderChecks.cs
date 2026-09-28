using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Leasing;
using TenantPlatform.Web.Services.Leasing.Invoices;

static class OrderChecks
{
    public static async Task Run(IDbContextFactory<TenantPlatformDbContext> factory, Guid oldAccount, Guid adminId, Guid ownerId, Guid outsiderId, Guid oldParty, Guid foreignAccount)
    {
        var account=Guid.NewGuid(); var party=Guid.NewGuid();
        await using(var db=factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id=account, Name="Order checks" });
            db.Organizations.Add(new Organization { Id=party, AccountId=account, Name="Supplier" });
            foreach(var user in new[]{adminId,ownerId,outsiderId}) { var member=new UserAccount { Id=Guid.NewGuid(), AccountId=account, UserId=user }; db.UserAccounts.Add(member); if(user==adminId) db.UserAccountRoles.Add(new(){Id=Guid.NewGuid(),UserAccountId=member.Id,Role=UserRole.AccountAdmin}); }
            await db.SaveChangesAsync();
        }
        LeasingService Service(Guid user) { var context=new Context(user,account); return new(factory,context,new TenantAuthorizationService(factory,context),new MemoryStorage(),new Clock(),NullLogger<LeasingService>.Instance); }
        var admin=Service(adminId); var owner=Service(ownerId); var outsider=Service(outsiderId);
        async Task<Guid> Frame(decimal limit, bool vat=false, DateOnly? end=null)
        { return await admin.SaveFrameworkAsync(account,null,new(){ Name="Orders",Number=Guid.NewGuid().ToString(),OwnerUserId=ownerId,FinanceOrganizationId=party,AcquisitionFrom=new(2027,1,1),AcquisitionTo=end??new(2028,12,31),Limit=limit,IncludesVat=vat,CreditNotesReleaseLimit=true },""); }
        async Task<OrderDetails> Read(Guid id)=>await admin.GetOrderAsync(account,id);
        async Task<LeasingDetails> Capacity(Guid id)=>await admin.GetAsync(account,id,true);
        async Task<Guid> Order(Guid f, decimal quantity=10, decimal price=10000, LeasingOrderMethod method=LeasingOrderMethod.Quantity)
        { var request=Guid.NewGuid(); var input=new LeasingOrder {FrameworkId=f,SupplierOrganizationId=party,OwnerUserId=ownerId,OrderDate=new(2028,1,10),Lines=[new(){Description="Equipment",Quantity=quantity,UnitPrice=price,Method=method}]}; var id=await owner.SaveOrderAsync(account,null,input,"",request); Check(await owner.SaveOrderAsync(account,null,input,"",request)==id,"order creation retry"); return id; }
        async Task Act(Guid id, LeasingOrderStatus status) { var o=(await Read(id)).Order; await admin.OrderActionAsync(account,id,o.Revision,status,"Test action",Guid.NewGuid()); }
        async Task Approve(Guid id) { await Act(id,LeasingOrderStatus.Pending); await Act(id,LeasingOrderStatus.Approved); }
        async Task<LeasingAcquisition> Purchase(Guid f, decimal quantity, decimal price)
        { var a=await admin.NewAcquisitionAsync(account,f); a.Name="Purchase";a.Reference=Guid.NewGuid().ToString();a.SupplierOrganizationId=party;a.OwnerUserId=ownerId;a.PurchaseDate=new(2027,6,1);a.Status=LeasingAcquisitionStatus.Registered;a.Items=[new(){Description="Actual",Quantity=quantity,UnitPrice=price}];a.FinancedAmount=LeasingCalculator.Total(a.Items).Gross;return a; }
        async Task<OrderDelivery> Delivery(Guid id, decimal scope, bool confirmed=true) { var o=(await Read(id)).Order; return new(){OrderId=id,Revision=o.Revision,VarianceConfirmed=confirmed,Lines=[new(){OrderLineId=o.Lines.Single().Id,ItemPosition=0,Scope=scope}]}; }
        async Task<Guid> Buy(Guid f, Guid order, decimal quantity, decimal price, decimal? scope=null) => await admin.SaveAcquisitionAsync(account,null,await Purchase(f,quantity,price),"",delivery:await Delivery(order,scope??quantity));
        var f=await Frame(104000); var order=await Order(f);
        Check((await Capacity(f)).Reserved==0,"draft does not reserve"); await Act(order,LeasingOrderStatus.Pending);
        Check((await Capacity(f)).Reserved==0,"pending does not reserve");
        await Deny(()=>owner.OrderActionAsync(account,order,(Read(order).GetAwaiter().GetResult()).Order.Revision,LeasingOrderStatus.Approved,"No approval right",Guid.NewGuid()));
        await Act(order,LeasingOrderStatus.Approved); Check((await Capacity(f)).Reserved==100000,"approval reserves original order value");
        await Deny(()=>outsider.GetOrderAsync(account,order)); await Deny(()=>admin.GetOrderAsync(foreignAccount,order));
        await Reject(()=>admin.SaveAcquisitionAsync(account,null,Purchase(f,1,4001).GetAwaiter().GetResult(),""),"LeasingLimitExceeded");
        var unconfirmed=await Delivery(order,4,false);
        await Reject(()=>admin.SaveAcquisitionAsync(account,null,Purchase(f,4,11000).GetAwaiter().GetResult(),"",delivery:unconfirmed),"LeasingOrderConfirmVariance");
        Check((await Capacity(f)).Used==0 && (await Capacity(f)).Reserved==100000,"unconfirmed price variance rolls back purchase and reservation");
        var a=await Purchase(f,4,11000); var delivery=await Delivery(order,4); var aid=await admin.SaveAcquisitionAsync(account,null,a,"",delivery:delivery);
        Check(await admin.SaveAcquisitionAsync(account,null,a,"",delivery:delivery)==aid,"acquisition retry returns same result without consuming again");
        var d=await Read(order); Check(d.Capacity.Used==44000 && d.Capacity.Reserved==60000 && d.Capacity.Available==0,"four units at higher price use 44000 and leave 60000 reserved");
        Check(d.Realizations.Single().ApprovedNet==40000 && d.Realizations.Single().ActualNet==44000,"realization preserves approved and actual values");
        await Reject(()=>Buy(f,order,7,10000),"LeasingOrderOverDelivery");
        var purchase=(await admin.GetAsync(account,aid,false)).Acquisition!; purchase.Items[0].UnitPrice=1;
        await Reject(()=>admin.SaveAcquisitionAsync(account,aid,purchase,"Cannot silently edit"),"LeasingOrderLinkedImmutable");
        // Proposal remains pending while another delivery changes the current state.
        var proposed=Clone(d.Order); proposed.Lines[0].Quantity=5;
        await owner.SaveOrderAsync(account,order,proposed,"Reduce remaining order",Guid.NewGuid());
        Check((await Read(order)).Capacity.Reserved==60000,"pending reduction retains approved reservation");
        await Buy(f,order,2,10000);
        await Reject(()=>Act(order,LeasingOrderStatus.Approved),"LeasingOrderOverDelivery");
        d=await Read(order); proposed=Clone(d.Order);proposed.Lines[0].Quantity=8;
        await owner.SaveOrderAsync(account,order,proposed,"Valid reduction",Guid.NewGuid());await Act(order,LeasingOrderStatus.Approved);
        Check((await Read(order)).Capacity.Reserved==20000,"approved reduction releases only remaining difference");
        await Buy(f,order,2,9000);d=await Read(order);
        Check(d.Capacity.Used==82000 && d.Capacity.Reserved==0 && LeasingService.FulfillmentKey(d.Order)=="OrderFullyRealized","full lower-price final delivery consumes entire reservation");
        // Separate invoices document a manual acquisition; no order mapping on invoice matches.
        async Task<Guid> Invoice(Guid acquisition, decimal qty, decimal price, Guid? matchItem, Guid? orderId=null, decimal scope=0, bool credit=false, Guid? credited=null)
        {
            var line=new InvoiceDataLine { ReviewId=Guid.NewGuid(),SourceId="1",Description="Invoice item",Quantity=qty,Price=price,BaseQuantity=1,VatPercent=25,VatCategory="S",Net=LeasingOrderCalculator.Money(qty*price) };
            var review=new InvoiceReview { AcquisitionId=acquisition==Guid.Empty?null:acquisition,ContentConfirmed=true,PurchaseDateConfirmed=true,DuplicateOverrideReason="Independent document",
                Data=new(){Kind=credit?LeasingInvoiceKind.CreditNote:LeasingInvoiceKind.Invoice,Number=Guid.NewGuid().ToString(),SupplierName="Supplier",SupplierNumber="",Currency="NOK",Date=new(2028,1,15),Lines=[line]} };
            var amounts=InvoiceCalculator.Calculate(review.Data); review.Data.Net=amounts.Net;review.Data.Vat=amounts.Vat;review.Data.Gross=amounts.Gross;review.Data.Payable=amounts.Gross;review.Data.Taxes=amounts.Taxes;
            if(acquisition!=Guid.Empty) review.AcquisitionRevision=(await admin.GetAsync(account,acquisition,false)).Acquisition!.Revision;
            else { var od=await Read(orderId!.Value);review.NewAcquisition=await Purchase(od.Order.FrameworkId,qty,price);review.NewAcquisition.Items=[]; }
            review.Matches=[new(){ReviewLineId=line.ReviewId,ItemId=matchItem,CreditedLineId=credited}];
            if(orderId.HasValue) { review.OrderDelivery=await Delivery(orderId.Value,scope);review.Matches[0].OrderLineId=(await Read(orderId.Value)).Order.Lines.Single().Id;review.Matches[0].OrderScope=scope; }
            var id=Guid.NewGuid();await using(var db=factory.CreateDbContext()) { db.LeasingInvoices.Add(new(){Id=id,AccountId=account,AcquisitionId=review.AcquisitionId,UploadedByUserId=adminId,UploadedUtc=new Clock().GetUtcNow(),FileName="manual.xml",StorageKey=id.ToString(),FileHash=id.ToString(),MediaType="application/xml",Status=LeasingInvoiceStatus.Review,Processing=LeasingInvoiceProcessing.Ready,ReviewJson=JsonSerializer.Serialize(review),Revision=Guid.NewGuid()});await db.SaveChangesAsync(); }
            var details=await admin.GetInvoiceAsync(account,id);await admin.ApproveInvoiceAsync(account,id,details.Invoice.Revision);return id;
        }
        var item=(await admin.GetAsync(account,aid,false)).Acquisition!.Items.Single().Id;
        var inv1=await Invoice(aid,1.6m,11000,item);await Invoice(aid,2.4m,11000,item);
        d=await Read(order);Check(d.Capacity.Used==82000&&d.Capacity.Reserved==0&&d.Invoices.Sum(x=>x.Net)==44000,"partial invoices document manual realization without double counting");
        var creditSource=(await admin.GetInvoiceAsync(account,inv1)).Invoice.Lines.Single().Id;
        await Invoice(aid,1,11000,item,credit:true,credited:creditSource);
        Check((await Read(order)).Capacity.Used==71000&&(await Read(order)).Capacity.Reserved==0,"credit releases used capacity but never restores reservation");
        var f2=await Frame(200000);var o2=await Order(f2);await Approve(o2);
        var independent=await admin.SaveAcquisitionAsync(account,null,await Purchase(f2,4,10000),"");
        var independentPurchase=(await admin.GetAsync(account,independent,false)).Acquisition!;var ld=await Delivery(o2,4);
        await owner.LinkOrderAcquisitionAsync(account,independent,independentPurchase.Revision,ld);await owner.LinkOrderAcquisitionAsync(account,independent,independentPurchase.Revision,ld);
        Check((await Read(o2)).Capacity.Used==40000&&(await Read(o2)).Capacity.Reserved==60000,"link existing purchase releases only documented scope, idempotently");
        var o3=await Order(f2,1,1);await Approve(o3);
        await Reject(()=>owner.LinkOrderAcquisitionAsync(account,independent,(admin.GetAsync(account,independent,false).GetAwaiter().GetResult()).Acquisition!.Revision,Delivery(o3,1).GetAwaiter().GetResult()),"LeasingOrderOnePurchase");
        await Act(o2,LeasingOrderStatus.Closed);Check((await Read(o2)).Capacity.Used==40000&&(await Read(o2)).Capacity.Reserved==1,"closing remainder preserves acquisitions");
        await Act(o3,LeasingOrderStatus.Cancelled);Check((await Read(o3)).Capacity.Reserved==0,"cancelling releases remainder");
        var f3=await Frame(125000,true);var o4=await Order(f3,1,100000,LeasingOrderMethod.AmountShare);await Approve(o4);
        var imported=await Invoice(Guid.Empty,1,40000,null,o4,.4m);
        var importedDetails=await admin.GetInvoiceAsync(account,imported);var importedAid=importedDetails.Invoice.AcquisitionId!.Value;
        Check((await Read(o4)).Capacity.Used==50000&&(await Read(o4)).Capacity.Reserved==75000,"invoice approval realizes amount share atomically on VAT-inclusive framework");
        await admin.ReverseInvoiceAsync(account,imported,importedDetails.Invoice.Revision,"Wrong purchase");
        d=await Read(o4);Check(d.Capacity.Used==0&&d.Capacity.Reserved==75000&&d.Order.Lines.Single().Unreserved==.4m,"reversal explicitly leaves portion unreserved");
        await admin.SaveAcquisitionAsync(account,null,await Purchase(f3,1,40000),"");
        await Reject(()=>admin.ReopenOrderAsync(account,o4,(Read(o4).GetAwaiter().GetResult()).Order.Revision,"Reopen",Guid.NewGuid()),"LeasingLimitExceeded");
        d=await Read(o4);Check(d.Order.Lines.Single().Unreserved==.4m&&d.Capacity.Reserved==75000,"failed reopening leaves visible unreserved portion");
        var limitChange=Guid.NewGuid();var frame=(await Capacity(f3)).Framework!;
        await admin.ProposeLimitAsync(account,f3,frame.Revision,200000,"More capacity","ref-001",limitChange);
        Check((await Capacity(f3)).Framework!.Limit==125000,"limit proposal has no effect until approval");
        await admin.ApproveLimitAsync(account,limitChange);await admin.ApproveLimitAsync(account,limitChange);
        await admin.ReopenOrderAsync(account,o4,(await Read(o4)).Order.Revision,"Approved reopening",Guid.NewGuid());
        Check((await Read(o4)).Capacity.Reserved==125000 && LeasingService.ApprovedOrderValue(await Read(o4))==125000,"explicit reopening restores only reversed scope without inflating approved value");
        var reduce=Guid.NewGuid();frame=(await Capacity(f3)).Framework!;
        await admin.ProposeLimitAsync(account,f3,frame.Revision,174999,"Too small",null,reduce);await Reject(()=>admin.ApproveLimitAsync(account,reduce),"LeasingLimitExceeded");
        await Deny(()=>owner.ProposeLimitAsync(account,f3,frame.Revision,300000,"Forbidden",null,Guid.NewGuid()));
        var expired=await Frame(100,end:new(2027,12,31));var expiredOrder=await Order(expired,1,100);await Act(expiredOrder,LeasingOrderStatus.Pending);await Reject(()=>Act(expiredOrder,LeasingOrderStatus.Approved),"LeasingOutsidePeriod");
        await admin.SaveAcquisitionAsync(account,null,await Purchase(expired,1,100),"");Check((await Capacity(expired)).Used==100,"late registration of in-period purchase remains allowed without order");
        // Concurrent approvals contend with both other reservations and ordinary purchases.
        var cf=await Frame(100);var co1=await Order(cf,1,60);var co2=await Order(cf,1,60);await Act(co1,LeasingOrderStatus.Pending);await Act(co2,LeasingOrderStatus.Pending);
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Race(Func<Task> action) { await gate.Task;try { await action();return true; }catch(LeasingValidationException ex) when(ex.Message=="LeasingLimitExceeded") {return false;} }
        var race=new[]{Race(()=>Act(co1,LeasingOrderStatus.Approved)),Race(()=>Act(co2,LeasingOrderStatus.Approved)),Race(async()=>{await admin.SaveAcquisitionAsync(account,null,await Purchase(cf,1,60),"");})};gate.SetResult();var winners=await Task.WhenAll(race);
        Check(winners.Count(x=>x)==1 && (await Capacity(cf)).Used+(await Capacity(cf)).Reserved==60,"concurrent order and acquisition approvals cannot oversubscribe");
        // Rounding and proportional distribution suggestions.
        var small=new LeasingOrder {Status=LeasingOrderStatus.Approved,Lines=[new(){Quantity=3,UnitPrice=.01m,VatPercent=25}]};var smallLine=small.Lines[0];smallLine.Fulfilled=2;Check(LeasingOrderCalculator.Remaining(smallLine).Net==.01m,"cumulative cent rounding");smallLine.Fulfilled=3;Check(LeasingOrderCalculator.Reserved(small,true)==0,"final tiny delivery leaves no rounding residue");
        var allocation=new LeasingClassificationInput {Mode=LeasingAllocationMode.Quantity,Rows=[new(){InputValue=6},new(){InputValue=4}]};
        var template=new LeasingOrderLine {ClassificationJson=JsonSerializer.Serialize(allocation)};
        var scaled=LeasingService.DeliveryClassification(template,new(){Quantity=4,UnitPrice=11000});Check(scaled.Rows[0].InputValue==2.4m&&scaled.Rows.Sum(x=>x.InputValue)==4,"partial delivery scales quantity distribution");
        allocation.Mode=LeasingAllocationMode.NetAmount;allocation.Rows[0].InputValue=60000;allocation.Rows[1].InputValue=40000;template.ClassificationJson=JsonSerializer.Serialize(allocation);
        scaled=LeasingService.DeliveryClassification(template,new(){Quantity=4,UnitPrice=11000});Check(scaled.Rows[0].InputValue==26400&&scaled.Rows.Sum(x=>x.InputValue)==44000,"partial delivery scales net distributions to actual price");
        d=await Read(order);await owner.UploadOrderAsync(account,order,d.Order.Revision,"order.pdf",new MemoryStream([1,2,3]));var doc=(await Read(order)).Documents.Single();await Deny(()=>outsider.DownloadAsync(account,doc.Id));
        Check((await Read(order)).History.Any(x=>x.Action=="Realized")&&(await Read(order)).History.Any(x=>x.Action=="Approved"),"approvals, changes, documents and realizations remain auditable");
        var rejected=await Order(f2,1,10);await Act(rejected,LeasingOrderStatus.Pending);await Act(rejected,LeasingOrderStatus.Rejected);
        Check((await Read(rejected)).Capacity.Reserved==0,"rejection never creates reservation");
        var reviewFrame=await Frame(1000);var reviewOrder=await Order(reviewFrame,1,100);await Approve(reviewOrder);
        var reviewEdit=(await Capacity(reviewFrame)).Framework!;reviewEdit.AcquisitionTo=new(2027,12,31);await admin.SaveFrameworkAsync(account,reviewFrame,reviewEdit,"Period ended");
        Check((await Read(reviewOrder)).NeedsReview&&(await Read(reviewOrder)).Capacity.Reserved==100,"expiry marks review without automatically releasing reservation");
        await Buy(reviewFrame,reviewOrder,1,100);Check((await Read(reviewOrder)).Capacity.Used==100,"existing reservation can realize historical in-period purchase after expiry");
        var exactFrame=await Frame(100);var exactOrder=await Order(exactFrame,1,100);await Approve(exactOrder);
        Check((await Read(exactOrder)).Capacity.Available==0,"approval at exact capacity succeeds");
        var increase=Clone((await Read(exactOrder)).Order);increase.Lines.Single().Quantity=2;
        await owner.SaveOrderAsync(account,exactOrder,increase,"Increase pending",Guid.NewGuid());
        await Reject(()=>Act(exactOrder,LeasingOrderStatus.Approved),"LeasingLimitExceeded");
        Check((await Read(exactOrder)).Capacity.Reserved==100,"failed increase approval keeps original reservation");
        await Act(exactOrder,LeasingOrderStatus.Rejected);Check((await Read(exactOrder)).Order.Status==LeasingOrderStatus.Approved,"reject amendment preserves approved order status");
        var invalidReference=Clone((await Read(exactOrder)).Order);invalidReference.SupplierOrganizationId=oldParty;
        await Reject(()=>owner.SaveOrderAsync(account,exactOrder,invalidReference,"Wrong tenant supplier",Guid.NewGuid()),"LeasingInvalidParty");
        // New permissions are independent, and an order owner need not own the framework.
        var owned=await Order(f2,1,10);var ownedEdit=(await Read(owned)).Order;ownedEdit.OwnerUserId=outsiderId;
        await admin.SaveOrderAsync(account,owned,ownedEdit,"Assign order owner",Guid.NewGuid());await Approve(owned);
        var ownerPurchase=await outsider.NewOrderAcquisitionAsync(account,owned);ownerPurchase.Status=LeasingAcquisitionStatus.Registered;ownerPurchase.FinancedAmount=12.5m;ownerPurchase.Items=[new(){Description="Assigned order",Quantity=1,UnitPrice=10}];
        await outsider.SaveAcquisitionAsync(account,null,ownerPurchase,"",delivery:await Delivery(owned,1));
        Check((await Read(owned)).Order.Lines.Single().Fulfilled==1,"assigned order owner registers delivery without gaining framework edit rights");
        await using(var db=factory.CreateDbContext()) { var membership=await db.UserAccounts.SingleAsync(x=>x.AccountId==account&&x.UserId==outsiderId); db.UserAccountRoles.Add(new(){Id=Guid.NewGuid(),UserAccountId=membership.Id,Role=UserRole.LeasingOrderApprover});await db.SaveChangesAsync(); }
        var permissionOrder=await Order(f2,1,10);await Act(permissionOrder,LeasingOrderStatus.Pending);
        await outsider.OrderActionAsync(account,permissionOrder,(await Read(permissionOrder)).Order.Revision,LeasingOrderStatus.Approved,"Explicit approval role",Guid.NewGuid());
        await Deny(()=>outsider.ProposeLimitAsync(account,f2,(Capacity(f2).GetAwaiter().GetResult()).Framework!.Revision,300000,"Different permission",null,Guid.NewGuid()));
        Check((await outsider.GetOrderAsync(account,permissionOrder)).Order.Status==LeasingOrderStatus.Approved,"order approval role works independently of limit permission");
        var readOnly=(await outsider.GetAsync(account,aid,false)).Acquisition!;await outsider.GetInvoiceAsync(account,inv1);
        await Deny(()=>outsider.SaveAcquisitionAsync(account,aid,readOnly,"Approval is not purchase edit permission"));
        await OrderComponentChecks.Run(admin,account,adminId,o4,f3);
        var mixedFrame=await Frame(1000);var mixedOrder=await Order(mixedFrame,1,100);await Approve(mixedOrder);
        var mixedPurchase=await Purchase(mixedFrame,1,100);mixedPurchase.Items.Add(new(){Description="Unrelated",Quantity=1,UnitPrice=50});
        var mixedAid=await admin.SaveAcquisitionAsync(account,null,mixedPurchase,"",delivery:await Delivery(mixedOrder,1));
        var mixedSaved=(await admin.GetAsync(account,mixedAid,false)).Acquisition!;
        await Invoice(mixedAid,1,50,mixedSaved.Items.Single(x=>x.Position==1).Id);
        Check((await Read(mixedOrder)).DocumentedNet==0,"order documented amount excludes unrelated items on linked acquisitions");
        await Invoice(mixedAid,1,100,mixedSaved.Items.Single(x=>x.Position==0).Id);
        Check((await Read(mixedOrder)).DocumentedNet==100,"order documented amount includes only linked item invoices");
        var shareFrame=await Frame(200);var shareOrder=await Order(shareFrame,1,100,LeasingOrderMethod.AmountShare);await Approve(shareOrder);
        await Buy(shareFrame,shareOrder,1,40,.4m);await Buy(shareFrame,shareOrder,1,50,.6m);
        Check((await Read(shareOrder)).Capacity.Used==90&&(await Read(shareOrder)).Capacity.Reserved==0,"full amount-share realization releases all approved reservation despite lower actual value");
        var dimensionContext=new Context(adminId,account);
        var dimensions=new TenantPlatform.Web.Services.Dimensions.DimensionService(factory,dimensionContext,new TenantAuthorizationService(factory,dimensionContext),new Clock());
        var dimension=await dimensions.SaveDimensionAsync(null,new(){Name="Department",Code="D"});
        var valueA=await dimensions.SaveValueAsync(dimension,null,new(){DimensionId=dimension,Name="A",Code="A"});
        var valueB=await dimensions.SaveValueAsync(dimension,null,new(){DimensionId=dimension,Name="B",Code="B"});
        await admin.SaveDimensionRuleAsync(account,new(){DimensionId=dimension,IsEnabled=true,IsRequired=true,AllowAllocation=true});
        var df=await Frame(1000);var incompleteOrder=await Order(df,10,10);await Approve(incompleteOrder);
        await Reject(()=>Buy(df,incompleteOrder,4,10),"LeasingMissingDimensions");
        var dimOrder=(await Read(incompleteOrder)).Order;
        var dimInput=new LeasingClassificationInput {Mode=LeasingAllocationMode.Quantity,VaryingDimensions=[dimension],Rows=[new(){InputValue=6,Choices=[new(dimension,valueA)]},new(){InputValue=4,Choices=[new(dimension,valueB)]}]};
        dimOrder.Lines.Single().ClassificationJson=JsonSerializer.Serialize(dimInput);
        await owner.SaveOrderAsync(account,incompleteOrder,dimOrder,"Add allocation",Guid.NewGuid());await Act(incompleteOrder,LeasingOrderStatus.Approved);
        dimOrder=(await Read(incompleteOrder)).Order;var dimPurchase=await Purchase(df,4,11);
        var dimSuggestion=LeasingService.DeliveryClassification(dimOrder.Lines.Single(),dimPurchase.Items.Single());
        var dimAid=await owner.SaveAcquisitionAsync(account,null,dimPurchase,"",classifications:new Dictionary<int,LeasingClassificationInput>{{0,dimSuggestion}},delivery:await Delivery(incompleteOrder,4));
        var persisted=(await owner.GetAsync(account,dimAid,false)).Acquisition!;
        Check(persisted.Items.Single().AllocationRows.Sum(x=>x.InputValue)==4&&persisted.Items.Single().DimensionSelections.Count==2,"partial delivery validates and persists scaled required dimensions");
        dimOrder=(await Read(incompleteOrder)).Order;dimOrder.Lines.Single().ClassificationJson="{}";
        await owner.SaveOrderAsync(account,incompleteOrder,dimOrder,"Incomplete remaining dimensions",Guid.NewGuid());await Act(incompleteOrder,LeasingOrderStatus.Approved);
        Check((await owner.GetAsync(account,dimAid,false)).Acquisition!.Items.Single().DimensionSelections.Count==2,"order amendments do not rewrite acquisition dimensions");
        Console.WriteLine("All phase-four order checks passed.");
    }
    private static LeasingOrder Clone(LeasingOrder order)=>JsonSerializer.Deserialize<LeasingOrder>(JsonSerializer.Serialize(order))!;
    private static void Check(bool condition,string message) {if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private static async Task Reject(Func<Task> action,string key) {try{await action();throw new Exception("Expected "+key);}catch(LeasingValidationException e)when(e.Message==key){Console.WriteLine("PASS rejection: "+key);}}
    private static async Task Deny(Func<Task> action) {try{await action();throw new Exception("Expected access denial");}catch(UnauthorizedAccessException){Console.WriteLine("PASS: order access denied");}}
}
