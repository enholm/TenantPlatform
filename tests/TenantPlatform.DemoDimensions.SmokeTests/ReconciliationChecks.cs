using System.Collections;
using System.Globalization;
using System.Text.Json;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Initialization;

static class ReconciliationChecks
{
    static MagidaDemoGraph Fresh() => MagidaDemoData.Create(Verify.User, Verify.Time);
    static int Count(MagidaDemoGraph graph) => typeof(MagidaDemoGraph).GetProperties().Sum(p => ((ICollection)p.GetValue(graph)!).Count);
    static string Json(object value) => JsonSerializer.Serialize(value, Verify.Json);
    public static void Run()
    {
        var graph = Fresh(); var original = Json(graph);
        var empty = new MagidaDemoGraph();
        Verify.Equal(Verify.Element(graph), Verify.Element(graph.MissingFrom(empty)), "empty state creates the whole candidate graph");
        Verify.That(Count(graph.MissingFrom(Fresh())) == 0, "repeated full graph has no insertions");
        Verify.That(Json(graph) == original && Count(empty) == 0, "reconciliation does not mutate either input");
        foreach (var language in new[] { "nb-NO", "en-GB", "sv-SE" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            Verify.Equal(Verify.Element(graph), Verify.Element(Fresh()), "deterministic production generation " + language);
        }
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
        var otherUser = Guid.Parse("c0000000-0000-0000-0000-000000000987");
        var later = MagidaDemoData.Create(otherUser, Verify.Time.AddDays(4));
        Verify.That(later.Agreements.All(x => x.OwnerUserId == otherUser && x.CreatedByUserId == otherUser && x.UpdatedByUserId == otherUser && x.CreatedUtc == Verify.Time.AddDays(4) && x.UpdatedUtc == Verify.Time.AddDays(4)) && later.Indices.All(x => x.ActorUserId == otherUser) && later.IndexValues.All(x => x.ActorUserId == otherUser) && later.IndexSelections.All(x => x.ActorUserId == otherUser) && later.LineVersions.All(x => x.ActorUserId == otherUser) && later.PriceVersions.All(x => x.ActorUserId == otherUser), "resolved identity and one injected clock instant are used throughout all actor/owner fields");
        Verify.That(Count(later.MissingFrom(graph)) == 0, "later time or reused administrator identity does not change deterministic IDs");
        Verify.Section("deterministic full-graph generation and insert-only repetition without changing either input");

        var existing = Fresh();
        existing.Agreements[0].Title = "User title";
        existing.Agreements[1].Revision = otherUser;
        existing.Agreements[1].IsArchived = true;
        existing.Assignments.Clear(); // All explicitly removed choices remain removed.
        existing.Lines.Clear(); existing.LineVersions.Clear(); existing.PriceVersions.Clear(); existing.IndexSelections.Clear();
        existing.Dimensions[0].IsActive = false; existing.Dimensions[0].Name = "User dimension"; existing.Dimensions[0].LeafOnly = true;
        existing.Translations[0].Name = "User translation";
        existing.Rules[0].IsEnabled = false; existing.Rules[0].Revision = otherUser;
        existing.Values[0].Name = "User node"; existing.Values[0].ParentId = existing.Values.First(v => v.ParentId is null && v.Id != existing.Values[0].Id).Id;
        existing.Counterparties[0].Name = "User counterparty";
        existing.IndexValues[0].Value = 123m;
        var before = Json(existing); var batch = graph.MissingFrom(existing);
        Verify.That(Count(batch) == 0 && Json(existing) == before, "edited metadata, agreements, classifications, existing rows and financial history are never overwritten/repaired");
        Verify.That(batch.Assignments.Count == 0 && batch.Lines.Count == 0, "whole-agreement skip never recreates deleted assignments or financial children");
        Verify.Section("edited/archived/existing agreements and explicit removals remain untouched; no rewriting of existing rows");

        var partialMetadata = new MagidaDemoGraph
        {
            Dimensions = graph.Dimensions, Translations = graph.Translations.Take(2).ToList(),
            Values = graph.Values.Where(v => v.DimensionId == SeedIds.MagidaDimension(4)).ToList(), Rules = graph.Rules.Take(1).ToList(),
            Counterparties = graph.Counterparties, Indices = graph.Indices, IndexValues = graph.IndexValues
        };
        batch = graph.MissingFrom(partialMetadata);
        Verify.That(batch.Dimensions.Count == 0 && batch.Translations.Count == 2 && batch.Values.Count == 36 && batch.Rules.Count == 1 && batch.Agreements.Count == 300, "missing metadata rows and all absent agreement graphs are inserted without resetting existing configuration");
        var partial = Fresh();
        var retained = partial.Agreements.Take(137).Select(a => a.Id).ToHashSet();
        partial.Agreements.RemoveAll(x => !retained.Contains(x.Id));
        partial.Assignments.RemoveAll(x => !retained.Contains(x.AgreementId));
        partial.IndexSelections.RemoveAll(x => !retained.Contains(x.AgreementId));
        partial.Lines.RemoveAll(x => !retained.Contains(x.AgreementId));
        partial.LineVersions.RemoveAll(x => !retained.Contains(x.AgreementId));
        partial.PriceVersions.RemoveAll(x => !retained.Contains(x.AgreementId));
        batch = graph.MissingFrom(partial);
        Verify.That(batch.Agreements.Count == 163 && batch.Assignments.Count == 326 && batch.Lines.Count == 326 && batch.IndexSelections.Count == 163 && batch.LineVersions.Count == 326 && batch.PriceVersions.Count == 326 && batch.Assignments.All(x => !retained.Contains(x.AgreementId)), "partial whole-agreement state produces exactly the missing 163 complete graphs");
        Append(partial, batch);
        Verify.That(Count(graph.MissingFrom(partial)) == 0 && Count(partial) == Count(graph), "applying the pure insertion batch once is idempotent at coordination level");
        var semanticTranslation = Fresh(); semanticTranslation.Translations[0].Id = otherUser;
        Verify.That(Count(graph.MissingFrom(semanticTranslation)) == 0, "existing translation with non-seed ID and same language key is retained without duplication");
        Verify.Throws(() => graph.MissingFrom(new() { Dimensions = [new Dimension { Id = otherUser, AccountId = SeedIds.MagidaAccount, Code = "GEO" }] }), "dimension code collision is not silently remapped");
        var v = graph.Values[0];
        Verify.Throws(() => graph.MissingFrom(new() { Values = [new DimensionValue { Id = otherUser, AccountId = SeedIds.MagidaAccount, DimensionId = v.DimensionId, Code = v.Code }] }), "value code collision is not silently remapped");
        Verify.Section("missing-row coordination, natural-key preservation, partial whole graphs and explicit collision rejection");

        foreach (var property in typeof(MagidaDemoGraph).GetProperties())
        {
            var foreign = Fresh(); var row = ((IList)property.GetValue(foreign)!)[0]!;
            row.GetType().GetProperty("AccountId")!.SetValue(row, SeedIds.NordicPropertyAccount);
            Verify.Throws(() => graph.MissingFrom(foreign), "reject foreign existing row in " + property.Name);
            Verify.Throws(() => foreign.MissingFrom(new()), "reject foreign candidate row in " + property.Name);
        }
        Verify.Section("account boundary validated for every candidate/existing collection; no tenant mixing");
    }
    static void Append(MagidaDemoGraph existing, MagidaDemoGraph batch)
    {
        foreach (var property in typeof(MagidaDemoGraph).GetProperties())
        {
            var target = (IList)property.GetValue(existing)!;
            foreach (var item in (IEnumerable)property.GetValue(batch)!) target.Add(item);
        }
    }
}
