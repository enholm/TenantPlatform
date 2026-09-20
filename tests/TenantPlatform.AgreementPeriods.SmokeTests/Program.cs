using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web;
using TenantPlatform.Web.Email;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

var connection = Environment.GetEnvironmentVariable("AGREEMENT_TEST_CONNECTION") ?? throw new Exception("Set AGREEMENT_TEST_CONNECTION.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_agreement_tests") throw new Exception("Only the disposable tenant_agreement_tests database is allowed.");
var schema = "period_test_" + Guid.NewGuid().ToString("N");
await using (var setup = new NpgsqlConnection(connection))
{
    await setup.OpenAsync();
    await using var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup); await cmd.ExecuteNonQueryAsync();
}
cs.SearchPath = schema;
Console.WriteLine($"Isolated schema: {schema}");
var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString).Options);
await using (var db = factory.CreateDbContext())
{
    Assert(!db.Database.HasPendingModelChanges(), "model matches migration snapshot");
    await db.GetService<IMigrator>().MigrateAsync("20260919130349_AddAgreementNoticeRules");
}
var clock = new TestClock(new(2032, 2, 29, 6, 59, 0, TimeSpan.Zero));
var today = new DateOnly(2032, 2, 29);
var account = Guid.NewGuid(); var otherAccount = Guid.NewGuid();
var adminId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var readerId = Guid.NewGuid(); var editorId = Guid.NewGuid();
var strangerId = Guid.NewGuid(); var foreignId = Guid.NewGuid(); var org = Guid.NewGuid(); var foreignOrg = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Accounts.AddRange(new Account { Id = account, Name = "A" }, new Account { Id = otherAccount, Name = "B" });
    foreach (var memberId in new[] { adminId, ownerId, readerId, editorId, strangerId, foreignId })
    {
        db.Users.Add(new User { Id = memberId, FirstName = memberId == ownerId ? "Owner" : "Member", Email = $"{memberId}@example.test", PreferredLanguage = "en-GB" });
        var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = memberId == foreignId ? otherAccount : account, UserId = memberId };
        db.UserAccounts.Add(membership);
        if (memberId == adminId || memberId == foreignId)
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
    }
    db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Acme" }, new Organization { Id = foreignOrg, AccountId = otherAccount, Name = "Other" });
    await db.SaveChangesAsync();
}
var root = Path.Combine(Path.GetTempPath(), "followup-capture-" + Guid.NewGuid().ToString("N"));
var storage = new LocalAgreementDocumentStorage(Options.Create(new AgreementDocumentStorageOptions { RootPath = root }));
var options = Options.Create(new AgreementReminderOptions { ApplicationBaseUrl = "https://tenant.example.test", CapturePath = root });
AgreementService Service(Guid userId, Guid accountId) {
    var context = new UserContext(userId, accountId);
    return new(factory, context, new TenantAuthorizationService(factory, context), storage, NullLogger<AgreementService>.Instance, clock, options);
}
var admin = Service(adminId, account); var owner = Service(ownerId, account); var reader = Service(readerId, account);
var editor = Service(editorId, account); var stranger = Service(strangerId, account); var foreign = Service(foreignId, otherAccount);
// Upgrade populated prior schema; nullable additions must not classify or rewrite existing contracts/documents.
var legacy = Guid.NewGuid(); var legacyDocument = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO agreements ("Id","AccountId","Title","Type","CounterpartyOrganizationId","OwnerUserId","Status","StartDate","AutoRenew","CreatedUtc","UpdatedUtc","CreatedByUserId","UpdatedByUserId","Revision","CurrentPeriodStartDate")
        VALUES ({legacy},{account},'Existing supplier',4,{org},{ownerId},2,{today},false,{clock.Now},{clock.Now},{adminId},{adminId},{Guid.NewGuid()},{today})
        """);
    db.AgreementDocuments.Add(new() { Id = legacyDocument, AccountId = account, AgreementId = legacy,
        OriginalFileName = "existing.pdf", StorageKey = Guid.NewGuid().ToString("N"), MediaType = "application/pdf", Size = 10,
        Category = AgreementDocumentCategory.Contract, UploadedByUserId = ownerId, UploadedUtc = clock.Now });
    await db.SaveChangesAsync(); await db.Database.MigrateAsync();
    Assert(!db.Database.HasPendingModelChanges(), "model and generated migration match");
}
var migrated = await admin.GetAsync(account, legacy);
Assert(migrated.Direction == null && migrated.Currency == null && migrated.Documents.Single().Id == legacyDocument, "migration preserves contracts and document identity without guessing direction");

// Pure deterministic engine scenarios.
var pureAgreement = Guid.NewGuid();
(AgreementLineVersion V, AgreementPriceVersion P) Fixture(AgreementFrequency frequency, DateOnly start, decimal amount, AgreementAnchor anchor = AgreementAnchor.Calendar)
{
    var line = Guid.NewGuid();
    return (new() { Id = Guid.NewGuid(), AgreementId = pureAgreement, LineId = line, Sequence = 1,
        Name = "Fixture", StartDate = start, EffectiveFrom = start, FirstPayableDate = start, Frequency = frequency,
        Anchor = anchor, AnchorDate = start, BillingTiming = AgreementBillingTiming.Advance, Status = AgreementLineStatus.Active },
        new() { Id = Guid.NewGuid(), AgreementId = pureAgreement, LineId = line, Sequence = 1, EffectiveFrom = start, Quantity = 1, UnitPrice = amount });
}
List<AgreementCalculatedPeriod> Calculate((AgreementLineVersion V, AgreementPriceVersion P) item, DateOnly start, DateOnly end) =>
    AgreementPeriodCalculator.Calculate(pureAgreement, AgreementDirection.Income, "NOK", [item.V], [item.P], start, end);
var parking = Fixture(AgreementFrequency.Monthly, new(2027, 1, 16), 3100);
var rent = Fixture(AgreementFrequency.Monthly, new(2027, 1, 1), 10000);
var both = AgreementPeriodCalculator.Calculate(pureAgreement, AgreementDirection.Income, "NOK", [parking.V, rent.V], [parking.P, rent.P], new(2027, 1, 1), new(2027, 1, 31));
Assert(both.Single(x => x.LineId == parking.V.LineId).Amount == 1600 && both.Single(x => x.LineId == rent.V.LineId).Amount == 10000, "extra parking mid-month prorates independently");
var license = Fixture(AgreementFrequency.Once, new(2027, 3, 15), 24000);
var maintenance = Fixture(AgreementFrequency.Yearly, new(2027, 3, 15), 12000, AgreementAnchor.Date);
maintenance.V.FirstPayableDate = new(2028, 3, 15);
var maintenanceRows = Calculate(maintenance, new(2027, 3, 15), new(2029, 3, 14));
Assert(maintenanceRows.Count == 2 && maintenanceRows[0].Included && maintenanceRows[0].Amount == 0 && maintenanceRows[1].Amount == 12000 && maintenanceRows[1].From == new DateOnly(2028, 3, 15), "twelve months included then full annual maintenance price");
var licenseRows = Calculate(license, new(2027, 1, 1), new(2029, 12, 31));
Assert(licenseRows.Single().Amount == 24000 && license.V.EndDate == null && licenseRows.Single().EventKey == Calculate(license, new(2027, 1, 1), new(2029, 12, 31)).Single().EventKey, "one-off has stable event and does not end perpetual delivery");
foreach (var (frequency, count) in new[] { (AgreementFrequency.Monthly,12), (AgreementFrequency.Quarterly,4), (AgreementFrequency.HalfYearly,2), (AgreementFrequency.Yearly,1), (AgreementFrequency.Once,1) })
{
    var item = Fixture(frequency, new(2027, 1, 1), 100);
    var rows = Calculate(item, new(2027, 1, 1), new(2027, 12, 31));
    Assert(rows.Count == count && rows.All(x => x.Amount == 100), "price is per frequency: " + frequency);
}
var mixed = Enum.GetValues<AgreementFrequency>().Select(f => Fixture(f,new(2027,1,1),100)).ToArray();
var mixedRows=AgreementPeriodCalculator.Calculate(pureAgreement,AgreementDirection.Income,"NOK",mixed.Select(x=>x.V),mixed.Select(x=>x.P),new(2027,1,1),new(2027,12,31));
Assert(mixedRows.Count==20 && mixedRows.Sum(x=>x.Amount)==2000,"all five frequencies coexist independently under one agreement");
var ending = Fixture(AgreementFrequency.Monthly, new(2027, 1, 1), 3100); ending.V.EndDate = new(2027, 1, 10);
Assert(Calculate(ending, new(2027, 1, 1), new(2027, 3, 1)).Single().Amount == 1000, "inclusive mid-period end prorates correctly");
foreach (var day in new[] { 29,30,31 })
{
    var item = Fixture(AgreementFrequency.Monthly, new(2028, 1, day), 100, AgreementAnchor.Date);
    var rows = Calculate(item, new(2028, 1, day), new(2028, 4, 28));
    Assert(rows[1].ReferenceFrom == new DateOnly(2028, 2, 29) && rows[2].ReferenceFrom == new DateOnly(2028, 3, day), "original anchor survives leap February: " + day);
}
var narrow = Calculate(rent, new(2027, 1, 10), new(2027, 1, 12)).Single();
Assert(narrow.Amount == 10000 && narrow.From == new DateOnly(2027, 1, 1) && narrow.To == new DateOnly(2027, 1, 31), "search overlap does not clip period amount");
var rounding = Fixture(AgreementFrequency.Monthly, new(2027, 1, 1), 1.005m);
Assert(Calculate(rounding, new(2027, 1, 1), new(2027, 1, 31)).Single().Amount == 1.01m, "decimal rounding is away from zero at currency precision");
var deferred = Fixture(AgreementFrequency.Monthly, new(2027, 1, 1), 3100); deferred.V.FirstPayableDate = new(2027, 1, 16); deferred.V.BillingTiming = AgreementBillingTiming.Arrears;
var def = Calculate(deferred, new(2027, 1, 1), new(2027, 1, 31));
Assert(def.Count == 2 && def[0].Amount == 0 && def[1].Amount == 1600 && def[1].InvoiceDate == new DateOnly(2027, 2, 1), "partial included period and arrears date");
var yearBoundary = Fixture(AgreementFrequency.Monthly, new(2027, 12, 31), 100, AgreementAnchor.Date);
Assert(Calculate(yearBoundary, new(2027, 12, 31), new(2028, 2, 1))[1].ReferenceFrom == new DateOnly(2028, 1, 31), "date anchors cross year boundaries");

// Service and database integration.
SaveAgreementRequest AgreementRequest(AgreementDirection? direction = AgreementDirection.Income) => new() { Title = "Financial agreement", Form = AgreementForm.Renewing,
    RenewalDate = new(2035, 1, 1), StartDate = new(2027, 1, 1), OwnerUserId = ownerId, CounterpartyOrganizationId = org, Direction = direction, Currency = "NOK", Status = AgreementStatus.Active };
var id = await admin.CreateAsync(account, AgreementRequest());
await owner.SetAccessAsync(account, id, readerId, AgreementAccessLevel.Read, (await owner.GetAsync(account,id)).Revision);
await owner.SetAccessAsync(account, id, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account,id)).Revision);
SaveAgreementLineRequest LineRequest(string name = "Line", decimal price = 100) => new() { Name = name, StartDate = new(2027, 1, 1), FirstPayableDate = new(2027, 1, 1), AnchorDate = new(2027, 1, 1), UnitPrice = price, Activate = true };
async Task<Guid> Save(Guid agreementId, Guid? lineId, SaveAgreementLineRequest request) { request.Revision = (await owner.GetAsync(account,agreementId)).Revision; return await owner.SaveLineAsync(account,agreementId,lineId,request); }
var baseLine = await Save(id,null,LineRequest("Rent",10000));
var parked = LineRequest("Parking",3100); parked.StartDate = new(2027,1,16); parked.FirstPayableDate = parked.StartDate;
var parkingId = await Save(id,null,parked);
Assert((await reader.ForecastAsync(account,id,new(2027,1,1),new(2027,1,31))).Income == 11600, "reader sees combined independent income forecast");
var supplierRequest = AgreementRequest(null); supplierRequest.Type = AgreementType.Supplier;
var supplier = await admin.CreateAsync(account,supplierRequest);
Assert((await owner.GetAsync(account,supplier)).Direction == AgreementDirection.Cost, "new supplier agreement defaults to explicit cost");
await Save(supplier,null,LineRequest());
var costForecast = await owner.ForecastAsync(account,supplier,new(2027,1,1),new(2027,1,31));
Assert(costForecast.Income == 0 && costForecast.Cost == 100 && costForecast.Periods.All(x=>x.Direction == AgreementDirection.Cost), "cost forecasts never produce outgoing income");
var settings = await owner.GetAsync(account,id); settings.Direction = AgreementDirection.Cost;
await Expect<AgreementValidationException>(()=>owner.UpdateAsync(account,id,settings), "direction locked after activation");
settings = await owner.GetAsync(account,id); settings.Currency = "EUR";
await Expect<AgreementValidationException>(()=>owner.UpdateAsync(account,id,settings), "currency locked after activation");
settings = await owner.GetAsync(account,id); settings.CounterpartyOrganizationId = foreignOrg;
await Expect<AgreementValidationException>(()=>owner.UpdateAsync(account,id,settings), "cross-account counterparty rejected");
// Document references are immutable IDs, not copies; all FK scopes include agreement and account.
await owner.UploadAsync(account,id,(await owner.GetAsync(account,id)).Revision,"contract.pdf",new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()),AgreementDocumentCategory.Contract,null);
var doc = (await owner.GetAsync(account,id)).Documents.Single().Id;
var licensed = LineRequest("Perpetual license",24000); licensed.Frequency = AgreementFrequency.Once; licensed.StartDate = new(2027,3,15); licensed.FirstPayableDate = licensed.StartDate; licensed.DocumentIds = [doc];
var licenseId = await Save(id,null,licensed);
var maintained = LineRequest("Maintenance",12000); maintained.StartDate = licensed.StartDate; maintained.Frequency = AgreementFrequency.Yearly; maintained.Anchor = AgreementAnchor.Date; maintained.AnchorDate = licensed.StartDate;
maintained.PayableSourceLineId = licenseId; maintained.PayableOffsetMonths = 12; maintained.DocumentIds = [doc];
var maintenanceId = await Save(id,null,maintained);
var lineData = await reader.GetLinesAsync(account,id);
Assert(lineData.Lines.Single(x=>x.Line.Id == maintenanceId).Versions[0].FirstPayableDate == new DateOnly(2028,3,15) &&
    lineData.Lines.Count(x=>x.Versions[0].Documents.Any(d=>d.DocumentId==doc)) == 2 && (await owner.GetAsync(account,id)).Documents.Count==1, "source-based payment snapshot and shared document without reupload");
var invalidDoc = LineRequest(); invalidDoc.DocumentIds = [legacyDocument];
await Expect<AgreementValidationException>(()=>Save(id,null,invalidDoc), "same-account different-agreement document rejected");
var foreignAgreementRequest = AgreementRequest(); foreignAgreementRequest.OwnerUserId = foreignId; foreignAgreementRequest.CounterpartyOrganizationId = foreignOrg;
var foreignAgreement = await foreign.CreateAsync(otherAccount,foreignAgreementRequest);
await foreign.UploadAsync(otherAccount,foreignAgreement,(await foreign.GetAsync(otherAccount,foreignAgreement)).Revision,"foreign.pdf",new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()),AgreementDocumentCategory.Contract,null);
invalidDoc.DocumentIds = [(await foreign.GetAsync(otherAccount,foreignAgreement)).Documents.Single().Id];
await Expect<AgreementValidationException>(()=>Save(id,null,invalidDoc), "foreign-account document rejected");
var invalidSource = LineRequest(); invalidSource.PayableSourceLineId = Guid.NewGuid(); invalidSource.PayableOffsetMonths = 12;
await Expect<AgreementValidationException>(()=>Save(id,null,invalidSource), "foreign or unknown source line rejected");
var priceChange = LineRequest("Rent",12000); priceChange.EffectiveFrom = new(2027,2,1); priceChange.Reason="New price";
await Save(id,baseLine,priceChange);
var changed = await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,2,28));
Assert(changed.Periods.Single(x=>x.LineId==baseLine && x.From.Month==1).Amount == 10000 && changed.Periods.Single(x=>x.LineId==baseLine && x.From.Month==2).Amount == 12000, "dated price version preserves old amounts");
priceChange.EffectiveFrom = new(2027,3,15);
await Save(id,baseLine,priceChange); // Corrections are allowed inside a period.
priceChange.EffectiveFrom=new(2027,4,1); priceChange.Frequency=AgreementFrequency.Quarterly; priceChange.Quantity=2;
await Save(id,baseLine,priceChange);
var ruleForecast=await owner.ForecastAsync(account,id,new(2027,3,1),new(2027,6,30));
Assert(ruleForecast.Periods.Where(x=>x.LineId==baseLine && x.From.Month==3).Sum(x=>x.Amount)==12000 && ruleForecast.Periods.Single(x=>x.LineId==baseLine && x.From.Month==4).Amount==24000,
    "dated frequency and quantity changes retain old monthly period and create full quarterly price");
foreach(var invalid in new[]{0m,-1m,0.00001m}) { var invalidQuantity=LineRequest(); invalidQuantity.Quantity=invalid;
    await Expect<AgreementValidationException>(()=>Save(id,null,invalidQuantity),"invalid quantity rejected"); }
await owner.CloseLineAsync(account,id,parkingId,(await owner.GetAsync(account,id)).Revision,new(2027,2,10),false,"Space returned");
var afterClose = await owner.ForecastAsync(account,id,new(2027,2,1),new(2027,3,31));
Assert(afterClose.Periods.Single(x=>x.LineId==parkingId).Amount == decimal.Round(3100m*10/28,2,MidpointRounding.AwayFromZero) && afterClose.Periods.Where(x=>x.LineId==baseLine).Select(x=>x.ReferenceFrom).Distinct().Count()==2,
    "dated ending retains partial final period and other lines continue");
var deactivateId = await Save(id,null,LineRequest("Deactivate",3100));
await owner.CloseLineAsync(account,id,deactivateId,(await owner.GetAsync(account,id)).Revision,new(2027,1,11),true,"Removed from processing");
Assert((await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,2,28))).Periods.Single(x=>x.LineId==deactivateId).Amount==1000, "deactivation keeps historical amount and cuts future periods");
await Expect<AgreementValidationException>(()=>Save(id,deactivateId,LineRequest()), "closed line cannot be silently reactivated");

// Draft source changes and activation snapshots, including a cycle attempt.
var draftA = LineRequest("Source draft"); draftA.Activate=false;
var sourceId = await Save(id,null,draftA);
var draftB = LineRequest("Dependent draft"); draftB.Activate=false; draftB.PayableSourceLineId=sourceId; draftB.PayableOffsetMonths=12;
var dependentId = await Save(id,null,draftB);
draftA.PayableSourceLineId=dependentId; draftA.PayableOffsetMonths=0;
await Expect<AgreementValidationException>(()=>Save(id,sourceId,draftA), "circular dependencies rejected");
draftA.PayableSourceLineId=null; draftA.StartDate=new(2027,2,1); draftA.FirstPayableDate=draftA.StartDate;
await Save(id,sourceId,draftA);
draftB.Activate=true; await Save(id,dependentId,draftB);
var activatedSnapshot=(await owner.GetLinesAsync(account,id)).Lines.Single(x=>x.Line.Id==dependentId).Versions[0];
Assert(activatedSnapshot.FirstPayableDate==new DateOnly(2028,2,1) && activatedSnapshot.PayableSourceStartDate==new DateOnly(2027,2,1), "activation recalculates and stores source basis");
draftA.StartDate=new(2027,3,1); draftA.FirstPayableDate=draftA.StartDate; await Save(id,sourceId,draftA);
Assert((await owner.GetLinesAsync(account,id)).Lines.Single(x=>x.Line.Id==dependentId).Versions[0].FirstPayableDate==new DateOnly(2028,2,1), "source edits cannot silently change active dependent");
var abandoned = LineRequest("Draft price",100); abandoned.Activate=false; abandoned.StartDate=new(2027,2,1); abandoned.FirstPayableDate=abandoned.StartDate;
var abandonedId=await Save(id,null,abandoned); abandoned.StartDate=new(2027,1,1); abandoned.FirstPayableDate=abandoned.StartDate; abandoned.UnitPrice=200; abandoned.Activate=true;
await Save(id,abandonedId,abandoned);
Assert((await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,3,31))).Periods.Where(x=>x.LineId==abandonedId).All(x=>x.Amount==200), "abandoned draft prices never activate later");
foreach (var user in new[] { reader, stranger }) await Expect<UnauthorizedAccessException>(()=>user.SaveLineAsync(account,id,null,LineRequest()), "non-editor cannot write lines");
await Expect<UnauthorizedAccessException>(()=>foreign.GetLinesAsync(otherAccount,id), "foreign tenant cannot read line versions");
await Expect<UnauthorizedAccessException>(()=>admin.ForecastAsync(otherAccount,id,new(2027,1,1),new(2027,2,1)), "forged account cannot forecast");
await Expect<UnauthorizedAccessException>(()=>reader.CloseLineAsync(account,id,baseLine,Guid.Empty,new(2027,3,1),true,"No access"), "reader cannot deactivate");
var editorLine=LineRequest("Editor line"); editorLine.Revision=(await editor.GetAsync(account,id)).Revision;
await editor.SaveLineAsync(account,id,null,editorLine);
await Expect<AgreementValidationException>(()=>editor.SaveLineAsync(account,id,null,editorLine), "stale revision prevents duplicate mutation");
var raceRequest=LineRequest("Concurrent line"); raceRequest.Revision=(await owner.GetAsync(account,id)).Revision;
var race=await Task.WhenAll(TryCreate(),TryCreate()); Assert(race.Count(x=>x)==1,"one winner for concurrent line creation");
await using (var db=factory.CreateDbContext())
{
    var version=(await owner.GetLinesAsync(account,id)).Lines.Single(x=>x.Line.Id==licenseId).Versions[0];
    db.AgreementLineDocuments.Add(new(){AccountId=account,AgreementId=id,LineVersionId=version.Id,DocumentId=legacyDocument});
    await Expect<DbUpdateException>(()=>db.SaveChangesAsync(),"composite FK rejects cross-agreement document link");
}
var f1=await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,12,31)); clock.Now=clock.Now.AddYears(5); var f2=await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,12,31));
Assert(System.Text.Json.JsonSerializer.Serialize(f1)==System.Text.Json.JsonSerializer.Serialize(f2),"repeat preview identical and has no billing side effects");
Console.WriteLine("All agreement financial period tests passed. No email or billing transport is used.");
async Task<bool> TryCreate(){try{await owner.SaveLineAsync(account,id,null,raceRequest);return true;}catch(AgreementValidationException e) when(e.Message=="AgreementConcurrencyConflict"){return false;}}
static void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
static async Task Expect<T>(Func<Task> action,string name) where T:Exception{try{await action();}catch(T){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: "+name);}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options):IDbContextFactory<TenantPlatformDbContext>{public TenantPlatformDbContext CreateDbContext()=>new(options);}
sealed class UserContext(Guid user,Guid account):ICurrentUserContextService{public CurrentUserContext Current=>new(){IsAuthenticated=true,UserId=user,CurrentAccountId=account};}
sealed class TestClock(DateTimeOffset initial):TimeProvider{public DateTimeOffset Now{get;set;}=initial;public override DateTimeOffset GetUtcNow()=>Now;}
