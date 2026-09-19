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
var schema = "processing_test_" + Guid.NewGuid().ToString("N");
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
var legacy = Guid.NewGuid(); var legacyDocument = Guid.NewGuid(); var legacyLine = Guid.NewGuid(); var legacyPrice = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO agreements ("Id","AccountId","Title","Type","CounterpartyOrganizationId","OwnerUserId","Status","StartDate","AutoRenew","CreatedUtc","UpdatedUtc","CreatedByUserId","UpdatedByUserId","Revision","CurrentPeriodStartDate")
        VALUES ({legacy},{account},'Existing supplier',4,{org},{ownerId},2,{today},false,{clock.Now},{clock.Now},{adminId},{adminId},{Guid.NewGuid()},{today})
        """);
    db.AgreementDocuments.Add(new() { Id = legacyDocument, AccountId = account, AgreementId = legacy,
        OriginalFileName = "existing.pdf", StorageKey = Guid.NewGuid().ToString("N"), MediaType = "application/pdf", Size = 10,
        Category = AgreementDocumentCategory.Contract, UploadedByUserId = ownerId, UploadedUtc = clock.Now });
    await db.SaveChangesAsync();
    await db.GetService<IMigrator>().MigrateAsync("20260919163903_AddAgreementFinancialLines");
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO agreement_lines ("Id","AccountId","AgreementId") VALUES ({legacyLine},{account},{legacy});
        INSERT INTO agreement_line_versions ("Id","AccountId","AgreementId","LineId","Sequence","EffectiveFrom","RecordedUtc","ActorUserId","Name","StartDate","FirstPayableDate","Frequency","Anchor","AnchorDate","BillingTiming","Status")
        VALUES ({Guid.NewGuid()},{account},{legacy},{legacyLine},1,{today},{clock.Now},{ownerId},'Legacy draft',{today},{today},1,1,{today},1,0);
        INSERT INTO agreement_price_versions ("Id","AccountId","AgreementId","LineId","Sequence","EffectiveFrom","Quantity","UnitPrice","RecordedUtc","ActorUserId")
        VALUES ({legacyPrice},{account},{legacy},{legacyLine},1,{today},2,123.4567,{clock.Now},{ownerId});
        """);
    await db.Database.MigrateAsync();
    var preserved = await db.AgreementPriceVersions.SingleAsync(x=>x.AccountId==account && x.Id==legacyPrice);
    Assert(preserved.UnitPrice==123.4567m && preserved.Quantity==2 && !preserved.Independent && preserved.AdjustmentId==null,"phase 4 migration preserves existing price identity, precision and draft semantics");
    Assert(!db.Database.HasPendingModelChanges(), "model and generated migration match");
}
var migrated = await admin.GetAsync(account, legacy);
Assert(migrated.Direction == null && migrated.Currency == null && migrated.Documents.Single().Id == legacyDocument, "migration preserves contracts and document identity without guessing direction");

// Pure adjustment calculations.
var rule = new AgreementAdjustmentRule { Kind=AgreementAdjustmentKind.Index, AllowDecrease=true };
decimal Calc(decimal share,AgreementAdjustmentAddition addition=AgreementAdjustmentAddition.None,decimal extra=0)
{ rule.SharePercent=share;rule.Addition=addition;rule.AdditionPercent=extra;return AgreementAdjustmentCalculator.Calculate(rule,1000,100,104,null).Price; }
Assert(Calc(100)==1040,"whole index change"); Assert(Calc(70)==1028,"70 percent share");
Assert(Calc(100,AgreementAdjustmentAddition.PercentagePoints,2)==1060,"percentage points");
Assert(Calc(100,AgreementAdjustmentAddition.PriceMarkup,2)==1060.80m,"markup after index adjustment");
rule.Addition=AgreementAdjustmentAddition.None;rule.AllowDecrease=true;
Assert(AgreementAdjustmentCalculator.Calculate(rule,1000,100,96,null).Price==960,"negative index reduces price");
rule.FloorPercent=0;Assert(AgreementAdjustmentCalculator.Calculate(rule,1000,100,96,null).Price==1000,"floor zero prevents reduction");
var limit=new AgreementAdjustmentRule{Kind=AgreementAdjustmentKind.Limit,CeilingPercent=5};
Assert(AgreementAdjustmentCalculator.Calculate(limit,1000,null,null,3).Price==1030,"explicit 3 percent within cap");
await Expect<AgreementValidationException>(()=>Task.FromResult(AgreementAdjustmentCalculator.Calculate(limit,1000,null,null,6)),"6 percent exceeds cap");
await Expect<AgreementValidationException>(()=>Task.FromResult(AgreementAdjustmentCalculator.Calculate(limit,1000,null,null,null)),"cap never applied without explicit choice");
rule.FloorPercent=6;rule.CeilingPercent=5;
await Expect<AgreementValidationException>(()=>Task.FromResult(AgreementAdjustmentCalculator.Calculate(rule,1000,100,104,null)),"inverted floor ceiling rejected");

