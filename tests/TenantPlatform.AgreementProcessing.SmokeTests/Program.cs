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
    await db.GetService<IMigrator>().MigrateAsync("20260919214314_AddAgreementAdjustmentsAndBases");
    // Populate the immediately previous model, including groups, documents and conflicting rules.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO agreement_delivery_groups ("Id","AccountId","AgreementId","Name","CreatedUtc","CreatedByUserId")
        VALUES ({legacyLine},{account},{legacy},'Old group',{clock.Now},{ownerId});
        UPDATE agreement_line_versions SET "DeliveryGroupId"={legacyLine} WHERE "LineId"={legacyLine};
        INSERT INTO agreement_line_documents ("AccountId","AgreementId","LineVersionId","DocumentId")
        SELECT {account},{legacy},"Id",{legacyDocument} FROM agreement_line_versions WHERE "LineId"={legacyLine};
        INSERT INTO agreement_indices ("Id","AccountId","Code","Name","Description","Source","Resolution","RecordedUtc","ActorUserId")
        VALUES ({legacyLine},{account},'LEGACY','Legacy index','','Test',1,{clock.Now},{ownerId}),
               ({legacyPrice},{account},'LEGACY2','Other legacy index','','Test',1,{clock.Now},{ownerId});
        INSERT INTO agreement_index_values ("Id","AccountId","IndexId","Period","Revision","Value","RecordedUtc","ActorUserId","Reason")
        VALUES ({legacyDocument},{account},{legacyLine},'2020-01-01',1,100,{clock.Now},{ownerId},'Legacy base');
        INSERT INTO agreement_adjustment_rules
          ("Id","AccountId","AgreementId","LineId","EffectiveFrom","RecordedUtc","ActorUserId","Reason","Kind","IndexId","SharePercent","Addition","AdditionPercent","FixedPercent","AllowDecrease","Basis","BasePriceVersionId","BaseIndexPeriod","FirstAllowedDate","IntervalMonths","Anchor","AnchorDate","ComparisonOffsetMonths","PriceTiming")
        VALUES ({legacyLine},{account},{legacy},{legacyLine},'2020-01-01',{clock.Now},{ownerId},'Advanced legacy',1,{legacyLine},70,0,0,0,true,2,{legacyPrice},'2020-01-01','2021-01-01',1,1,'2021-01-01',0,2);
        """);
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        CREATE TEMP TABLE migration_cases (id uuid, line uuid, price uuid, label text, kind integer);
        INSERT INTO migration_cases VALUES (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'Migration pure',1),
            (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'Migration mixed',1),
            (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'Migration fixed',0);
        INSERT INTO agreements SELECT (jsonb_populate_record(NULL::agreements,to_jsonb(a) || jsonb_build_object('Id',c.id,'Title',c.label))).*
            FROM agreements a CROSS JOIN migration_cases c WHERE a."Id"={legacy};
        INSERT INTO agreement_lines SELECT (jsonb_populate_record(NULL::agreement_lines,to_jsonb(l) || jsonb_build_object('Id',c.line,'AgreementId',c.id))).*
            FROM agreement_lines l CROSS JOIN migration_cases c WHERE l."Id"={legacyLine};
        INSERT INTO agreement_line_versions SELECT (jsonb_populate_record(NULL::agreement_line_versions,to_jsonb(v) || jsonb_build_object('Id',gen_random_uuid(),'AgreementId',c.id,'LineId',c.line,'DeliveryGroupId',NULL))).*
            FROM agreement_line_versions v CROSS JOIN migration_cases c WHERE v."LineId"={legacyLine};
        INSERT INTO agreement_price_versions SELECT (jsonb_populate_record(NULL::agreement_price_versions,to_jsonb(p) || jsonb_build_object('Id',c.price,'AgreementId',c.id,'LineId',c.line))).*
            FROM agreement_price_versions p CROSS JOIN migration_cases c WHERE p."Id"={legacyPrice};
        INSERT INTO agreement_adjustment_rules SELECT (jsonb_populate_record(NULL::agreement_adjustment_rules,to_jsonb(r) || jsonb_build_object('Id',gen_random_uuid(),'AgreementId',c.id,'LineId',c.line,'BasePriceVersionId',c.price,'SharePercent',100,'Kind',c.kind,'IndexId',CASE WHEN c.kind=1 THEN {legacyLine} ELSE NULL END))).*
            FROM agreement_adjustment_rules r CROSS JOIN migration_cases c WHERE r."Id"={legacyLine};
        UPDATE migration_cases SET line=gen_random_uuid(),price=gen_random_uuid() WHERE label='Migration mixed';
        INSERT INTO agreement_lines SELECT (jsonb_populate_record(NULL::agreement_lines,to_jsonb(l) || jsonb_build_object('Id',c.line,'AgreementId',c.id))).*
            FROM agreement_lines l CROSS JOIN migration_cases c WHERE l."Id"={legacyLine} AND c.label='Migration mixed';
        INSERT INTO agreement_line_versions SELECT (jsonb_populate_record(NULL::agreement_line_versions,to_jsonb(v) || jsonb_build_object('Id',gen_random_uuid(),'AgreementId',c.id,'LineId',c.line,'DeliveryGroupId',NULL))).*
            FROM agreement_line_versions v CROSS JOIN migration_cases c WHERE v."LineId"={legacyLine} AND c.label='Migration mixed';
        INSERT INTO agreement_price_versions SELECT (jsonb_populate_record(NULL::agreement_price_versions,to_jsonb(p) || jsonb_build_object('Id',c.price,'AgreementId',c.id,'LineId',c.line))).*
            FROM agreement_price_versions p CROSS JOIN migration_cases c WHERE p."Id"={legacyPrice} AND c.label='Migration mixed';
        INSERT INTO agreement_adjustment_rules SELECT (jsonb_populate_record(NULL::agreement_adjustment_rules,to_jsonb(r) || jsonb_build_object('Id',gen_random_uuid(),'AgreementId',c.id,'LineId',c.line,'BasePriceVersionId',c.price,'SharePercent',100,'IndexId',{legacyPrice}))).*
            FROM agreement_adjustment_rules r CROSS JOIN migration_cases c WHERE r."Id"={legacyLine} AND c.label='Migration mixed';
        DROP TABLE migration_cases;
        """);
    await db.GetService<IMigrator>().MigrateAsync("20260920010742_AutomaticAgreementIndexPricing");
    // Simulate existing same-date revisions followed by a moved period and another correction.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO agreement_index_values ("Id","AccountId","IndexId","Period","Revision","Value","RecordedUtc","ActorUserId","Reason","PeriodKey","Superseded")
        VALUES (gen_random_uuid(),{account},{legacyLine},'2020-01-01',2,101,{clock.Now},{ownerId},'Old correction',{legacyDocument},false),
               (gen_random_uuid(),{account},{legacyLine},'2020-02-01',3,101,{clock.Now},{ownerId},'Moved period',{legacyDocument},false),
               (gen_random_uuid(),{account},{legacyLine},'2020-02-01',4,102,{clock.Now},{ownerId},'Latest correction',{legacyDocument},false),
               (gen_random_uuid(),{account},{legacyLine},'2021-01-01',1,105,{clock.Now},{ownerId},'Separate period',gen_random_uuid(),false);
        UPDATE agreement_index_values SET "Superseded"=true WHERE "Id"={legacyDocument};
        """);
    await db.Database.MigrateAsync();
    var migratedValues = await db.AgreementIndexValueRecords.AsNoTracking().Where(x=>x.AccountId==account && x.IndexId==legacyLine).ToListAsync();
    Assert(migratedValues.Where(x=>x.PeriodKey==legacyDocument).All(x=>x.Superseded==(x.Revision<4)),"migration supersedes all replaced revisions across period moves and preserves the current revision");
    Assert(!migratedValues.Single(x=>x.PeriodKey!=legacyDocument).Superseded,"migration leaves independent periods current");
    var preserved = await db.AgreementPriceVersions.SingleAsync(x=>x.AccountId==account && x.Id==legacyPrice);
    Assert(preserved.UnitPrice==123.4567m && preserved.Quantity==2 && !preserved.Independent && preserved.AdjustmentId==null,"phase 4 migration preserves existing price identity, precision and draft semantics");
    Assert(!db.Database.HasPendingModelChanges(), "model and generated migration match");
    var pure=await db.Agreements.SingleAsync(x=>x.AccountId==account && x.Title=="Migration pure");
    var mixed=await db.Agreements.SingleAsync(x=>x.AccountId==account && x.Title=="Migration mixed");
    var fixedAgreement=await db.Agreements.SingleAsync(x=>x.AccountId==account && x.Title=="Migration fixed");
    Assert(pure.IndexId==legacyLine && !pure.IndexSetupNeedsReview,"unambiguous full-index legacy setup migrates automatically");
    Assert(mixed.IndexId==null && mixed.IndexSetupNeedsReview,"mixed indices never choose arbitrary index");
    Assert(fixedAgreement.IndexId==null && !fixedAgreement.IndexSetupNeedsReview && !(await db.AgreementLineVersions.SingleAsync(x=>x.AgreementId==fixedAgreement.Id)).IndexRegulated,"fixed price migrates to no regulation");

}
await using (var db = factory.CreateDbContext())
{
    Assert((await db.AgreementLineVersions.Include(x => x.Documents).SingleAsync(x => x.LineId == legacyLine)).Documents.Single().DocumentId == legacyDocument,"group removal preserves line identity and document links");
    Assert(await db.AgreementAdjustmentRuleRecords.CountAsync(x => x.AgreementId == legacy)==1,"legacy calculation configuration retained only for audit");
}
var migrated = await admin.GetAsync(account, legacy);
Assert(migrated.IndexId==legacyLine && migrated.IndexSetupNeedsReview,"migration infers unique index but flags advanced rule");
Assert(AgreementAutomaticIndexCalculator.Project(true, [], [], [], [], []).Count==0,"ambiguous legacy setup disables only automatic index projection");
migrated.Title="Legacy still editable";await admin.UpdateAsync(account,legacy,migrated);
Assert((await admin.GetAsync(account,legacy)).Title=="Legacy still editable","review flag does not block ordinary agreement editing");
migrated=await admin.GetAsync(account,legacy);migrated.ResolveIndexSetup=true;await admin.UpdateAsync(account,legacy,migrated);
Assert(!(await admin.GetAsync(account,legacy)).IndexSetupNeedsReview,"explicit review resolves migration flag");
Assert(migrated.Direction == null && migrated.Currency == null && migrated.Documents.Single().Id == legacyDocument, "migration preserves contracts and document identity without guessing direction");


AutomaticIndexChecks.Run();
Assert(AgreementAdjustmentCalculator.Calculate(1000,100,104).Price==1040,"full index change");
Assert(AgreementAdjustmentCalculator.Calculate(1000,100,96).Price==960,"full negative index change");
SaveAgreementRequest AgreementRequest(AgreementDirection direction=AgreementDirection.Income)=>new(){Title="Processing agreement",Form=AgreementForm.Renewing,RenewalDate=new(2035,1,1),StartDate=new(2027,1,1),OwnerUserId=ownerId,CounterpartyOrganizationId=org,Direction=direction,Currency="NOK",Status=AgreementStatus.Active};
async Task<Guid> NewAgreement(AgreementDirection direction=AgreementDirection.Income)=>await admin.CreateAsync(account,AgreementRequest(direction));
SaveAgreementLineRequest LineRequest(bool regulated=false)=>new(){Name="Active line",StartDate=new(2027,1,1),FirstPayableDate=new(2027,1,1),AnchorDate=new(2027,1,1),EffectiveFrom=new(2027,1,1),UnitPrice=1000,Activate=true,IndexRegulated=regulated};
async Task<Guid> Save(Guid a,Guid? l,SaveAgreementLineRequest r){r.Revision=(await owner.GetAsync(account,a)).Revision;return await owner.SaveLineAsync(account,a,l,r);}
async Task SetIndex(Guid a,Guid? index){var r=await owner.GetAsync(account,a);r.IndexId=index;await owner.UpdateAsync(account,a,r);}
AgreementBasisData Body(AgreementBasisDetails d)=>System.Text.Json.JsonSerializer.Deserialize<AgreementBasisData>(d.Snapshots[0].DataJson)!;
var indexId=await admin.CreateIndexAsync(account,"CPI","Consumer prices","Test","Statistics",AgreementIndexResolution.Year);
await admin.RecordIndexValueAsync(account,indexId,new(2026,1,1),101,null,"Base");
await admin.RecordIndexValueAsync(account,indexId,new(2027,1,1),105,null,"Published");
await admin.RecordIndexValueAsync(account,indexId,new(2028,1,1),108,null,"Published");
var automaticRequest=AgreementRequest();automaticRequest.StartDate=new(2026,1,1);
var id=await admin.CreateAsync(account,automaticRequest);
await Expect<AgreementValidationException>(()=>Save(id,null,LineRequest(true)),"index required for regulated line");
await SetIndex(id,indexId);
var r=LineRequest(true);r.StartDate=r.FirstPayableDate=r.AnchorDate=r.EffectiveFrom=new(2026,1,1);
var line=await Save(id,null,r);var secondLine=await Save(id,null,r);
var fixedRequest=LineRequest();fixedRequest.StartDate=fixedRequest.FirstPayableDate=new(2026,1,1);
var fixedLine=await Save(id,null,fixedRequest);
await owner.SetAccessAsync(account,id,readerId,AgreementAccessLevel.Read,(await owner.GetAsync(account,id)).Revision);
await owner.SetAccessAsync(account,id,editorId,AgreementAccessLevel.Edit,(await owner.GetAsync(account,id)).Revision);
await Expect<AgreementValidationException>(()=>SetIndex(id,null),"cannot remove index while regulated lines exist");
var revisionBefore=(await owner.GetAsync(account,id)).Revision;
var forecast=await reader.ForecastAsync(account,id,new(2026,1,1),new(2027,12,31));
Assert(forecast.Periods.Where(x=>x.From.Year==2026).All(x=>x.UnitPrice==1000),"2026 forecast uses registered base price at index 101");
Assert(forecast.Periods.Where(x=>x.From.Year==2027 && x.LineId!=fixedLine).All(x=>x.UnitPrice==1039.60m),"2027 forecast automatically applies 105/101 to every regulated line");
Assert(forecast.Periods.Where(x=>x.LineId==fixedLine).All(x=>x.UnitPrice==1000 && x.IndexAdjustments.Count==0),"fixed line is excluded from automatic regulation");
Assert(forecast.Periods.First(x=>x.From.Year==2027 && x.LineId==line).IndexAdjustments.Single().BaseIndex.Value==101,"forecast exposes exact index calculation");
var repeated=await reader.ForecastAsync(account,id,new(2026,1,1),new(2027,12,31));
Assert(System.Text.Json.JsonSerializer.Serialize(repeated)==System.Text.Json.JsonSerializer.Serialize(forecast),"repeated forecasts are deterministic and never compound twice");
var narrow=await reader.ForecastAsync(account,id,new(2027,1,1),new(2027,1,31));
Assert(narrow.Periods.Single(x=>x.LineId==line).UnitPrice==1039.60m,"starting forecast in 2027 still includes prior index basis");
Assert((await owner.GetAsync(account,id)).Revision==revisionBefore && (await owner.GetLinesAsync(account,id)).Lines.All(x=>x.Prices.Count==1),"forecast is read-only: no new prices, proposals or approval steps");
await using (var db = factory.CreateDbContext())
    Assert(!await db.AgreementAdjustmentProposalRecords.AnyAsync(x=>x.AccountId==account && x.AgreementId==id),"forecast creates no adjustment proposals");
var preview=await reader.PreviewBasisAsync(account,id,AgreementDirection.Income,new(2027,1,1),new(2027,1,31));
Assert(preview.Income==narrow.Income,"basis preview and forecast use identical automatic prices");
var indexBasis=(await editor.GenerateBasisAsync(account,id,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
Assert(Body(await owner.GetBasisAsync(account,indexBasis)).Events.Single(x=>x.LineId==line).Segments.Single().UnitPrice==1039.60m,"generation automatically snapshots regulated price without per-line approval");
await Expect<UnauthorizedAccessException>(()=>editor.ApproveBasisAsync(account,indexBasis,1),"basis approval still requires its own permission");
await owner.ApproveBasisAsync(account,indexBasis,1);
var lockedIndexSnapshot=(await owner.GetBasisAsync(account,indexBasis)).Snapshots[0].DataJson;
var indexDraft=(await editor.GenerateBasisAsync(account,id,AgreementDirection.Income,new(2027,2,1),new(2027,2,28))).Created.Single();
Assert((await editor.GenerateBasisAsync(account,id,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Count==0,"repeated generation does not duplicate events or apply index twice");
var originalValue=(await admin.GetIndicesAsync(account)).Values.Single(x=>x.IndexId==indexId && x.Period==new DateOnly(2027,1,1));
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,indexId,new(2027,1,1),106,null,"Duplicate"),"duplicate periods require explicit edit");
await admin.RecordIndexValueAsync(account,indexId,new(2027,1,1),106,null,"Corrected value",originalValue.Id);
var correctedValues=(await admin.GetIndicesAsync(account)).Values.Where(x=>x.IndexId==indexId && x.Period==originalValue.Period).ToList();
Assert(correctedValues.Single(x=>x.Id==originalValue.Id).Superseded && !correctedValues.Single(x=>x.Revision==2).Superseded,"same-date correction supersedes the previous revision only");
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,indexId,new(2027,1,1),107,null,"Stale correction",originalValue.Id),"superseded same-date revision cannot be edited");
Assert((await owner.GetBasisAsync(account,indexDraft)).Stale,"index correction invalidates an unapproved draft automatically");
await Expect<AgreementValidationException>(()=>owner.ApproveBasisAsync(account,indexDraft,1),"stale index draft cannot be approved");
Assert((await owner.GetBasisAsync(account,indexBasis)).Snapshots[0].DataJson==lockedIndexSnapshot,"used value correction never rewrites an approved basis");
Assert((await owner.GetBasisAsync(account,indexBasis)).NeedsCorrection,"used value correction reports difference on approved basis");
var indexCorrection=await owner.CreateCorrectionAsync(account,indexBasis,"Index corrected");
Assert(Body(await owner.GetBasisAsync(account,indexCorrection)).Events.Sum(x=>x.Amount)==19.80m,"correction uses new index value once for both regulated lines");
await owner.ApproveBasisAsync(account,indexCorrection,1);
await owner.RegenerateBasisAsync(account,indexDraft,1,"New index revision");await owner.ApproveBasisAsync(account,indexDraft,2);
Assert(!(await owner.GetBasisAsync(account,indexBasis)).NeedsCorrection,"approved index correction reconciles the old basis");
Assert(Body(await owner.GetBasisAsync(account,indexDraft)).Events.Single(x=>x.LineId==line).Segments.Single().IndexAdjustments.Single().ComparisonIndex.Revision==2,"new basis keeps exact revised index value");
await admin.UpdateIndexAsync(account,indexId,"CPI2","Corrected name","Description","https://example.test/source",AgreementIndexResolution.Year);
Assert((await admin.GetIndicesAsync(account)).Indices.Single(x=>x.Id==indexId).Name=="Corrected name","index metadata editable");
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,indexId,new(2030,1,1),0,null,"Invalid"),"invalid index value rejected");
await Expect<UnauthorizedAccessException>(()=>owner.UpdateIndexAsync(account,indexId,"X","No","","No",AgreementIndexResolution.Year),"index editing requires account administrator");
await Expect<UnauthorizedAccessException>(()=>foreign.RecordIndexValueAsync(otherAccount,indexId,new(2030,1,1),110,null,"Foreign"),"cross-account index edit rejected");

// Quantity/schedule edits retain the price anchor; actual price edits reset it.
r.Quantity=2;r.EffectiveFrom=new(2027,3,1);r.Reason="Quantity correction";await Save(id,line,r);
Assert((await owner.ForecastAsync(account,id,new(2027,3,1),new(2027,3,31))).Periods.Single(x=>x.LineId==line).Amount==2099m,"quantity-only edit preserves automatic unit price");
r.Frequency=AgreementFrequency.Quarterly;r.EffectiveFrom=new(2027,4,1);await Save(id,line,r);
Assert((await owner.ForecastAsync(account,id,new(2027,4,1),new(2027,6,30))).Periods.Single(x=>x.LineId==line).UnitPrice==1049.50m,"schedule-only edit does not reset index basis");
r.UnitPrice=2000;r.EffectiveFrom=new(2027,7,1);r.Reason="Registered price corrected";await Save(id,line,r);
Assert((await owner.ForecastAsync(account,id,new(2027,7,1),new(2027,9,30))).Periods.Single(x=>x.LineId==line).UnitPrice==2000,"manual price correction becomes new anchor at its effective date");
Assert((await owner.ForecastAsync(account,id,new(2028,1,1),new(2028,3,31))).Periods.Single(x=>x.LineId==line).UnitPrice==2037.74m,"later index regulation uses corrected registered price");

// Changing the agreement's index never changes earlier forecast periods.
var alternate=await admin.CreateIndexAsync(account,"ALT","Alternative","","Local",AgreementIndexResolution.Year);
await admin.RecordIndexValueAsync(account,alternate,new(2027,1,1),200,null,"Base");
await admin.RecordIndexValueAsync(account,alternate,new(2028,1,1),220,null,"Next");
var beforeSwitch=await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,5,31));
var oldClock=clock.Now;clock.Now=new(2027,6,1,12,0,0,TimeSpan.Zero);
await SetIndex(id,alternate);clock.Now=oldClock;
var afterSwitch=await owner.ForecastAsync(account,id,new(2027,1,1),new(2027,5,31));
Assert(System.Text.Json.JsonSerializer.Serialize(beforeSwitch)==System.Text.Json.JsonSerializer.Serialize(afterSwitch),"index switch preserves prior forecast prices and index evidence");
Assert((await owner.ForecastAsync(account,id,new(2028,1,1),new(2028,1,31))).Periods.Single(x=>x.LineId==secondLine).UnitPrice==1154.45m,"all lines use the new agreement index only after the switch");
Assert((await owner.GetBasisAsync(account,indexBasis)).Snapshots[0].DataJson==lockedIndexSnapshot,"index switch preserves approved calculation byte for byte");

// Many lines need only one register entry, with no generated price rows or proposals.
var bulkRequest=AgreementRequest();bulkRequest.StartDate=new(2026,1,1);bulkRequest.IndexId=indexId;
var bulk=await admin.CreateAsync(account,bulkRequest);
for(var n=0;n<30;n++){var item=LineRequest(true);item.StartDate=item.FirstPayableDate=new(2026,1,1);await Save(bulk,null,item);}
var bulkForecast=await owner.ForecastAsync(account,bulk,new(2027,1,1),new(2027,1,31));
Assert(bulkForecast.Periods.Count==30 && bulkForecast.Periods.All(x=>x.UnitPrice==1049.50m),"all thirty regulated lines update automatically from one index entry");
Assert((await owner.GetLinesAsync(account,bulk)).Lines.All(x=>x.Prices.Count==1),"automatic pricing never writes a per-line price change");

// A price already regulated by the old workflow is a price anchor, not an extra multiplier.
var legacyAutoRequest=AgreementRequest();legacyAutoRequest.StartDate=new(2026,1,1);legacyAutoRequest.IndexId=indexId;
var legacyAuto=await admin.CreateAsync(account,legacyAutoRequest);
var legacyAutoLineRequest=LineRequest(true);legacyAutoLineRequest.StartDate=legacyAutoLineRequest.FirstPayableDate=new(2026,1,1);
var legacyAutoLine=await Save(legacyAuto,null,legacyAutoLineRequest);
var legacyData=(await owner.GetLinesAsync(account,legacyAuto)).Lines.Single();var legacyAdjustment=Guid.NewGuid();
var baseValue=(await admin.GetIndicesAsync(account)).Values.Single(x=>x.IndexId==indexId && x.Period==new DateOnly(2026,1,1));
await using(var db=factory.CreateDbContext())
{
    var calculation=new AgreementAdjustmentCalculation(null,legacyData.Prices[0],legacyData.Prices[0],legacyData.Versions[0],baseValue,originalValue,new(2027,1,1),new(2027,1,1),3.960396m,3.960396m,1039.60m,"legacy");
    db.AgreementAdjustmentProposalRecords.Add(new(){Id=legacyAdjustment,AccountId=account,AgreementId=legacyAuto,LineId=legacyAutoLine,IndexId=indexId,
        ScheduledDate=new(2027,1,1),EffectiveDate=new(2027,1,1),OldPrice=1000,NewPrice=1039.60m,Status=AgreementProposalStatus.Approved,
        RecordedUtc=clock.Now,ActorUserId=ownerId,CalculationJson=System.Text.Json.JsonSerializer.Serialize(calculation)});
    db.AgreementPriceVersions.Add(new(){Id=Guid.NewGuid(),AccountId=account,AgreementId=legacyAuto,LineId=legacyAutoLine,Sequence=2,Independent=true,AdjustmentId=legacyAdjustment,
        EffectiveFrom=new(2027,1,1),Quantity=1,UnitPrice=1039.60m,RecordedUtc=clock.Now,ActorUserId=ownerId});
    await db.SaveChangesAsync();
}
Assert((await owner.ForecastAsync(account,legacyAuto,new(2027,1,1),new(2027,1,31))).Periods.Single().UnitPrice==1039.60m,"previously approved index adjustment is never applied twice");
var legacyAutoBasis=(await owner.GenerateBasisAsync(account,legacyAuto,AgreementDirection.Income,new(2028,1,1),new(2028,1,31))).Created.Single();
var legacyAutoBody=Body(await owner.GetBasisAsync(account,legacyAutoBasis));
Assert(legacyAutoBody.Events.Single().Segments.Single().UnitPrice==1059.22m && legacyAutoBody.Events.Single().Adjustments.Single().Id==legacyAdjustment,
    "automatic continuation retains old adjustment evidence and only applies the next period");

// Changing a period's date moves its one current revision; it never creates a second multiplication.
var movedIndex=await admin.CreateIndexAsync(account,"MOVE","Moved period","","Local",AgreementIndexResolution.Month);
await admin.RecordIndexValueAsync(account,movedIndex,new(2026,1,1),101,null,"Base");
await admin.RecordIndexValueAsync(account,movedIndex,new(2027,1,1),105,null,"Next");
var movedRequest=AgreementRequest();movedRequest.StartDate=new(2026,1,1);movedRequest.IndexId=movedIndex;
var movedAgreement=await admin.CreateAsync(account,movedRequest);await Save(movedAgreement,null,legacyAutoLineRequest);
var movedValue=(await admin.GetIndicesAsync(account)).Values.First(x=>x.IndexId==movedIndex);
await admin.RecordIndexValueAsync(account,movedIndex,new(2027,2,1),105,null,"Corrected period",movedValue.Id);
var movedRevisions=(await admin.GetIndicesAsync(account)).Values.Where(x=>x.IndexId==movedIndex && x.Period.Year==2027).ToList();
Assert(movedRevisions.Single(x=>x.Id==movedValue.Id).Superseded && !movedRevisions.Single(x=>x.Revision==2).Superseded,"period move supersedes its original revision");
await admin.RecordIndexValueAsync(account,movedIndex,new(2027,2,1),105,null,"Same-date revision after move",movedRevisions.Single(x=>!x.Superseded).Id);
var finalMovedRevisions=(await admin.GetIndicesAsync(account)).Values.Where(x=>x.IndexId==movedIndex && x.Period.Year==2027).ToList();
Assert(finalMovedRevisions.Count==3 && finalMovedRevisions.All(x=>x.Superseded==(x.Revision<3)),"correction after move leaves only the newest revision current");
var movedForecast=await owner.ForecastAsync(account,movedAgreement,new(2027,1,1),new(2027,3,31));
Assert(movedForecast.Periods[0].UnitPrice==1000 && movedForecast.Periods.Skip(1).All(x=>x.UnitPrice==1039.60m),"moved index period takes effect once on the corrected date");
await Expect<AgreementValidationException>(()=>admin.RecordIndexValueAsync(account,movedIndex,new(2027,2,1),107,null,"Stale editor",movedValue.Id),"stale period editor still rejected");

// Active editing and basis snapshots, including documents and economic correction.
var editable=await NewAgreement();var editRequest=LineRequest();var editLine=await Save(editable,null,editRequest);
await owner.UploadAsync(account,editable,(await owner.GetAsync(account,editable)).Revision,"contract.pdf",new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()),AgreementDocumentCategory.Contract,null);
var document=(await owner.GetAsync(account,editable)).Documents.Single().Id;
editRequest.DocumentIds=[document]; editRequest.Name="Corrected name"; editRequest.Description="Corrected description";
await Save(editable,editLine,editRequest);
Assert((await owner.GetLinesAsync(account,editable)).Lines.Single().Versions[0].Documents.Single().DocumentId==document,"active line accepts documents and descriptive corrections without a reason");
Assert((await owner.GetLinesAsync(account,editable)).Lines.Single().Prices.Count==1,"descriptive edits do not create price versions");
var jan=(await owner.GenerateBasisAsync(account,editable,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
await owner.ApproveBasisAsync(account,jan,1);var approved=(await owner.GetBasisAsync(account,jan)).Snapshots[0].DataJson;
var feb=(await owner.GenerateBasisAsync(account,editable,AgreementDirection.Income,new(2027,2,1),new(2027,2,28))).Created.Single();
editRequest.DocumentIds=[];await Save(editable,editLine,editRequest);
Assert((await owner.GetLinesAsync(account,editable)).Lines.Single().Versions[0].Documents.Count==0,"active document reference removable");
Assert((await owner.GetBasisAsync(account,jan)).Snapshots[0].DataJson==approved && Body(await owner.GetBasisAsync(account,jan)).Events.Single().Segments.Single().DocumentIds.Contains(document),"approved document snapshot immutable");
editRequest.Quantity=2;editRequest.UnitPrice=600;editRequest.EffectiveFrom=new(2027,1,1);editRequest.Reason="Correct entry";
await Save(editable,editLine,editRequest);
Assert((await owner.GetBasisAsync(account,feb)).Stale,"affected draft marked stale");
await Expect<AgreementValidationException>(()=>owner.ApproveBasisAsync(account,feb,1),"stale draft cannot be approved");
Assert((await owner.GetBasisAsync(account,jan)).NeedsCorrection,"approved basis identifies correction need");
var correction=await owner.CreateCorrectionAsync(account,jan,"Correct price and quantity");
Assert(Body(await owner.GetBasisAsync(account,correction)).Events.Single().Amount==200,"existing correction flow calculates delta");
await owner.ApproveBasisAsync(account,correction,1);
Assert(!(await owner.GetBasisAsync(account,jan)).NeedsCorrection,"approved correction resolves difference");
await owner.RegenerateBasisAsync(account,feb,1,"Refresh");await owner.ApproveBasisAsync(account,feb,2);
Assert(Body(await owner.GetBasisAsync(account,feb)).Events.Single().Amount==1200,"controlled draft regeneration uses corrected price");
editRequest.Frequency=AgreementFrequency.Quarterly;editRequest.Anchor=AgreementAnchor.Date;editRequest.AnchorDate=new(2027,1,15);
editRequest.StartDate=new(2027,1,15);editRequest.EndDate=new(2029,12,31);editRequest.FirstPayableDate=new(2027,2,1);editRequest.BillingTiming=AgreementBillingTiming.Arrears;
await Save(editable,editLine,editRequest);
var updated=(await owner.GetLinesAsync(account,editable)).Lines.Single();
Assert(updated.Line.Id==editLine && updated.Versions[0].Frequency==AgreementFrequency.Quarterly && updated.Versions[0].FirstPayableDate==new DateOnly(2027,2,1) && updated.Versions[0].BillingTiming==AgreementBillingTiming.Arrears,"active frequency, dates, anchor and invoice timing editable with stable identity");
Assert((await owner.GetBasisAsync(account,jan)).Snapshots[0].DataJson==approved,"financial corrections never overwrite approved snapshot");
Assert((await owner.GetBasisAsync(account,jan)).NeedsCorrection,"changed event identity identifies correction");
var periodCorrection=await owner.CreateCorrectionAsync(account,jan,"Correct period definition");
Assert(Body(await owner.GetBasisAsync(account,periodCorrection)).Events.Single().Amount==-1200,"old event credited when corrected schedule replaces it");
await owner.ApproveBasisAsync(account,periodCorrection,1);
var once=LineRequest();once.Frequency=AgreementFrequency.Once;var onceId=await Save(editable,null,once);once.UnitPrice=1200;once.Reason="Correct one-off";await Save(editable,onceId,once);
Assert((await owner.GetLinesAsync(account,editable)).Lines.Single(x=>x.Line.Id==onceId).Prices[0].UnitPrice==1200,"active one-off line editable");
var backdated=await NewAgreement();var backdatedRequest=LineRequest();backdatedRequest.StartDate=new(2027,1,15);backdatedRequest.FirstPayableDate=backdatedRequest.StartDate;
var backdatedLine=await Save(backdated,null,backdatedRequest);backdatedRequest.StartDate=new(2027,1,1);backdatedRequest.FirstPayableDate=backdatedRequest.StartDate;backdatedRequest.EffectiveFrom=backdatedRequest.StartDate;backdatedRequest.UnitPrice=2000;backdatedRequest.Reason="Original start was wrong";
await Save(backdated,backdatedLine,backdatedRequest);
var correctedForecast=await owner.ForecastAsync(account,backdated,new(2027,1,1),new(2027,2,28));
Assert(correctedForecast.Income==4000 && correctedForecast.Periods.All(x=>x.UnitPrice==2000),"backdated correction does not resurrect later-dated older version");
var partial=await NewAgreement();var partialRequest=LineRequest();partialRequest.FirstPayableDate=new(2027,1,15);var partialLine=await Save(partial,null,partialRequest);
var partialPreview=await owner.PreviewBasisAsync(account,partial,AgreementDirection.Income,new(2027,1,1),new(2027,1,31));
Assert(partialPreview.Periods.Single().InvoiceDate==new DateOnly(2027,1,15),"included days do not move the payable invoice date");
partialRequest.EffectiveFrom=new(2027,1,20);partialRequest.UnitPrice=1200;partialRequest.Reason="Mid-period correction";await Save(partial,partialLine,partialRequest);
var partialBasis=(await owner.GenerateBasisAsync(account,partial,AgreementDirection.Income,new(2027,1,1),new(2027,1,31))).Created.Single();
Assert(Body(await owner.GetBasisAsync(account,partialBasis)).Events.Single().Segments.All(x=>x.InvoiceDate==new DateOnly(2027,1,15)),"mid-period edit creates one coherent invoice event");
var invalid=LineRequest();invalid.DocumentIds=[document];await Expect<AgreementValidationException>(()=>Save(id,null,invalid),"documents restricted to same agreement");
await Expect<UnauthorizedAccessException>(()=>reader.SaveLineAsync(account,id,line,LineRequest()),"read grant cannot edit active line");
await Expect<UnauthorizedAccessException>(()=>stranger.GetLinesAsync(account,id),"unrelated organization/member has no access");
await Expect<UnauthorizedAccessException>(()=>foreign.GetLinesAsync(otherAccount,id),"cross-account line access denied");
var foreignIndex=await foreign.CreateIndexAsync(otherAccount,"OTHER","Other","","Other",AgreementIndexResolution.Year);
await Expect<AgreementValidationException>(()=>SetIndex(id,foreignIndex),"agreement rejects foreign tenant index");
var cost=await NewAgreement(AgreementDirection.Cost);await SetIndex(cost,indexId);await Save(cost,null,LineRequest(true));
Assert((await owner.PreviewBasisAsync(account,cost,AgreementDirection.Cost,new(2028,1,1),new(2028,1,31))).Periods.Single().UnitPrice==1018.87m,"cost lines receive the same automatic index pricing");
await Expect<AgreementValidationException>(()=>owner.GenerateBasisAsync(account,cost,AgreementDirection.Income,new(2027,1,1),new(2027,1,31)),"cost cannot produce outgoing invoice basis");
var costBasis=(await owner.GenerateBasisAsync(account,cost,AgreementDirection.Cost,new(2027,1,1),new(2027,1,31))).Created.Single();
Assert((await owner.GetBasisAsync(account,costBasis)).Basis.Direction==AgreementDirection.Cost,"cost basis remains separate");
await Expect<UnauthorizedAccessException>(()=>foreign.GetBasisAsync(otherAccount,jan),"basis tenant isolation");


await ComponentEditorChecks.Automatic(owner, new UserContext(ownerId,account), bulk);

// Exercise the actual Razor editor handlers and inspect rendered HTML without a browser.
await ComponentEditorChecks.Run(owner, admin, new UserContext(ownerId,account), new UserContext(adminId,account), editable, editLine, indexId);

await BulkBasisChecks.Run(factory, admin, owner, reader, foreign, account, otherAccount, ownerId, readerId, org, foreignOrg, foreignId);
await BasisApprovalChecks.Run(factory, admin, owner, editor, foreign, account, otherAccount, ownerId, editorId, foreignId, foreignOrg, clock);
Console.WriteLine("All automatic index, basis, bulk generation, bulk approval and active editing tests passed.");
static void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
static async Task Expect<T>(Func<Task> action,string name) where T:Exception{try{await action();}catch(T){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: "+name);}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options):IDbContextFactory<TenantPlatformDbContext>{public TenantPlatformDbContext CreateDbContext()=>new(options);}
sealed class UserContext(Guid user,Guid account):ICurrentUserContextService{public CurrentUserContext Current=>new(){IsAuthenticated=true,UserId=user,CurrentAccountId=account};}
sealed class TestClock(DateTimeOffset initial):TimeProvider{public DateTimeOffset Now{get;set;}=initial;public override DateTimeOffset GetUtcNow()=>Now;}
