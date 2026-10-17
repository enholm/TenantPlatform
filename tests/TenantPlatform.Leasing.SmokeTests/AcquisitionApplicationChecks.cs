using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Leasing;
using TenantPlatform.Infrastructure.Persistence;
static class AcquisitionApplicationChecks
{
    public static async Task Run(Factory factory, Guid admin, Guid owner, Guid outsider, Guid foreign)
    {
        var account = Guid.NewGuid(); var party = Guid.NewGuid(); var frame = Guid.NewGuid(); var order = Guid.NewGuid(); var orderLine = Guid.NewGuid(); var membership = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.Add(new() { Id = account, Name = "Direct acquisition tests" });
            db.Organizations.Add(new() { Id = party, AccountId = account, Name = "Supplier and finance" });
            db.UserAccounts.AddRange(new() { Id = membership, AccountId = account, UserId = owner }, new() { Id = Guid.NewGuid(), AccountId = account, UserId = outsider });
            var m = new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = admin }; db.UserAccounts.Add(m); db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = m.Id, Role = UserRole.AccountAdmin });
            db.LeasingFrameworks.Add(new() { Id = frame, AccountId = account, Name = "Bounded", Number = "ACQUISITION", FinanceOrganizationId = party, OwnerUserId = owner, Limit = 50, AcquisitionFrom = new(2028,1,1), AcquisitionTo = new(2028,12,31), Revision = Guid.NewGuid() });
            db.LeasingOrders.Add(new() { Id = order, AccountId = account, FrameworkId = frame, OwnerUserId = owner, SupplierOrganizationId = party, Number = "B-DIRECT", OrderDate = new(2028,1,1), Status = LeasingOrderStatus.Approved, Revision = Guid.NewGuid(), Lines = [new() { Id = orderLine, AccountId = account, OrderId = order, Description = "Direct order", Quantity = 2, UnitPrice = 10, VatPercent = 25 }] });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, owner, account)); var clock = new Clock();
        var auth = new AuthorizationService(actor, new AuthorizationData(factory), clock);
        var service = new LeasingAcquisitionWriter(auth, new LeasingAcquisitionData(factory), clock);
        var orderWriter = new LeasingOrderWriter(auth, new LeasingOrderWriteData(factory), clock);
        var equipment = new LeasingEquipmentService(auth, new LeasingEquipmentData(factory), clock);
        var guard = new SaveGuard(); DbContextOptions<TenantPlatformDbContext> options;
        await using (var db = factory.CreateDbContext()) options = new DbContextOptionsBuilder<TenantPlatformDbContext>((DbContextOptions<TenantPlatformDbContext>)db.GetService<IDbContextOptions>()).AddInterceptors(guard).Options;
        var readFactory = new Factory(options);
        var equipmentReader = new LeasingEquipmentService(auth, new LeasingEquipmentData(readFactory), clock);
        var orderReader = new LeasingOrderReader(auth, new LeasingOrderReadData(readFactory), new LeasingReadData(readFactory), clock);
        guard.Enabled = true;
        Check((await orderReader.ListOrdersAsync(account)).Single().Id == order && (await orderReader.GetOrderAsync(account, order)).CanEdit, "owner reads order within SQL scope without writes");
        Check((await orderReader.NewOrderAcquisitionAsync(account, order)).OwnerUserId == owner && !(await orderReader.CanChangeLimitAsync(account)), "order acquisition suggestion uses trusted actor and does not grant limit approval");
        Check((await orderReader.LimitChangesAsync(account, frame)).Count == 0, "owner can read limit history without settings initialization");
        actor.Current = new(true, outsider, account);
        Check((await orderReader.ListOrdersAsync(account)).Count == 0, "unrelated member sees no order rows");
        await Denied(() => orderReader.GetOrderAsync(account, order)); await Denied(() => orderReader.NewOrderAcquisitionAsync(account, order)); await Denied(() => orderReader.LimitChangesAsync(account, frame));
        actor.Current = new(true, owner, account); guard.Enabled = false;
        var draft = new LeasingOrder { FrameworkId = frame, OwnerUserId = owner, SupplierOrganizationId = party, Currency = "NOK", OrderDate = new(2028,1,15), Lines = [new() { Description = "Direct draft", Quantity = 1, UnitPrice = 5, VatPercent = 25 }] };
        var draftRequest = Guid.NewGuid(); var draftId = await orderWriter.SaveOrderAsync(account, null, draft, "", draftRequest);
        Check(await orderWriter.SaveOrderAsync(account, null, draft, "", draftRequest) == draftId, "direct order creation retry preserves one ID");
        var draftState = (await orderReader.GetOrderAsync(account, draftId)).Order;
        var badOrder = new LeasingOrder { FrameworkId = frame, OwnerUserId = owner, SupplierOrganizationId = Guid.NewGuid(), Currency = "NOK", OrderDate = new(2028,1,15), Lines = [new() { Description = "Bad reference", Quantity = 1, UnitPrice = 1 }] };
        await Invalid(() => orderWriter.SaveOrderAsync(account, null, badOrder, "", Guid.NewGuid()), "LeasingInvalidParty");
        draftState.OwnerUserId = outsider; await Denied(() => orderWriter.SaveOrderAsync(account, draftId, draftState, "Unauthorized transfer", Guid.NewGuid()));
        draftState = (await orderReader.GetOrderAsync(account, draftId)).Order;
        await orderWriter.OrderActionAsync(account, draftId, draftState.Revision, LeasingOrderStatus.Pending, "Submit direct", Guid.NewGuid());
        draftState = (await orderReader.GetOrderAsync(account, draftId)).Order;
        await Denied(() => orderWriter.OrderActionAsync(account, draftId, draftState.Revision, LeasingOrderStatus.Approved, "Owner has no approval role", Guid.NewGuid()));
        actor.Current = new(true, admin, account); var approveRequest = Guid.NewGuid();
        await orderWriter.OrderActionAsync(account, draftId, draftState.Revision, LeasingOrderStatus.Approved, "Approve direct", approveRequest);
        await orderWriter.OrderActionAsync(account, draftId, draftState.Revision, LeasingOrderStatus.Approved, "Retry direct", approveRequest);
        actor.Current = new(true, owner, account);
        draftState = (await orderReader.GetOrderAsync(account, draftId)).Order; draftState.Lines.Single().Quantity = 2;
        await orderWriter.SaveOrderAsync(account, draftId, draftState, "Propose changed lines", Guid.NewGuid());
        var proposed = (await orderReader.GetOrderAsync(account, draftId)).Order;
        Check(proposed.ProposalJson is not null && proposed.Lines.Single().Quantity == 1, "approved order changes remain proposals until approval");
        actor.Current = new(true, admin, account);
        await orderWriter.OrderActionAsync(account, draftId, proposed.Revision, LeasingOrderStatus.Approved, "Approve changed lines", Guid.NewGuid());
        actor.Current = new(true, owner, account); draftState = (await orderReader.GetOrderAsync(account, draftId)).Order;
        await orderWriter.OrderActionAsync(account, draftId, draftState.Revision, LeasingOrderStatus.Closed, "Close direct", Guid.NewGuid());
        Guid frameRevision; await using (var db = factory.CreateDbContext()) frameRevision = await db.LeasingFrameworks.Where(x => x.Id == frame).Select(x => x.Revision).SingleAsync();
        var limitRequest = Guid.NewGuid(); await Denied(() => orderWriter.ProposeLimitAsync(account, frame, frameRevision, 60, "Owner cannot approve limit", null, limitRequest));
        actor.Current = new(true, admin, account);
        Check(await orderReader.CanChangeLimitAsync(account), "direct limit authority comes from fresh account-wide assignment");
        await orderWriter.ProposeLimitAsync(account, frame, frameRevision, 60, "Raise limit", null, limitRequest);
        await orderWriter.ProposeLimitAsync(account, frame, frameRevision, 60, "Retry proposal", null, limitRequest);
        await orderWriter.ApproveLimitAsync(account, limitRequest); await orderWriter.ApproveLimitAsync(account, limitRequest);
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingFrameworks.AnyAsync(x => x.Id == frame && x.Limit == 60) && await db.LeasingLimitChanges.CountAsync(x => x.AccountId == account && x.Id == limitRequest) == 1 && await db.LeasingHistory.CountAsync(x => x.AccountId == account && x.Action == "LimitApproved" && x.ActorUserId == admin) == 1, "limit proposal/approval is atomic, audited and idempotent");
        actor.Current = new(true, owner, account);
        LeasingAcquisition Input(decimal price = 10) => new() { AccountId = foreign, Name = "Direct purchase", Reference = Guid.NewGuid().ToString("N"), FrameworkId = frame, FinanceOrganizationId = party, SupplierOrganizationId = party, OwnerUserId = owner, PurchaseDate = new(2028,2,1), FinancedAmount = 10, Status = LeasingAcquisitionStatus.Registered, Items = [new() { Description = "Unit", Quantity = 2, UnitPrice = price, VatPercent = 25 }] };
        async Task<LeasingAcquisition> Read(Guid id) { await using var db = factory.CreateDbContext(); return await db.LeasingAcquisitions.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.AccountId == account && x.Id == id); }
        var first = await service.SaveAcquisitionAsync(account, null, Input(), "");
        var saved = await Read(first);
        Check(saved.AccountId == account && saved.Items.All(x => x.AccountId == account), "client account is ignored on parent and items");
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingFinancingRevisions.CountAsync(x => x.AccountId == account && x.AcquisitionId == first && x.ActorUserId == owner) == 1 && await db.LeasingHistory.AnyAsync(x => x.AccountId == account && x.AcquisitionId == first && x.ActorUserId == owner), "first financing and history use trusted actor in the same transaction");
        var itemId = saved.Items.Single().Id;
        await Denied(() => equipment.SetCountableAsync(account, first, itemId, true, "Owner without equipment role"));
        actor.Current = new(true, admin, account);
        await equipment.SetCountableAsync(account, first, itemId, true, "Explicit equipment registration");
        var unitInput = new LeasingEquipment { AccountId = foreign, AcquisitionId = Guid.NewGuid(), ItemId = itemId, Description = "Direct unit", RegisteredDate = new(2028,1,15), InternalId = " direct-1 ", SerialNumber = "serial-1", OwnerUserId = owner };
        var equipmentRequest = Guid.NewGuid();
        var unitIds = await equipment.SaveEquipmentAsync(account, first, [unitInput], false, "Direct equipment", equipmentRequest);
        Check((await equipment.SaveEquipmentAsync(account, first, [unitInput], false, "Retry", equipmentRequest)).SequenceEqual(unitIds), "direct equipment request is idempotent");
        await Invalid(() => equipment.SetCountableAsync(account, first, itemId, false, "Cannot remove tracking"), "LifeCountableRequired");
        var badUnit = new LeasingEquipment { ItemId = itemId, Description = "Foreign building", RegisteredDate = new(2028,1,15), BuildingId = Guid.NewGuid() };
        await Denied(() => equipment.SaveEquipmentAsync(account, first, [badUnit], false, "Foreign reference", Guid.NewGuid()));
        var duplicate = new LeasingEquipment { ItemId = itemId, Description = "Duplicate serial", RegisteredDate = new(2028,1,15), SerialNumber = "SERIAL-1" };
        await Invalid(() => equipment.SaveEquipmentAsync(account, first, [duplicate], false, "No duplicate confirmation", Guid.NewGuid()), "LifeDuplicateSerial");
        var excess = new List<LeasingEquipment> { new() { ItemId = itemId, Description = "Second", RegisteredDate = new(2028,1,15) }, new() { ItemId = itemId, Description = "Third", RegisteredDate = new(2028,1,15) } };
        await Invalid(() => equipment.SaveEquipmentAsync(account, first, excess, true, "Over quantity", Guid.NewGuid()), "LifeOverQuantity");
        actor.Current = new(true, owner, account); guard.Enabled = true;
        var unitsPage = await equipmentReader.ListEquipmentAsync(account);
        Check(unitsPage.Rows.Single().AccountId == account && unitsPage.Rows.Single().AcquisitionId == first && unitsPage.Rows.Single().InternalId == "DIRECT-1", "equipment uses server parent/account and normalized identifier in a write-free scoped page");
        await equipmentReader.EquipmentBuildingsAsync(account);
        actor.Current = new(true, outsider, account);
        Check((await equipmentReader.ListEquipmentAsync(account)).Count == 0, "unrelated member sees no equipment rows or acquisition names");
        await Denied(() => equipment.SaveEquipmentAsync(account, first, [unitInput], false, "Unrelated actor", Guid.NewGuid()));
        actor.Current = new(true, owner, account); guard.Enabled = false;
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingEquipment.CountAsync(x => x.AccountId == account) == 1 && await db.LeasingPaymentEvents.AnyAsync(x => x.AccountId == account && x.Id == equipmentRequest && x.ActorUserId == admin), "failed equipment batches roll back and successful history records trusted actor");
        var transfer = await Read(first); transfer.OwnerUserId = outsider;
        await Denied(() => service.SaveAcquisitionAsync(account, first, transfer, "Owner transfer"));
        var bad = Input(); bad.FrameworkId = Guid.NewGuid(); await Denied(() => service.SaveAcquisitionAsync(account, null, bad, "Foreign framework"));
        bad = Input(); bad.SupplierOrganizationId = Guid.NewGuid(); await Invalid(() => service.SaveAcquisitionAsync(account, null, bad, "Foreign supplier"), "LeasingInvalidParty");
        bad = Input(); bad.OwnerUserId = Guid.NewGuid(); await Invalid(() => service.SaveAcquisitionAsync(account, null, bad, "Foreign owner"), "LeasingInvalidOwner");
        var standalone = Input(); standalone.FrameworkId = null; await Denied(() => service.SaveAcquisitionAsync(account, null, standalone, "Owner cannot create standalone"));
        var counts = await Counts(); await Invalid(() => service.SaveAcquisitionAsync(account, null, Input(30), "Over limit"), "LeasingLimitExceeded");
        Check((await Counts()).SequenceEqual(counts), "late capacity failure rolls back acquisition, financing and history atomically");
        actor.Current = new(true, outsider, account);
        await Denied(() => service.SaveAcquisitionAsync(account, first, saved, "Other owner")); await Denied(() => service.CancelAsync(account, first, saved.Revision, "Other owner"));
        actor.Current = new(true, owner, foreign); await Denied(() => service.SaveAcquisitionAsync(account, null, Input(), "Wrong account"));
        actor.Current = new(false, owner, account); await Denied(() => service.CancelAsync(account, first, saved.Revision, "Anonymous"));
        actor.Current = new(true, owner, account);
        var paused = new LeasingAcquisitionWriter(auth, new AfterLock(new LeasingAcquisitionData(factory), () => actor.Current = new(false, owner, account)), clock);
        await Denied(() => paused.SaveAcquisitionAsync(account, null, Input(), "Revoked after lock")); actor.Current = new(true, owner, account);
        Guid orderRevision; await using (var db = factory.CreateDbContext()) orderRevision = await db.LeasingOrders.Where(x => x.Id == order).Select(x => x.Revision).SingleAsync();
        var delivery = new OrderDelivery { OrderId = order, Revision = orderRevision, RequestId = Guid.NewGuid(), Lines = [new() { OrderLineId = orderLine, ItemPosition = 0, Scope = 2 }] };
        var deliveries = await Task.WhenAll(service.SaveAcquisitionAsync(account, null, Input(), "", delivery: delivery), service.SaveAcquisitionAsync(account, null, Input(), "", delivery: delivery));
        Check(deliveries[0] == deliveries[1], "concurrent direct delivery retries return one acquisition");
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingOrderRealizations.CountAsync(x => x.AccountId == account && x.RequestId == delivery.RequestId) == 1 && await db.LeasingOrderEvents.CountAsync(x => x.AccountId == account && x.Id == delivery.RequestId) == 1, "delivery realization and event are idempotent");
        saved = await Read(first); saved.Name = "Direct update"; await service.SaveAcquisitionAsync(account, first, saved, "Owner update");
        await Invalid(() => service.CancelAsync(account, first, saved.Revision, "Stale cancel"), "LeasingConcurrency");
        saved = await Read(deliveries[0]); await service.CancelAsync(account, saved.Id, saved.Revision, "Direct cancel delivery");
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingOrderRealizations.AnyAsync(x => x.AccountId == account && x.AcquisitionId == saved.Id && x.Reversed) && await db.LeasingOrderLines.AnyAsync(x => x.Id == orderLine && x.Unreserved == 2 && x.Fulfilled == 0), "cancellation reverses consumption without reopening reservation");
        var reopen = (await orderReader.GetOrderAsync(account, order)).Order;
        await Denied(() => orderWriter.ReopenOrderAsync(account, order, reopen.Revision, "Owner cannot reopen", Guid.NewGuid()));
        actor.Current = new(true, admin, account); var reopenRequest = Guid.NewGuid();
        await orderWriter.ReopenOrderAsync(account, order, reopen.Revision, "Reopen explicitly", reopenRequest);
        await orderWriter.ReopenOrderAsync(account, order, reopen.Revision, "Retry reopen", reopenRequest);
        actor.Current = new(true, owner, account);
        var linkOrder = Guid.NewGuid(); var linkLine = Guid.NewGuid(); var linkRevision = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.LeasingOrders.Add(new() { Id = linkOrder, AccountId = account, FrameworkId = frame, OwnerUserId = owner, SupplierOrganizationId = party, Number = "B-DIRECT-LINK", OrderDate = new(2028,1,1), Status = LeasingOrderStatus.Approved, Revision = linkRevision, Lines = [new() { Id = linkLine, AccountId = account, OrderId = linkOrder, Description = "Link target", Quantity = 2, UnitPrice = 5, VatPercent = 25 }] }); await db.SaveChangesAsync();
        }
        saved = await Read(first);
        var link = new OrderDelivery { OrderId = linkOrder, Revision = linkRevision, RequestId = Guid.NewGuid(), VarianceConfirmed = true, Lines = [new() { OrderLineId = linkLine, ItemPosition = 0, Scope = 2 }] };
        actor.Current = new(true, outsider, account); await Denied(() => service.LinkOrderAcquisitionAsync(account, first, saved.Revision, link));
        actor.Current = new(true, owner, account);
        await service.LinkOrderAcquisitionAsync(account, first, saved.Revision, link);
        await service.LinkOrderAcquisitionAsync(account, first, saved.Revision, link);
        guard.Enabled = true;
        Check((await orderReader.AcquisitionOrdersAsync(account, first)).Single().Id == linkOrder && (await orderReader.OrderLinkCandidatesAsync(account, linkOrder)).Any(x => x.Id == first), "direct linking is idempotent and reads only authorized candidates/realizations");
        guard.Enabled = false;
        await using (var db = factory.CreateDbContext()) { var m = await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == outsider); db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = m.Id, Role = UserRole.LeasingOrderApprover }); await db.SaveChangesAsync(); }
        actor.Current = new(true, outsider, account); guard.Enabled = true;
        var approver = await orderReader.GetOrderAsync(account, linkOrder);
        Check(approver.CanApprove && !approver.CanEdit && (await orderReader.ListOrdersAsync(account)).Count == 3, "order approver has read/approval flag without owner edit flag");
        await Denied(() => orderReader.NewOrderAcquisitionAsync(account, linkOrder));
        Check((await orderReader.OrderLinkCandidatesAsync(account, linkOrder)).Count == 0 && (await orderReader.AcquisitionOrdersAsync(account, first)).Count == 1, "order relationship grants read without broadening owned link candidates");
        guard.Enabled = false;
        await using (var db = factory.CreateDbContext()) { var m = await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == outsider); db.UserAccountRoles.RemoveRange(db.UserAccountRoles.Where(x => x.UserAccountId == m.Id)); await db.SaveChangesAsync(); }
        await Denied(() => orderReader.GetOrderAsync(account, linkOrder));
        await using (var db = factory.CreateDbContext()) { var u = await db.Users.SingleAsync(x => x.Id == outsider); u.IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => orderReader.ListOrdersAsync(account));
        await Denied(() => orderWriter.SaveOrderAsync(account, null, draft, "Inactive", Guid.NewGuid()));
        await using (var db = factory.CreateDbContext()) { var u = await db.Users.SingleAsync(x => x.Id == outsider); u.IsActive = true; await db.SaveChangesAsync(); }
        actor.Current = new(true, owner, account);
        saved = await Read(first);
        await using (var db = factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == membership)); await db.SaveChangesAsync(); }
        await Denied(() => service.CancelAsync(account, first, saved.Revision, "Revoked member"));
        await Denied(() => service.SaveAcquisitionAsync(account, first, saved, "Revoked member"));
        await Denied(() => orderReader.GetOrderAsync(account, order));
        await Denied(() => orderReader.ListOrdersAsync(account));
        await Denied(() => orderWriter.SaveOrderAsync(account, null, draft, "Revoked", Guid.NewGuid()));
        await Denied(() => equipment.ListEquipmentAsync(account));
        await Denied(() => equipment.EquipmentBuildingsAsync(account));
        Check(true, "direct acquisition calls revalidate membership and active trusted identity");
        async Task<int[]> Counts() { await using var db = factory.CreateDbContext(); return [await db.LeasingAcquisitions.CountAsync(x => x.AccountId == account), await db.LeasingFinancingRevisions.CountAsync(x => x.AccountId == account), await db.LeasingHistory.CountAsync(x => x.AccountId == account)]; }
    }
    private sealed class SaveGuard : SaveChangesInterceptor { public bool Enabled; public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) => Enabled ? throw new Exception("Order reader attempted SaveChanges") : base.SavingChangesAsync(data, result, ct); }
    private sealed class Actor(ActorSnapshot value) : ICurrentActor { public ActorSnapshot Current = value; public ActorSnapshot GetSnapshot() => Current; }
    private sealed class AfterLock(ILeasingAcquisitionData data, Action action) : ILeasingAcquisitionData { public async Task<ILeasingAcquisitionWork> BeginAsync(Guid account, CancellationToken ct) { var work = await data.BeginAsync(account, ct); action(); return work; } }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL direct acquisition: " + name); Console.WriteLine("PASS direct acquisition: " + name); }
    private static async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected access denial"); } catch (UnauthorizedAccessException) { } }
    private static async Task Invalid(Func<Task> action, string key) { try { await action(); throw new Exception("Expected " + key); } catch (LeasingValidationException ex) when (ex.Message == key) { } }
}