SaveAgreementRequest AgreementRequest(AgreementDirection direction=AgreementDirection.Income)=>new(){Title="Processing agreement",Form=AgreementForm.Renewing,RenewalDate=new(2035,1,1),StartDate=new(2027,1,1),OwnerUserId=ownerId,CounterpartyOrganizationId=org,Direction=direction,Currency="NOK",Status=AgreementStatus.Active};
async Task<Guid> NewAgreement(AgreementDirection direction=AgreementDirection.Income)=>await admin.CreateAsync(account,AgreementRequest(direction));
async Task<Guid> Line(Guid a,decimal price=1000,AgreementFrequency frequency=AgreementFrequency.Monthly,AgreementBillingTiming timing=AgreementBillingTiming.Advance)=>await owner.SaveLineAsync(account,a,null,new(){Revision=(await owner.GetAsync(account,a)).Revision,Name="Test line",StartDate=new(2027,1,1),FirstPayableDate=new(2027,1,1),AnchorDate=new(2027,1,1),UnitPrice=price,Activate=true,Frequency=frequency,BillingTiming=timing});
async Task Manual(Guid a,Guid l,DateOnly d,decimal p,AgreementPriceTiming timing=AgreementPriceTiming.Split)=>await owner.ChangePriceAsync(account,a,l,(await owner.GetAsync(account,a)).Revision,d,p,timing,"Manual correction");
async Task<AgreementAdjustmentRule> Rule(Guid a,Guid l,Guid? idx=null,DateOnly? effective=null)=>new(){LineId=l,Kind=idx.HasValue?AgreementAdjustmentKind.Index:AgreementAdjustmentKind.Percentage,IndexId=idx,FixedPercent=4,BasePriceVersionId=(await owner.GetLinesAsync(account,a)).Lines.Single(x=>x.Line.Id==l).Prices[0].Id,BaseIndexPeriod=new(2027,1,1),EffectiveFrom=effective??new(2027,1,1),FirstAllowedDate=new(2028,1,1),AnchorDate=new(2028,1,1),Reason="Agreed adjustment",IntervalMonths=12};
async Task SaveRule(Guid a,AgreementAdjustmentRule r)=>await owner.SaveAdjustmentRuleAsync(account,a,(await owner.GetAsync(account,a)).Revision,r);
AgreementBasisData Body(AgreementBasisDetails d)=>System.Text.Json.JsonSerializer.Deserialize<AgreementBasisData>(d.Snapshots[0].DataJson)!;

