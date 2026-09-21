using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Agreements;

static class BasisApprovalChecks
{
    public static async Task Run(IDbContextFactory<TenantPlatformDbContext> factory, AgreementService admin, AgreementService owner,
        AgreementService editor, AgreementService foreign, Guid account, Guid otherAccount, Guid ownerId, Guid editorId,
        Guid foreignId, Guid foreignOrg, TestClock clock)
    {
        var from = new DateOnly(2027, 1, 1); var to = new DateOnly(2027, 1, 31);
        int outboxBefore;
        await using (var db = await factory.CreateDbContextAsync()) outboxBefore = await db.EmailOutboxMessages.CountAsync();
        var party = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        { db.Organizations.Add(new Organization { Id = party, AccountId = account, Name = "Approval customer" }); await db.SaveChangesAsync(); }
        SaveAgreementRequest Request(string name, AgreementDirection direction = AgreementDirection.Income, string currency = "NOK") =>
            new() { Title = name, Form = AgreementForm.Renewing, RenewalDate = new(2035, 1, 1), StartDate = from,
                OwnerUserId = ownerId, CounterpartyOrganizationId = party, Direction = direction, Currency = currency, Status = AgreementStatus.Active };
        SaveAgreementLineRequest Line() => new() { Name = "Approval line", StartDate = from, FirstPayableDate = from, AnchorDate = from,
            EffectiveFrom = from, UnitPrice = 1000, Activate = true };
        async Task<(Guid Agreement, Guid Line, Guid Basis)> Create(string name, AgreementDirection direction = AgreementDirection.Income, string currency = "NOK")
        {
            var id = await admin.CreateAsync(account, Request(name, direction, currency));
            var line = Line(); line.Revision = (await owner.GetAsync(account, id)).Revision;
            var lineId = await owner.SaveLineAsync(account, id, null, line);
            var basis = (await owner.GenerateBasisAsync(account, id, direction, from, to)).Created.Single();
            return (id, lineId, basis);
        }
        async Task Change(Guid agreement, Guid lineId)
        {
            var line = Line(); line.Revision = (await owner.GetAsync(account, agreement)).Revision; line.UnitPrice = 1500; line.Reason = "Changed after review";
            await owner.SaveLineAsync(account, agreement, lineId, line);
        }
        var valid = await Create("Approval valid");
        var stale = await Create("Approval stale"); await Change(stale.Agreement, stale.Line);
        var revised = await Create("Approval revised");
        var cancelled = await Create("Approval cancelled"); await owner.CancelBasisAsync(account, cancelled.Basis, 1, "Cancelled draft");
        var approved = await Create("Approval already approved"); await owner.ApproveBasisAsync(account, approved.Basis, 1);
        var listed = await owner.ListPendingBasisApprovalsAsync(account, new(CounterpartyId: party));
        Require(listed.Items.Any(x => x.BasisId == valid.Basis && x.CanApprove), "pending draft is selectable");
        Require(listed.Items.Single(x => x.BasisId == stale.Basis).Reason == "ProcessingStale", "stale draft visible with reason and disabled");
        Require(!listed.Items.Any(x => x.BasisId == cancelled.Basis || x.BasisId == approved.Basis), "approved and cancelled bases excluded");
        await owner.RegenerateBasisAsync(account, revised.Basis, 1, "New review revision");
        var beforeSnapshot = (await owner.GetBasisAsync(account, valid.Basis)).Snapshots[0].DataJson;
        var time = clock.Now;
        var partial = await owner.ApproveBasesAsync(account, [new(stale.Basis, 1), new(revised.Basis, 1), new(cancelled.Basis, 1), new(valid.Basis, 1)]);
        Require(partial.Take(3).All(x => x.Outcome == AgreementBasisApprovalOutcome.Failed) && partial.Last().Outcome == AgreementBasisApprovalOutcome.Approved,
            "stale revised and cancelled failures do not roll back valid approval");
        var after = await owner.GetBasisAsync(account, valid.Basis);
        Require(after.Basis.Status == AgreementBasisStatus.Approved && after.Basis.ApprovedByUserId == ownerId && after.Basis.ApprovedUtc == time,
            "approval atomically records status actor and time");
        Require(after.Snapshots.Count == 1 && after.Snapshots[0].DataJson == beforeSnapshot && after.Basis.Revision == 1,
            "approval preserves amount revision and historical snapshot");
        await Expect<AgreementValidationException>(() => owner.RegenerateBasisAsync(account, valid.Basis, 1, "Must stay locked"), "approved basis cannot be regenerated");
        clock.Now = clock.Now.AddMinutes(1);
        var agreementRevision = (await owner.GetAsync(account, valid.Agreement)).Revision;
        var repeated = await owner.ApproveBasesAsync(account, [new(valid.Basis, 1), new(valid.Basis, 1)]);
        Require(repeated.Count == 1 && repeated.Single().Outcome == AgreementBasisApprovalOutcome.AlreadyProcessed, "duplicate selection and retries report already processed");
        after = await owner.GetBasisAsync(account, valid.Basis);
        Require(after.Basis.ApprovedUtc == time && (await owner.GetAsync(account, valid.Agreement)).Revision == agreementRevision && after.Snapshots.Count == 1,
            "retry produces no second approval timestamp revision or snapshot");
        var freshRevision = (await owner.ListPendingBasisApprovalsAsync(account, new(AgreementId: revised.Agreement))).Items.Single();
        Require(freshRevision.Revision == 2 && (await owner.ApproveBasesAsync(account, [new(revised.Basis, freshRevision.Revision)])).Single().Outcome == AgreementBasisApprovalOutcome.Approved,
            "newly reviewed revision can be approved");
        Require((await owner.ApproveBasesAsync(account, [new(revised.Basis, 1)])).Single().Outcome == AgreementBasisApprovalOutcome.AlreadyProcessed,
            "basis approved on a newer revision is reported as already processed");
        await Expect<AgreementValidationException>(() => owner.ApproveBasisAsync(account, revised.Basis, 1), "individual approval retains revision semantics");
        var race = await Create("Approval race");
        var concurrent = await Task.WhenAll(owner.ApproveBasesAsync(account, [new(race.Basis, 1)]), admin.ApproveBasesAsync(account, [new(race.Basis, 1)]));
        Require(concurrent.SelectMany(x => x).Count(x => x.Outcome == AgreementBasisApprovalOutcome.Approved) == 1 &&
            concurrent.SelectMany(x => x).Count(x => x.Outcome == AgreementBasisApprovalOutcome.AlreadyProcessed) == 1, "concurrent bulk approvals have one winner");
        var mixedRace = await Create("Approval individual and bulk race");
        var bulkTask = owner.ApproveBasesAsync(account, [new(mixedRace.Basis, 1)]);
        var singleTask = owner.ApproveBasisAsync(account, mixedRace.Basis, 1);
        await Task.WhenAll(bulkTask, singleTask);
        var raceAfter = await owner.GetBasisAsync(account, mixedRace.Basis);
        Require(raceAfter.Basis.Status == AgreementBasisStatus.Approved && raceAfter.Snapshots.Count == 1, "individual and bulk approval share atomic idempotency");
        var permission = await Create("Approval permission");
        await owner.SetAccessAsync(account, permission.Agreement, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account, permission.Agreement)).Revision);
        var editorList = await editor.ListPendingBasisApprovalsAsync(account, new(AgreementId: permission.Agreement));
        Require(!editorList.Items.Single().CanApprove && editorList.Items.Single().Reason == "ApprovalPermissionRequired", "edit permission does not imply approval permission");
        Require((await editor.ApproveBasesAsync(account, [new(permission.Basis, 1)])).Single().Reason == "ApprovalAccessDenied", "server rejects forged approval permission");
        var foreignRequest = Request("Foreign approval"); foreignRequest.OwnerUserId = foreignId; foreignRequest.CounterpartyOrganizationId = foreignOrg;
        var foreignAgreement = await foreign.CreateAsync(otherAccount, foreignRequest);
        var foreignLine = Line(); foreignLine.Revision = (await foreign.GetAsync(otherAccount, foreignAgreement)).Revision;
        await foreign.SaveLineAsync(otherAccount, foreignAgreement, null, foreignLine);
        var foreignBasis = (await foreign.GenerateBasisAsync(otherAccount, foreignAgreement, AgreementDirection.Income, from, to)).Created.Single();
        Require(!(await owner.ListPendingBasisApprovalsAsync(account, new())).Items.Any(x => x.BasisId == foreignBasis), "other account pending bases cannot be read");
        Require((await owner.ApproveBasesAsync(account, [new(foreignBasis, 1)])).Single().Reason == "ApprovalAccessDenied", "other account basis cannot be approved");
        await Expect<UnauthorizedAccessException>(() => owner.ApproveBasesAsync(otherAccount, [new(foreignBasis, 1)]), "client cannot change account boundary");
        var changed = await Create("Approval changed inputs");
        var oldRow = (await owner.ListPendingBasisApprovalsAsync(account, new(AgreementId: changed.Agreement))).Items.Single();
        await Change(changed.Agreement, changed.Line);
        Require((await owner.ApproveBasesAsync(account, [new(changed.Basis, oldRow.Revision)])).Single().Reason == "ProcessingStale", "underlying change after listing prevents approval without hidden regeneration");
        Require((await owner.GetBasisAsync(account, changed.Basis)).Basis.Revision == oldRow.Revision, "failed approval never regenerates");
        var revoked = await Create("Approval access revoked");
        var changeOwner = await owner.GetAsync(account, revoked.Agreement); changeOwner.OwnerUserId = editorId;
        await owner.UpdateAsync(account, revoked.Agreement, changeOwner);
        Require((await owner.ApproveBasesAsync(account, [new(revoked.Basis, 1)])).Single().Reason == "ApprovalAccessDenied", "approval rights are checked again after ownership changes");
        var zero = await Create("Approval zero correction"); await owner.ApproveBasisAsync(account, zero.Basis, 1);
        var shifted = Line(); shifted.StartDate = shifted.FirstPayableDate = from.AddMonths(1); shifted.Reason = "Remove January period";
        shifted.Revision = (await owner.GetAsync(account, zero.Agreement)).Revision;
        await owner.SaveLineAsync(account, zero.Agreement, zero.Line, shifted);
        var correction = await owner.CreateCorrectionAsync(account, zero.Basis, "Reverse original January amount");
        var correctionRow = (await owner.ListPendingBasisApprovalsAsync(account, new(AgreementId: zero.Agreement))).Items.Single();
        Require(correctionRow.IsCorrection && correctionRow.Amount == -1000 && correctionRow.PeriodFrom == from && correctionRow.PeriodTo == to,
            "negative correction retains original period when current segments are empty");
        Require((await owner.ApproveBasesAsync(account, [new(correction, 1)])).Single().Outcome == AgreementBasisApprovalOutcome.Approved,
            "correction drafts follow the existing approval validation");
        // Thirty drafts on one agreement make selection and page retention observable.
        var paged = await Create("Approval paged NOK");
        await owner.GenerateBasisAsync(account, paged.Agreement, AgreementDirection.Income, new(2027, 2, 1), new(2027, 12, 31));
        await owner.GenerateBasisAsync(account, paged.Agreement, AgreementDirection.Income, new(2028, 1, 1), new(2028, 12, 31));
        await owner.GenerateBasisAsync(account, paged.Agreement, AgreementDirection.Income, new(2029, 1, 1), new(2029, 6, 30));
        var euro = await Create("Approval EUR", currency: "EUR"); var cost = await Create("Approval cost", AgreementDirection.Cost);
        var filterList = await owner.ListPendingBasisApprovalsAsync(account, new(party, paged.Agreement, new(2028, 1, 1), new(2028, 12, 31), AgreementDirection.Income));
        Require(filterList.Items.Count == 12 && filterList.Items.All(x => x.PeriodFrom.HasValue && x.Amount == 1000), "party agreement date and direction filters combine without list limit");
        Require((await owner.ListPendingBasisApprovalsAsync(account, new(party, Direction: AgreementDirection.Cost))).Items.Single().BasisId == cost.Basis, "cost direction remains separate");
        await ComponentEditorChecks.Approvals(owner, new UserContext(ownerId, account), party, paged.Agreement, euro.Basis, cost.Basis,
            async () => (await owner.GenerateBasisAsync(account, paged.Agreement, AgreementDirection.Income, new(2029, 7, 1), new(2029, 7, 31))).Created.Single());
        Require((await owner.GetBasisAsync(account, cost.Basis)).Basis.Status == AgreementBasisStatus.Approved, "cost approval uses the same locked workflow");
        await using (var db = await factory.CreateDbContextAsync())
            Require(await db.EmailOutboxMessages.CountAsync() == outboxBefore, "bulk approval never queues email or invoice dispatch");
    }
    static void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL APPROVAL: " + name); Console.WriteLine("PASS APPROVAL: " + name); }
    static async Task Expect<T>(Func<Task> action, string name) where T : Exception
    { try { await action(); } catch (T) { Console.WriteLine("PASS APPROVAL: " + name); return; } throw new Exception("FAIL APPROVAL: " + name); }
}
