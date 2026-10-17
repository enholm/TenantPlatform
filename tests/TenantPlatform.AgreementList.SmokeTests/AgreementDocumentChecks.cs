using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;

static class AgreementDocumentChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL document: " + name); Console.WriteLine($"PASS {provider} document: {name}"); }
        async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { } }
        var account = Guid.NewGuid(); var owner = Guid.NewGuid(); var editor = Guid.NewGuid(); var reader = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var agreement = Guid.NewGuid(); var org = Guid.NewGuid(); var revision = Guid.NewGuid(); var docId = Guid.NewGuid(); var editGrant = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id = account, Name = "Documents" });
            foreach (var user in new[] { owner, editor, reader, outsider }) { db.Users.Add(new() { Id = user, Email = user + "@example.test", IsActive = true, IsPlatformAdmin = user == outsider }); db.UserAccounts.Add(new() { Id = Guid.NewGuid(), AccountId = account, UserId = user }); }
            db.Organizations.Add(new() { Id = org, AccountId = account, Name = "Party" });
            db.Agreements.Add(new() { Id = agreement, AccountId = account, OwnerUserId = owner, CounterpartyOrganizationId = org, Title = "Document target", Type = AgreementType.Other, Status = AgreementStatus.Active, CreatedByUserId = owner, UpdatedByUserId = owner, Revision = revision, StartDate = new(2020, 1, 1), CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow });
            db.AgreementAccess.AddRange(new AgreementAccess { Id = editGrant, AccountId = account, AgreementId = agreement, UserId = editor, Level = AgreementAccessLevel.Edit }, new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = agreement, UserId = reader, Level = AgreementAccessLevel.Read });
            db.AgreementDocuments.Add(new() { Id = docId, AccountId = account, AgreementId = agreement, Size = 1, Category = AgreementDocumentCategory.Contract, StorageKey = "existing", OriginalFileName = "contract.pdf", MediaType = "application/pdf", UploadedByUserId = owner, UploadedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, reader, account)); var storage = new Files();
        var service = new AgreementDocumentService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), new AgreementDocumentData(factory, storage, NullLogger<AgreementDocumentData>.Instance), storage, TimeProvider.System);
        guard.Protect = true;
        try
        {
            await using var stream = (await service.DownloadAsync(account, docId)).Content;
            Check(storage.Reads == 1, "authorized download opens stream without writes");
            actor.Current = new(true, outsider, account);
            await Denied(() => service.DownloadAsync(account, docId));
            Check(storage.Reads == 1, "global platform flag never opens protected stream");
        }
        finally { guard.Protect = false; }
        actor.Current = new(true, reader, account);
        await Denied(() => service.UploadAsync(account, agreement, revision, "file.pdf", new MemoryStream([1]), AgreementDocumentCategory.Contract, null));
        Check(storage.Stores == 0, "Read grant rejected before binary storage");
        if (provider == "PostgreSQL")
        {
            actor.Current = new(true, editor, account);
            await service.UploadAsync(account, agreement, revision, "file.pdf", new MemoryStream([1]), AgreementDocumentCategory.Contract, " Description ");
            await using (var db = factory.CreateDbContext())
            {
                var added = await db.AgreementDocuments.SingleAsync(x => x.AccountId == account && x.Id != docId);
                Check(added.UploadedByUserId == editor && added.Description == "Description", "direct upload records trusted actor and metadata");
                revision = (await db.Agreements.SingleAsync(x => x.Id == agreement)).Revision;
            }
            storage.OnStore = async () => { await using var db = factory.CreateDbContext(); db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.Id == editGrant)); await db.SaveChangesAsync(); };
            await Denied(() => service.UploadAsync(account, agreement, revision, "revoked.pdf", new MemoryStream([1]), AgreementDocumentCategory.Contract, null));
            await using (var db = factory.CreateDbContext()) Check(await db.AgreementDocuments.CountAsync(x => x.AccountId == account) == 2 && storage.Discarded == 1, "revocation during streaming rejects metadata and cleans uncommitted binary");
        }
        var management = new AgreementManagementService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), new AgreementManagementData(factory), TimeProvider.System);
        var financial = new AgreementFinancialService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), new AgreementFinancialData(factory), TimeProvider.System);
        actor.Current = new(true, reader, account);
        guard.Protect = true;
        try { Check(!(await financial.GetLinesAsync(account, agreement)).CanEdit, "direct financial read permits Read grant without writes"); }
        finally { guard.Protect = false; }
        await Denied(() => financial.SaveLineAsync(account, agreement, null, new()));
        await Denied(() => management.GetOptionsAsync(account, agreement));
        await Denied(() => management.SetAccessAsync(account, agreement, outsider, AgreementAccessLevel.Edit, revision));
        actor.Current = new(true, owner, account); guard.Protect = true;
        try { Check((await management.GetOptionsAsync(account, agreement)).Members.Count == 4, "direct management options independently authorize editor and remain write free"); }
        finally { guard.Protect = false; }
        if (provider == "PostgreSQL")
        {
            await using (var db = factory.CreateDbContext())
            {
                var membership = await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == owner);
                db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin }); await db.SaveChangesAsync();
            }
            var request = new SaveAgreementRequest { Title = "Created directly", Direction = AgreementDirection.Income, Currency = "NOK", Type = AgreementType.Other, Form = AgreementForm.Ongoing, NoticeCount = 1, NoticeUnit = AgreementNoticeUnit.Months, StartDate = new(2026, 1, 1), OwnerUserId = owner, CounterpartyOrganizationId = org };
            var created = await management.CreateAsync(account, request);
            async Task<Guid> Revision() { await using var db = factory.CreateDbContext(); return await db.Agreements.Where(x => x.Id == created).Select(x => x.Revision).SingleAsync(); }
            await management.SetAccessAsync(account, created, editor, AgreementAccessLevel.Edit, await Revision());
            actor.Current = new(true, editor, account); request.Revision = await Revision(); request.Title = "Updated directly";
            await management.UpdateAsync(account, created, request);
            request.Revision = await Revision(); request.OwnerUserId = editor;
            await Denied(() => management.UpdateAsync(account, created, request));
            actor.Current = new(true, owner, account); request.OwnerUserId = owner; request.Revision = await Revision(); request.CounterpartyOrganizationId = Guid.NewGuid();
            try { await management.UpdateAsync(account, created, request); throw new Exception("Expected invalid reference"); }
            catch (AgreementValidationException ex) when (ex.Message == "AgreementInvalidCounterparty") { }
            await management.SetAccessAsync(account, created, editor, null, await Revision());
            actor.Current = new(true, editor, account);
            await Denied(() => management.GetOptionsAsync(account, created));
            await using (var db = factory.CreateDbContext()) Check((await db.Agreements.SingleAsync(x => x.Id == created)).Title == "Updated directly", "direct management writes preserve grants, resource references and owner-only transfer");
            actor.Current = new(true, owner, account);
            var line = await financial.SaveLineAsync(account, created, null, new() { Revision = await Revision(), Name = "Rent", StartDate = new(2026, 1, 1), FirstPayableDate = new(2026, 1, 1), EffectiveFrom = new(2026, 1, 1), Activate = true, Quantity = 1, UnitPrice = 100 });
            guard.Protect = true;
            try { Check((await financial.ForecastAsync(account, created, new(2026, 1, 1), new(2026, 1, 31))).Income == 100 && (await financial.GetLinesAsync(account, created)).Lines.Single().Line.Id == line, "direct financial line and forecast share authorized snapshot"); }
            finally { guard.Protect = false; }
            await financial.CloseLineAsync(account, created, line, await Revision(), new(2026, 1, 31), false, "End rent");
            Check((await financial.GetLinesAsync(account, created)).Lines.Single().Versions.Count == 2, "direct line close preserves historical version");
            var from = new DateOnly(2026, 1, 1); var to = new DateOnly(2026, 1, 31);
            Check((await financial.PreviewBasisAsync(account, created, AgreementDirection.Income, from, to)).Income == 100, "direct basis preview uses complete invoice event");
            var run = await financial.GenerateBasisAsync(account, created, AgreementDirection.Income, from, to); var basisId = run.Created.Single();
            var basis = await financial.GetBasisAsync(account, basisId);
            await financial.RegenerateBasisAsync(account, basisId, basis.Basis.Revision, "Review");
            basis = await financial.GetBasisAsync(account, basisId);
            await management.SetAccessAsync(account, created, editor, AgreementAccessLevel.Edit, await Revision());
            actor.Current = new(true, editor, account);
            await Denied(() => financial.ApproveBasisAsync(account, basisId, basis.Basis.Revision));
            await Denied(() => financial.CancelBasisAsync(account, basisId, basis.Basis.Revision, "Cancel"));
            Check((await financial.ApproveBasesAsync(account, [new(basisId, basis.Basis.Revision)])).Single().Outcome == AgreementBasisApprovalOutcome.Failed, "direct bulk approval preserves per-item edit versus manage boundary");
            actor.Current = new(true, owner, account);
            Check((await financial.ListPendingBasisApprovalsAsync(account, new(AgreementId: created))).Items.Single().CanApprove && (await financial.ListBasisAsync(account, AgreementDirection.Income)).Single().Basis.Id == basisId, "direct pending and basis lists apply resource scope");
            await financial.ApproveBasisAsync(account, basisId, basis.Basis.Revision);
            Check((await financial.GetAffectedBasesAsync(account, created)).Count == 0, "unchanged approved basis needs no correction");
            try { await financial.CreateCorrectionAsync(account, basisId, "Correction"); throw new Exception("Expected no difference"); }
            catch (AgreementValidationException ex) when (ex.Message == "ProcessingNoDifference") { }
            await financial.CancelBasisAsync(account, basisId, basis.Basis.Revision, "Cancel");
            var preview = (await financial.PreviewBulkBasisAsync(account, new(from, to, AgreementId: created))).Single();
            var bulk = await financial.GenerateBulkBasisAsync(account, from, to, [new(created, preview.Fingerprint)]);
            Check(bulk.Single().Created.Count == 1 && (await financial.GetBulkBasisOptionsAsync(account)).Single().Id == created, "direct bulk generation reuses canceled events and exact preview fingerprint");


        }
    }
    private sealed class Files : IAgreementDocumentStorage
    {
        public int Stores, Reads, Discarded; public Func<Task>? OnStore;
        public long MaxFileSizeBytes => 1024;
        public async Task<StoredAgreementFile> StoreAsync(Stream source, string originalFileName, CancellationToken cancellationToken = default)
        { Stores++; if (OnStore is not null) await OnStore(); return new(Guid.NewGuid().ToString(), originalFileName, "application/pdf", 1); }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) { Reads++; return Task.FromResult<Stream>(new MemoryStream([1])); }
        public Task DiscardUncommittedAsync(string storageKey) { Discarded++; return Task.CompletedTask; }
    }
}