var indexId=await admin.CreateIndexAsync(account,"CPI","Consumer prices","Manual test","Statistics",AgreementIndexResolution.Month);
await admin.RecordIndexValueAsync(account,indexId,new(2027,1,1),100,null,"Base");
var id=await NewAgreement();var line=await Line(id);await SaveRule(id,await Rule(id,line,indexId));
await owner.SetAccessAsync(account,id,readerId,AgreementAccessLevel.Read,(await owner.GetAsync(account,id)).Revision);
await owner.SetAccessAsync(account,id,editorId,AgreementAccessLevel.Edit,(await owner.GetAsync(account,id)).Revision);
var due=await reader.FindAdjustmentsAsync(account,id,new(2028,1,1),new(2028,12,31));
Assert(due.Count==1 && due[0].BlockedKey=="ProcessingMissingIndex","missing exact index blocks annual adjustment on monthly line");
await admin.RecordIndexValueAsync(account,indexId,new(2028,1,1),104,null,"Published");
var proposal=await editor.ProposeAdjustmentAsync(account,id,line,new(2028,1,1),null);
await Expect<UnauthorizedAccessException>(()=>editor.DecideAdjustmentAsync(account,id,proposal,true,"Approve"),"editor cannot approve");
await admin.RecordIndexValueAsync(account,indexId,new(2028,1,1),105,null,"Correction");
await Expect<AgreementValidationException>(()=>owner.DecideAdjustmentAsync(account,id,proposal,true,"Approve"),"index revision makes proposal stale");
Assert((await owner.GetProcessingAsync(account,id)).Proposals.Single(x=>x.Id==proposal).Status==AgreementProposalStatus.Stale,"stale status persisted");
proposal=await owner.ProposeAdjustmentAsync(account,id,line,new(2028,1,1),null);
await Task.WhenAll(owner.DecideAdjustmentAsync(account,id,proposal,true,"Reviewed"),owner.DecideAdjustmentAsync(account,id,proposal,true,"Reviewed"));
var prices=(await owner.GetLinesAsync(account,id)).Lines.Single().Prices;
Assert(prices.Count(x=>x.AdjustmentId==proposal)==1 && prices[0].UnitPrice==1050,"parallel approvals create one price version");
await Expect<AgreementValidationException>(()=>owner.ProposeAdjustmentAsync(account,id,line,new(2028,2,1),null),"monthly billing cannot adjust before annual interval");
var captured=(await owner.GetProcessingAsync(account,id)).Proposals.Single(x=>x.Id==proposal).CalculationJson;
await admin.RecordIndexValueAsync(account,indexId,new(2028,1,1),106,null,"Later correction");
Assert((await owner.GetProcessingAsync(account,id)).Proposals.Single(x=>x.Id==proposal).CalculationJson==captured,"applied adjustment keeps exact old index revision");
await Expect<UnauthorizedAccessException>(()=>reader.RecordIndexValueAsync(account,indexId,new(2028,1,1),107,null,"No access"),"index write admin-only");
await Expect<UnauthorizedAccessException>(()=>foreign.RecordIndexValueAsync(otherAccount,indexId,new(2028,1,1),107,null,"Foreign"),"index tenant isolation");
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,indexId,new(2028,1,2),104,null,"Wrong boundary"),"index period cannot interpolate");
var quarter=await admin.CreateIndexAsync(account,"Q","Quarterly","","Source",AgreementIndexResolution.Quarter);
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,quarter,new(2028,2,1),104,null,"Wrong quarter"),"quarterly boundary validation");

var split=await NewAgreement();var splitLine=await Line(split,3100);await Manual(split,splitLine,new(2027,1,16),6200);
var periods=(await owner.ForecastAsync(account,split,new(2027,1,1),new(2027,1,31))).Periods;
Assert(periods.Count==2 && periods[0].Amount==1500 && periods[1].Amount==3200 && periods.All(x=>x.ReferenceDays==31),"mid-month split uses original denominator");
Assert(periods.Select(x=>x.EventKey).Distinct().Count()==1 && periods.Select(x=>x.InvoiceDate).Distinct().Count()==1,"price segments keep payment identity and invoice date");
var narrow=await owner.ForecastAsync(account,split,new(2027,1,20),new(2027,1,21));
Assert(narrow.Periods.Single().Amount==3200,"narrow search does not clip segment amount");
var next=await NewAgreement();var nextLine=await Line(next,3100);await Manual(next,nextLine,new(2027,1,16),6200,AgreementPriceTiming.NextPeriod);
var nextRows=(await owner.ForecastAsync(account,next,new(2027,1,1),new(2027,2,28))).Periods;
Assert(nextRows[0].Amount==3100 && nextRows[1].Amount==6200,"next full period keeps January price");
// Rounding allocation must equal rounded aggregate of unrounded segments.
var roundId=await NewAgreement();var roundLine=await Line(roundId,1);await Manual(roundId,roundLine,new(2027,1,16),1);
var rounded=(await owner.ForecastAsync(account,roundId,new(2027,1,1),new(2027,1,31))).Periods;
Assert(rounded.Count==2 && rounded.Sum(x=>x.Amount)==1,"rounding remainder allocated consistently");

