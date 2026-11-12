using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.Agreements;

static class AssignmentChecks
{
    public static async Task Run()
    {
        var f = new Fixture(); await f.Seed();
        var unclassified = await f.Create();
        Check.That((await f.Assignments(unclassified)).Count == 0, "legacy-only creation without configured requirements produces no generic assignments");
        await f.Rule(f.Single, true, true); await f.Rule(f.Multi);
        await Check.Invalid(() => f.Create(), "AgreementDimensionRequired", "create validates required when dimension input is omitted");
        await Check.Invalid(() => f.Create([]), "AgreementDimensionRequired", "empty outer patch list does not bypass create-required");
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single)]), "AgreementDimensionRequired", "explicit empty required create selection rejected");
        var id = await f.Create([Fixture.Patch(f.Single, f.A), Fixture.Patch(f.Multi, f.MRoot, f.M1)]);
        var initial = await f.Assignments(id);
        Check.That(initial.Count == 3 && initial.Any(x => x.ValueId == f.A) && initial.All(x => x.ValueId != f.Root), "only explicit values persist; no implicit ancestor assignment");
        Check.That(initial.Count(x => x.DimensionId == f.Multi) == 2, "multiple and parent-node assignments are accepted when configured");
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A, f.B)]), "AgreementDimensionSingleSelection", "single-select dimension rejects multiple values");
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.Root)]), "AgreementDimensionLeafOnly", "leaf-only dimension rejects new parent selection");
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A, f.A)]), "AgreementDimensionDuplicateValue", "duplicate values rejected before set deduplication");
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A), Fixture.Patch(f.Single, f.A)]), "AgreementDimensionDuplicatePatch", "repeated dimension patches are ambiguous and rejected");
        foreach (var patch in new[] { new AgreementDimensionPatch { DimensionId = f.Single }, Fixture.Patch(Guid.Empty, f.A), Fixture.Patch(f.Single, Guid.Empty), null! })
            await Check.Invalid(() => f.Create([patch]), "AgreementDimensionInvalidSelection", "null/missing patch values and empty identifiers are rejected");
        foreach (var patch in new[] { Fixture.Patch(f.Single, f.M1), Fixture.Patch(f.Single, f.ForeignValue), Fixture.Patch(f.Single, Guid.NewGuid()) })
            await Check.Invalid(() => f.Create([patch]), "AgreementDimensionInvalidSelection", "wrong-dimension, foreign-account and nonexistent values rejected");
        await f.Rule(f.Single, true, false);
        foreach (var patch in new[] { Fixture.Patch(f.Foreign, f.ForeignValue), Fixture.Patch(Guid.NewGuid(), f.A) })
            await Check.Invalid(() => f.Create([patch]), "AgreementDimensionInvalidSelection", "foreign or missing dimension rejected even without required rules");
        await f.Rule(f.Single, false);
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A)]), "AgreementDimensionUnavailable", "unavailable dimension cannot receive new assignments");
        await f.Rule(f.Single);
        await f.Mutate(async db => (await db.Dimensions.SingleAsync(x => x.Id == f.Single)).IsActive = false);
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A)]), "AgreementDimensionUnavailable", "inactive dimension cannot receive new assignments");
        await f.Mutate(async db => { (await db.Dimensions.SingleAsync(x => x.Id == f.Single)).IsActive = true; (await db.DimensionValues.SingleAsync(x => x.Id == f.A)).IsActive = false; });
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A)]), "AgreementDimensionInactiveValue", "inactive value cannot receive new assignment");
        var forgedHistory = f.Request();
        forgedHistory.DimensionChanges = JsonSerializer.Deserialize<List<AgreementDimensionPatch>>($"[{{\"DimensionId\":\"{f.Single}\",\"ValueIds\":[\"{f.A}\"],\"IsHistorical\":true}}]");
        await Check.Invalid(() => f.CreateRequest(forgedHistory), "AgreementDimensionInactiveValue", "client historical marker cannot exempt a new inactive assignment");
        await f.Mutate(async db => { (await db.DimensionValues.SingleAsync(x => x.Id == f.A)).IsActive = true; (await db.DimensionValues.SingleAsync(x => x.Id == f.Root)).IsActive = false; });
        await Check.Invalid(() => f.Create([Fixture.Patch(f.Single, f.A)]), "AgreementDimensionInactiveValue", "inactive ancestor blocks otherwise active leaf");
        await f.Mutate(async db => (await db.DimensionValues.SingleAsync(x => x.Id == f.Root)).IsActive = true);
        await Historical(f, id, unclassified);
        await RequiredConfiguration(f, unclassified);
        await Preservation(f);
        var missing = new Fixture(); await missing.Seed();
        var older = await missing.Create();
        await missing.Rule(missing.Single, true, true); await missing.Rule(missing.Multi);
        await missing.Update(older, [Fixture.Patch(missing.Multi, missing.M1)]);
        Check.That((await missing.Assignments(older)).Single().DimensionId == missing.Multi, "one dimension can change while another newly required dimension remains missing");
    }

    private static async Task Historical(Fixture f, Guid id, Guid oldMissing)
    {
        var singleRow = (await f.Assignments(id)).Single(x => x.DimensionId == f.Single);
        await f.Rule(f.Single, false); await f.Rule(f.Multi, true, true);
        await f.Mutate(async db => (await db.Dimensions.SingleAsync(x => x.Id == f.Single)).IsActive = false);
        var options = await f.Service(f.Owner).GetAgreementDimensionOptionsAsync(f.Account, id, true);
        var historical = options.Dimensions.Single(x => x.DimensionId == f.Single).Values.Single();
        Check.That(historical.Id == f.A && historical.IsHistorical && !historical.Selectable && historical.Path == "Root / Leaf A", "historical unavailable/inactive values remain readable with paths");
        var rowsBefore = Fixture.Json(await f.Assignments(id));
        var historiesBefore = (await f.Service().GetAgreementDimensionHistoryAsync(f.Account, id)).Count;
        await f.Update(id, edit: r => r.Title = "Unrelated edit", user: f.Owner);
        Check.That(Fixture.Json(await f.Assignments(id)) == rowsBefore, "omitted patches preserve historical assignment rows and IDs");
        await f.Update(oldMissing, edit: r => r.Title = "New required does not block old agreement");
        Check.That((await f.Assignments(oldMissing)).Count == 0, "new required rule neither blocks unrelated edit nor auto-creates assignments");
        await f.Update(oldMissing, [Fixture.Patch(f.Multi)]);
        Check.That((await f.Assignments(oldMissing)).Count == 0, "identical empty set is unchanged even after required was introduced");
        await f.Update(id, [Fixture.Patch(f.Single, f.A)]);
        Check.That((await f.Service().GetAgreementDimensionHistoryAsync(f.Account, id)).Count == historiesBefore, "identical historical set creates no assignment history and bypasses new-choice validation");
        await Check.Invalid(() => f.Update(id, [Fixture.Patch(f.Single, f.A, f.A)]), "AgreementDimensionDuplicateValue", "duplicate identical selections still rejected");
        await f.Update(id, [Fixture.Patch(f.Multi, f.M2)]);
        Check.That((await f.Assignments(id)).Single(x => x.DimensionId == f.Single).Id == singleRow.Id, "another dimension can change while historical values remain untouched");
        await f.Rule(f.Single, true, true);
        await f.Update(id, [Fixture.Patch(f.Single, f.A)]);
        await Check.Invalid(() => f.Update(id, [Fixture.Patch(f.Single)]), "AgreementDimensionRequired", "explicit removal of last historical required value rejected");
        await f.Rule(f.Single, false);
        await f.Update(id, [Fixture.Patch(f.Single)]);
        Check.That((await f.Assignments(id)).All(x => x.DimensionId != f.Single), "explicit optional removal works on inactive/unavailable dimension");
        // Multi already has M2. Keep an inactive retained value while adding a new active one.
        await f.Mutate(async db => (await db.DimensionValues.SingleAsync(x => x.Id == f.M2)).IsActive = false);
        var retained = (await f.Assignments(id)).Single(x => x.DimensionId == f.Multi);
        await f.Update(id, [Fixture.Patch(f.Multi, f.M2, f.M1)]);
        Check.That((await f.Assignments(id)).Single(x => x.ValueId == f.M2).Id == retained.Id, "retained inactive value plus new active addition is accepted without replacing retained row");
        await f.Update(id, [Fixture.Patch(f.Multi, f.MRoot, f.M1, f.M2)]);
        await f.Mutate(async db => { var dimension = await db.Dimensions.SingleAsync(x => x.Id == f.Multi); dimension.AllowMultiple = false; dimension.LeafOnly = true; });
        var oldSet = Fixture.Json(await f.Assignments(id));
        await f.Update(id, [Fixture.Patch(f.Multi, f.M2, f.MRoot, f.M1)]);
        Check.That(Fixture.Json(await f.Assignments(id)) == oldSet, "identical reordered historical set survives later single/leaf-only rule changes");
        await Check.Invalid(() => f.Update(id, [Fixture.Patch(f.Multi, f.MRoot, f.M1)]), "AgreementDimensionSingleSelection", "real change must satisfy current cardinality on entire resulting set");
        await f.Update(id, [Fixture.Patch(f.Multi, f.MRoot)]);
        await f.Mutate(async db => (await db.Dimensions.SingleAsync(x => x.Id == f.Multi)).AllowMultiple = true);
        await f.Update(id, [Fixture.Patch(f.Multi, f.MRoot, f.M1)]);
        Check.That((await f.Assignments(id)).Any(x => x.ValueId == f.MRoot), "retained historical parent is preserved when adding a valid leaf after LeafOnly change");
        await Check.Invalid(() => f.Update(id, [Fixture.Patch(f.Multi)]), "AgreementDimensionRequired", "real edit enforces required even on an old agreement");
    }

    private static async Task RequiredConfiguration(Fixture f, Guid oldMissing)
    {
        await f.Rule(f.Single, true, true);
        await Check.Invalid(() => f.Create(), "AgreementDimensionRequiredConfiguration", "inactive required dimension is a configuration error, not optional");
        await f.Mutate(async db => { (await db.Dimensions.SingleAsync(x => x.Id == f.Single)).IsActive = true; (await db.DimensionValues.SingleAsync(x => x.Id == f.A)).IsActive = false; (await db.DimensionValues.SingleAsync(x => x.Id == f.B)).IsActive = false; });
        await Check.Invalid(() => f.Create(), "AgreementDimensionRequiredConfiguration", "required leaf-only dimension without active leaves blocks new creation");
        await f.Update(oldMissing, edit: r => r.Description = "Still editable");
        Check.That((await f.Assignments(oldMissing)).Count == 0, "invalid required configuration does not invalidate unrelated edits of older agreements");
        await f.Rule(f.Single, false); await f.Rule(f.Multi, false);
    }

    private static async Task Preservation(Fixture f)
    {
        await f.Rule(f.Multi);
        var id = await f.Create();
        await f.Mutate(db =>
        {
            var line = Guid.NewGuid();
            db.AgreementLines.Add(new AgreementLine { Id = line, AccountId = f.Account, AgreementId = id, ActivatedUtc = Fixture.Now });
            db.AgreementLineVersions.Add(new AgreementLineVersion { Id = Guid.NewGuid(), AccountId = f.Account, AgreementId = id, LineId = line,
                Sequence = 1, Name = "Preserved financial line", EffectiveFrom = new(2026, 1, 1), StartDate = new(2026, 1, 1),
                FirstPayableDate = new(2026, 1, 1), Frequency = AgreementFrequency.Monthly, Anchor = AgreementAnchor.Calendar,
                AnchorDate = new(1, 1, 1), Status = AgreementLineStatus.Active, RecordedUtc = Fixture.Now, ActorUserId = f.Admin });
            db.AgreementPriceVersions.Add(new AgreementPriceVersion { Id = Guid.NewGuid(), AccountId = f.Account, AgreementId = id, LineId = line,
                Sequence = 1, EffectiveFrom = new(2026, 1, 1), Quantity = 7.5m, UnitPrice = 129.95m, RecordedUtc = Fixture.Now, ActorUserId = f.Admin });
            return Task.CompletedTask;
        });
        await using var beforeDb = f.Open();
        var before = JsonSerializer.SerializeToNode(await beforeDb.Agreements.AsNoTracking().SingleAsync(x => x.Id == id))!.AsObject();
        var beforeLines = Fixture.Json(await beforeDb.AgreementLines.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync());
        var beforePrices = Fixture.Json(await beforeDb.AgreementPriceVersions.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync());
        var beforeVersions = Fixture.Json(await beforeDb.AgreementLineVersions.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync());
        await f.Update(id, [Fixture.Patch(f.Multi, f.M1)]);
        await using var afterDb = f.Open();
        var after = JsonSerializer.SerializeToNode(await afterDb.Agreements.AsNoTracking().SingleAsync(x => x.Id == id))!.AsObject();
        Check.That(before["Revision"]!.ToString() != after["Revision"]!.ToString(), "assignment changes advance agreement revision");
        foreach (var key in new[] { "Revision", "UpdatedUtc", "UpdatedByUserId" }) { before.Remove(key); after.Remove(key); }
        Check.That(JsonNode.DeepEquals(before, after), "all unrelated agreement properties, legacy links, dates, notice and financial settings are preserved");
        Check.That(Fixture.Json(await afterDb.AgreementLines.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync()) == beforeLines, "existing financial line identities and activation remain unchanged");
        Check.That(Fixture.Json(await afterDb.AgreementPriceVersions.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync()) == beforePrices &&
            Fixture.Json(await afterDb.AgreementLineVersions.AsNoTracking().Where(x => x.AgreementId == id).ToListAsync()) == beforeVersions,
            "financial version histories, quantities, prices and dates remain byte-equivalent");
        Check.That(await afterDb.AuditLogs.AnyAsync(x => x.AccountId == f.Account && x.EntityType == nameof(AgreementDimensionAssignment) && x.Action == "Added"), "new assignments are audited through the existing interceptor");
        var history = (await f.Service(f.Owner).GetAgreementDimensionHistoryAsync(f.Account, id)).Single();
        Check.That(history.ActorUserId == f.Admin && history.RecordedUtc == Fixture.Now && history.Before.Single().Values.Count == 0 && history.After.Single().Values.Single().Path == "Multi root / Multi 1",
            "agreement classification history contains immutable IDs, actor, time and complete explicit paths");
        var historyJson = Fixture.Json(history);
        await f.Mutate(async db => (await db.DimensionValues.SingleAsync(x => x.Id == f.M1)).Name = "Renamed later");
        Check.That(Fixture.Json((await f.Service().GetAgreementDimensionHistoryAsync(f.Account, id)).Single()) == historyJson, "later register edits do not rewrite classification history");
        var stale = await f.Service().GetAsync(f.Account, id);
        await f.Update(id, edit: r => r.Title = "Next revision");
        stale.DimensionChanges = [Fixture.Patch(f.Multi, f.MRoot)];
        var rowsBefore = Fixture.Json(await f.Assignments(id));
        await Check.Invalid(() => f.UpdateRequest(id, stale), "AgreementConcurrencyConflict", "stale agreement revision rejects classification change");
        Check.That(Fixture.Json(await f.Assignments(id)) == rowsBefore && (await f.Service().GetAgreementDimensionHistoryAsync(f.Account, id)).Count == 1, "stale change writes neither assignments nor history");
        await using (var staleDb = f.Open())
        {
            await staleDb.Agreements.SingleAsync(x => x.Id == id);
            var oldInput = await f.Service().GetAsync(f.Account, id);
            await f.Update(id, edit: r => r.Title = "Concurrent winner");
            oldInput.Title = "Stale tracked writer";
            await Check.Invalid(() => (Task)Fixture.Invoke(f.Service(), "UpdateAgreementCoreAsync", staleDb, f.Account, id, oldInput, CancellationToken.None),
                "AgreementConcurrencyConflict", "EF detects stale tracked parent at SaveChanges and service translates conflict (InMemory only)");
            Check.That((await f.Service().GetAsync(f.Account, id)).Title == "Concurrent winner", "stale tracked writer does not overwrite the winning agreement");
        }
        // Validation must finish for all dimensions before applying the first patch.
        var invalidBatch = new List<AgreementDimensionPatch> { Fixture.Patch(f.Multi), Fixture.Patch(f.Single, f.A) };
        await Check.Invalid(() => f.Update(id, invalidBatch), "AgreementDimensionUnavailable", "later invalid patch rejects whole batch before assignment mutation");
        Check.That(Fixture.Json(await f.Assignments(id)) == rowsBefore, "invalid multi-dimension batch leaves persisted selection set unchanged");
    }
}