var runs=await Task.WhenAll(owner.GenerateBasisAsync(account,split,AgreementDirection.Income,new(2027,1,1),new(2027,1,31)),owner.GenerateBasisAsync(account,split,AgreementDirection.Income,new(2027,1,1),new(2027,1,31)));
Assert(runs.Sum(x=>x.Created.Count)==1 && runs.Sum(x=>x.Existing.Count)==1,"parallel generation returns one active basis");
var basisId=runs.SelectMany(x=>x.Created).Single();var basis=await owner.GetBasisAsync(account,basisId);
Assert(!basis.Stale && Body(basis).Events.Single().Amount==4700 && Body(basis).Events.Single().Segments.Count==2,"draft snapshot retains both segments");
Assert((await owner.GenerateBasisAsync(account,split,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Count==0,"rerun idempotent");
await Task.WhenAll(owner.ApproveBasisAsync(account,basisId,1),owner.ApproveBasisAsync(account,basisId,1));
var approved=(await owner.GetBasisAsync(account,basisId)).Snapshots[0].DataJson;
await Expect<AgreementValidationException>(()=>owner.RegenerateBasisAsync(account,basisId,1,"No"),"approved basis cannot regenerate");
await Manual(split,splitLine,new(2027,1,16),9300);
var needs=await owner.GetBasisAsync(account,basisId);
Assert(needs.NeedsCorrection && needs.Snapshots[0].DataJson==approved,"retroactive price signals correction without changing original");
var c1s=await Task.WhenAll(owner.CreateCorrectionAsync(account,basisId,"Retroactive change"),owner.CreateCorrectionAsync(account,basisId,"Retroactive change"));
Assert(c1s[0]==c1s[1],"parallel correction creation returns one draft");
var correction=c1s[0];var cbody=Body(await owner.GetBasisAsync(account,correction));
Assert(cbody.Events.Single().Amount==1600 && cbody.Events.Single().PreviousAmount==4700 && cbody.Events.Single().TargetAmount==6300,"positive correction uses approved net");
await Task.WhenAll(owner.ApproveBasisAsync(account,correction,1),owner.ApproveBasisAsync(account,correction,1));
await Expect<AgreementValidationException>(()=>owner.CreateCorrectionAsync(account,basisId,"Rerun"),"same correction never generated twice");
await Manual(split,splitLine,new(2027,1,16),7750);
var c2=await owner.CreateCorrectionAsync(account,basisId,"Reduce price");var c2data=Body(await owner.GetBasisAsync(account,c2));
Assert(c2data.Events.Single().Amount==-800 && c2data.Events.Single().PreviousAmount==6300,"later negative correction accounts for earlier approved correction");
await owner.ApproveBasisAsync(account,c2,1);
Assert(!(await owner.GetBasisAsync(account,basisId)).NeedsCorrection,"net corrected total matches target");
await Expect<AgreementValidationException>(()=>owner.CancelBasisAsync(account,basisId,1,"Cancel"),"original cannot cancel with live corrections");
await owner.CancelBasisAsync(account,c2,1,"Undo correction");
Assert((await owner.GetBasisAsync(account,basisId)).NeedsCorrection,"cancelled correction removed from approved net");

var staleId=await NewAgreement();var staleLine=await Line(staleId);var draft=(await owner.GenerateBasisAsync(account,staleId,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
await Manual(staleId,staleLine,new(2027,1,1),1100);
await Expect<AgreementValidationException>(()=>owner.ApproveBasisAsync(account,draft,1),"stale draft cannot approve");
await owner.RegenerateBasisAsync(account,draft,1,"Review changed price");
var regenerated=await owner.GetBasisAsync(account,draft);
Assert(regenerated.Snapshots.Count==2 && Body(regenerated).Events.Single().Amount==1100 && !regenerated.Stale,"regeneration preserves before and after snapshots");
await Expect<AgreementValidationException>(()=>owner.ApproveBasisAsync(account,draft,1),"old revision cannot approve regenerated draft");
await owner.ApproveBasisAsync(account,draft,2);
await owner.CancelBasisAsync(account,draft,2,"Release claim");
var replacement=(await owner.GenerateBasisAsync(account,staleId,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
Assert(replacement!=draft,"cancellation releases original event for controlled regeneration");

var cost=await NewAgreement(AgreementDirection.Cost);await Line(cost,1200);
await Expect<AgreementValidationException>(()=>owner.GenerateBasisAsync(account,cost,AgreementDirection.Income,new(2027,1,1),new(2027,1,31)),"cost rejected by outgoing basis service");
var costBasis=(await owner.GenerateBasisAsync(account,cost,AgreementDirection.Cost,new(2027,1,1),new(2027,1,31))).Created.Single();
Assert((await owner.GetBasisAsync(account,costBasis)).Basis.Direction==AgreementDirection.Cost,"separate cost basis");
var arrears=await NewAgreement();await Line(arrears,12000,AgreementFrequency.Yearly,AgreementBillingTiming.Arrears);
Assert((await owner.PreviewBasisAsync(account,arrears,AgreementDirection.Income,new(2028,1,1),new(2028,1,1))).Income==12000,"generation filters annual arrears invoice date, not delivery overlap");
Assert((await owner.PreviewBasisAsync(account,arrears,AgreementDirection.Income,new(2027,1,1),new(2027,12,31))).Periods.Count==0,"no premature annual arrears basis");
// Existing software delivery: once plus twelve months included maintenance.
var software=await NewAgreement();var once=await Line(software,24000,AgreementFrequency.Once);
await owner.SaveLineAsync(account,software,null,new(){Revision=(await owner.GetAsync(account,software)).Revision,Name="Maintenance",StartDate=new(2027,1,1),FirstPayableDate=new(2028,1,1),AnchorDate=new(2027,1,1),PayableSourceLineId=once,PayableOffsetMonths=12,UnitPrice=12000,Frequency=AgreementFrequency.Yearly,Activate=true});
Assert((await owner.PreviewBasisAsync(account,software,AgreementDirection.Income,new(2027,1,1),new(2027,12,31))).Income==24000,"included maintenance creates no basis in first year");
Assert((await owner.PreviewBasisAsync(account,software,AgreementDirection.Income,new(2028,1,1),new(2028,12,31))).Income==12000,"maintenance starts after included year; no repeated once event");

await Expect<UnauthorizedAccessException>(()=>foreign.GetBasisAsync(otherAccount,basisId),"foreign account cannot read basis snapshot");
await Expect<UnauthorizedAccessException>(()=>stranger.GetBasisAsync(account,basisId),"same-account stranger cannot read basis");
await Expect<UnauthorizedAccessException>(()=>reader.GenerateBasisAsync(account,id,AgreementDirection.Income,new(2027,1,1),new(2027,1,31)),"reader cannot generate");
var allowedDraft=(await editor.GenerateBasisAsync(account,id,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
await Expect<UnauthorizedAccessException>(()=>editor.ApproveBasisAsync(account,allowedDraft,1),"editor cannot approve basis");
await Expect<UnauthorizedAccessException>(()=>reader.CreateCorrectionAsync(account,basisId,"No"),"reader cannot create correction");
await Expect<UnauthorizedAccessException>(()=>foreign.CancelBasisAsync(otherAccount,basisId,1,"No"),"foreign cannot cancel basis");
Assert(!(await stranger.ListBasisAsync(account,AgreementDirection.Income)).Any(),"list respects agreement grants");
// Relational protection independently of application checks.
await using(var db=factory.CreateDbContext())
{
 var claim=await db.AgreementBasisEventRecords.AsNoTracking().FirstAsync(x=>x.AccountId==account&&x.BasisId==basisId&&x.Active);
 db.AgreementBasisEventRecords.Add(new(){Id=Guid.NewGuid(),AccountId=account,AgreementId=split,BasisId=basisId,LineId=claim.LineId,EventKey=claim.EventKey});
 await Expect<DbUpdateException>(()=>db.SaveChangesAsync(),"database rejects duplicate active event");
}
await using(var db=factory.CreateDbContext())
{
 db.AgreementBasisEventRecords.Add(new(){Id=Guid.NewGuid(),AccountId=account,AgreementId=cost,BasisId=basisId,LineId=splitLine,EventKey="forged"});
 await Expect<DbUpdateException>(()=>db.SaveChangesAsync(),"database rejects cross-agreement claims");
}
await owner.UploadAsync(account,id,(await owner.GetAsync(account,id)).Revision,"contract.pdf",new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()),AgreementDocumentCategory.Contract,null);
var document=(await owner.GetAsync(account,id)).Documents.Single().Id;
var forgedRule=await Rule(cost,(await owner.GetLinesAsync(account,cost)).Lines.Single().Line.Id);forgedRule.Documents=[new(){DocumentId=document}];
await Expect<AgreementValidationException>(()=>SaveRule(cost,forgedRule),"rule document must belong to same agreement");
var edited=await owner.GetAsync(account,id);edited.Title="Changed title";await owner.UpdateAsync(account,id,edited);
await Expect<AgreementValidationException>(()=>owner.ApproveBasisAsync(account,allowedDraft,1),"header changes invalidate draft approval");
// Approved data does not read current title or corrected index values.
Assert((await owner.GetBasisAsync(account,basisId)).Snapshots[0].DataJson==approved,"approved original remains byte-identical through all corrections");

// Further audit paths: rules, line/price changes, manual index basis, explicit limits and revisions.
var ruleCase=await NewAgreement();var ruleLine=await Line(ruleCase);var rr=await Rule(ruleCase,ruleLine,indexId);rr.Documents=[new(){DocumentId=document}];
await Expect<AgreementValidationException>(()=>SaveRule(ruleCase,rr),"foreign-agreement document rejected for index rule");
await owner.UploadAsync(account,ruleCase,(await owner.GetAsync(account,ruleCase)).Revision,"terms.pdf",new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()),AgreementDocumentCategory.Contract,null);
var ruleDoc=(await owner.GetAsync(account,ruleCase)).Documents.Single().Id;
rr.Documents=[new(){DocumentId=ruleDoc}];await SaveRule(ruleCase,rr);
var rp=await owner.ProposeAdjustmentAsync(account,ruleCase,ruleLine,new(2028,1,1),null);
var newer=await Rule(ruleCase,ruleLine,indexId,new(2027,2,1));newer.SharePercent=70;newer.Documents=rr.Documents;await SaveRule(ruleCase,newer);
await Expect<AgreementValidationException>(()=>owner.DecideAdjustmentAsync(account,ruleCase,rp,true,"Rule changed"),"new effective rule makes proposal stale");
rp=await owner.ProposeAdjustmentAsync(account,ruleCase,ruleLine,new(2028,1,1),null);
await Manual(ruleCase,ruleLine,new(2027,6,1),1100);
await Expect<AgreementValidationException>(()=>owner.DecideAdjustmentAsync(account,ruleCase,rp,true,"Price changed"),"manual price makes proposal stale");
await Expect<AgreementValidationException>(()=>owner.ProposeAdjustmentAsync(account,ruleCase,ruleLine,new(2028,1,1),null),"manual price needs explicit matching index period");
newer=await Rule(ruleCase,ruleLine,indexId,new(2027,7,1));newer.Documents=rr.Documents;await SaveRule(ruleCase,newer);
rp=await owner.ProposeAdjustmentAsync(account,ruleCase,ruleLine,new(2028,1,1),null);await owner.DecideAdjustmentAsync(account,ruleCase,rp,true,"Checked document");
var docBasis=(await owner.GenerateBasisAsync(account,ruleCase,AgreementDirection.Income,new(2028,1,1),new(2028,1,31))).Created.Single();
var docBody=Body(await owner.GetBasisAsync(account,docBasis));
var docCalc=System.Text.Json.JsonSerializer.Deserialize<AgreementAdjustmentCalculation>(docBody.Events.Single().Adjustments.Single().CalculationJson)!;
Assert(docCalc.Rule.Documents.Single().DocumentId==ruleDoc && docCalc.ComparisonIndex!.Revision==3,"basis snapshots exact rule document and index revision");
await owner.ApproveBasisAsync(account,docBasis,1);
var lockedDoc=(await owner.GetBasisAsync(account,docBasis)).Snapshots[0].DataJson;
await admin.RecordIndexValueAsync(account,indexId,new(2028,1,1),107,null,"Another revision");
Assert((await owner.GetBasisAsync(account,docBasis)).Snapshots[0].DataJson==lockedDoc,"later index revision leaves approved basis unchanged");
var limitCase=await NewAgreement();var limitLine=await Line(limitCase);var lr=await Rule(limitCase,limitLine);lr.Kind=AgreementAdjustmentKind.Limit;lr.CeilingPercent=5;await SaveRule(limitCase,lr);
await Expect<AgreementValidationException>(()=>owner.ProposeAdjustmentAsync(account,limitCase,limitLine,new(2028,1,1),null),"service rejects empty limit choice");
await Expect<AgreementValidationException>(()=>owner.ProposeAdjustmentAsync(account,limitCase,limitLine,new(2028,1,1),6),"service rejects excessive limit choice");
var lp=await owner.ProposeAdjustmentAsync(account,limitCase,limitLine,new(2028,1,1),3);await owner.DecideAdjustmentAsync(account,limitCase,lp,true,"Chosen 3 percent");
Assert((await owner.GetLinesAsync(account,limitCase)).Lines.Single().Prices[0].UnitPrice==1030,"service applies explicit permitted rate");
var forbiddenChange=new SaveAgreementLineRequest{Revision=(await owner.GetAsync(account,staleId)).Revision,Name="Test line",StartDate=new(2027,1,1),FirstPayableDate=new(2027,1,1),Frequency=AgreementFrequency.Quarterly,EffectiveFrom=new(2027,4,1),UnitPrice=1000,Reason="New frequency"};
await Expect<AgreementValidationException>(()=>owner.SaveLineAsync(account,staleId,staleLine,forbiddenChange),"claimed event locks payment anchor and frequency");

await admin.RecordIndexValueAsync(account,indexId,new(2029,1,1),108,null,"Next year");
var nextAnnual=await owner.ProposeAdjustmentAsync(account,id,line,new(2029,1,1),null);
var nextAnnualCalc=System.Text.Json.JsonSerializer.Deserialize<AgreementAdjustmentCalculation>((await owner.GetProcessingAsync(account,id)).Proposals.Single(x=>x.Id==nextAnnual).CalculationJson)!;
Assert(nextAnnualCalc.BaseIndex!.Value==105 && nextAnnualCalc.NewPrice==1080,"latest basis carries exact revision associated with last applied price");
var originalCase=await NewAgreement();var originalLine=await Line(originalCase);var originalRule=await Rule(originalCase,originalLine,indexId);
originalRule.Basis=AgreementAdjustmentBasis.Original;originalRule.Addition=AgreementAdjustmentAddition.PercentagePoints;originalRule.AdditionPercent=2;
await SaveRule(originalCase,originalRule);
var originalProposal=await owner.ProposeAdjustmentAsync(account,originalCase,originalLine,new(2028,1,1),null);await owner.DecideAdjustmentAsync(account,originalCase,originalProposal,true,"First year");
var originalNext=await owner.ProposeAdjustmentAsync(account,originalCase,originalLine,new(2029,1,1),null);
Assert((await owner.GetProcessingAsync(account,originalCase)).Proposals.Single(x=>x.Id==originalNext).NewPrice==1100,"original basis does not compound earlier additions");
var leapRule=new AgreementAdjustmentRule{EffectiveFrom=new(2024,1,1),FirstAllowedDate=new(2024,2,29),AnchorDate=new(2024,2,29),Anchor=AgreementAdjustmentAnchor.AnnualDate,IntervalMonths=12};
Assert(AgreementAdjustmentCalculator.IsScheduled(leapRule,new(2024,1,1),new(2025,2,28)) && AgreementAdjustmentCalculator.IsScheduled(leapRule,new(2024,1,1),new(2028,2,29)),"annual adjustment anchor survives leap years");
Console.WriteLine("All adjustment/basis tests passed. No external transport or email used.");
static void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
static async Task Expect<T>(Func<Task> action,string name) where T:Exception{try{await action();}catch(T){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: "+name);}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options):IDbContextFactory<TenantPlatformDbContext>{public TenantPlatformDbContext CreateDbContext()=>new(options);}
sealed class UserContext(Guid user,Guid account):ICurrentUserContextService{public CurrentUserContext Current=>new(){IsAuthenticated=true,UserId=user,CurrentAccountId=account};}
sealed class TestClock(DateTimeOffset initial):TimeProvider{public DateTimeOffset Now{get;set;}=initial;public override DateTimeOffset GetUtcNow()=>Now;}
