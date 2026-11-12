using System.Globalization;
using System.Text.Json;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Initialization;
using TenantPlatform.Web.Services.Dimensions;

static class EquivalenceChecks
{
    public static void Run(JsonElement reference, MagidaDemoGraph graph)
    {
        var geo = graph.Dimensions.Single(x => x.Code == "GEO");
        var dept = graph.Dimensions.Single(x => x.Code == "DEPT");
        Verify.That(graph.Dimensions.Count == 2 && graph.Translations.Count == 4 && graph.Rules.Count == 2, "two ordinary dimensions, four translations and two Agreements rules");
        foreach (var (dimension, nb, en, number) in new[] { (geo, "Geografi", "Geography", 3), (dept, "Avdeling", "Department", 4) })
        {
            Verify.That(dimension.AccountId == SeedIds.MagidaAccount && dimension.Id == SeedIds.MagidaDimension(number) && dimension.Revision == dimension.Id, dimension.Code + " stable identity/account/revision");
            Verify.That(dimension.IsActive && !dimension.AllowMultiple && !dimension.LeafOnly && dimension.Name == nb, dimension.Code + " active, single, all levels selectable, Norwegian fallback");
            var rule = graph.Rules.Single(x => x.DimensionId == dimension.Id);
            Verify.That(rule.AccountId == dimension.AccountId && rule.IsEnabled && !rule.IsRequired && rule.Revision == dimension.Id, dimension.Code + " available/optional in Agreements");
            var translations = graph.Translations.Where(t => t.DimensionId == dimension.Id).ToList();
            Verify.That(translations.Count == 2 && translations.All(t => t.AccountId == dimension.AccountId) && translations.Single(t => t.LanguageCode == "nb-NO").Name == nb && translations.Single(t => t.LanguageCode == "en-GB").Name == en, dimension.Code + " two account-owned translations");
            // Attach copies only in the test to exercise the existing localization helper; production stores rows separately.
            var display = JsonSerializer.Deserialize<Dimension>(JsonSerializer.Serialize(dimension))!;
            display.Translations = translations;
            Verify.That(DimensionLocalization.Name(display, "en-GB", "nb-NO") == en && DimensionLocalization.Name(display, "sv-SE", "nb-NO") == nb && DimensionLocalization.Name(display, "sv-SE", null) == nb, dimension.Code + " English lookup and Swedish account/fallback behavior");
        }
        Verify.That(graph.GetType().GetProperties().All(p => !p.PropertyType.ToString().Contains("Leasing") && !p.PropertyType.ToString().Contains("GeographicArea") && !p.PropertyType.ToString().Contains("OrganizationElement")), "graph has no Leasing rules or legacy entity collections");
        Nodes(reference.GetProperty("geography"), geo, graph, 36);
        Nodes(reference.GetProperty("organization_structure"), dept, graph, 30);
        foreach (var language in new[] { "nb-NO", "en-GB", "sv-SE" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            foreach (var (key, dimension) in new[] { ("geography", geo), ("organization_structure", dept) })
            {
                // Frozen full paths provide independent alphabetical display expectations for this complete dataset.
                var expectedOrder = reference.GetProperty(key).EnumerateArray()
                    .OrderBy(n => n.GetProperty("path").GetString(), StringComparer.CurrentCultureIgnoreCase)
                    .Select(n => n.GetProperty("id").GetGuid());
                var current = DimensionHierarchy.Build(dimension, graph.Values);
                Verify.That(expectedOrder.SequenceEqual(current.Select(x => x.Id)), key + " full alphabetical UI order preserved: " + language);
            }
        }
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
        Verify.Section("all 66 node IDs, names, parents, levels, paths, generation order, codes and UI ordering match the frozen reference");
        Agreements(reference, graph, geo, dept);
        Collection(reference.GetProperty("counterparties"), graph.Counterparties, x => x.Id, "counterparty", false);
        Collection(reference.GetProperty("indices"), graph.Indices, x => x.Id, "index");
        Collection(reference.GetProperty("index_values"), graph.IndexValues, x => x.Id, "index value");
        Verify.Section("all 16 counterparties, four indices and eight index values match every reference field");
        Distribution(reference.GetProperty("distributions").GetProperty("geography"), geo, graph);
        Distribution(reference.GetProperty("distributions").GetProperty("organization_structure"), dept, graph);
        Verify.Section("explicit and inclusive unique-agreement distributions match at every GEO/DEPT node");
        Identity(graph);
    }
    static Guid? NullableId(JsonElement id) => id.ValueKind == JsonValueKind.Null ? null : id.GetGuid();
    static void Nodes(JsonElement reference, Dimension dimension, MagidaDemoGraph graph, int count)
    {
        var values = graph.Values.Where(x => x.DimensionId == dimension.Id).ToList();
        var hierarchy = DimensionHierarchy.Build(dimension, values);
        var expectedIds = reference.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        foreach (var value in values) Verify.That(expectedIds.Contains(value.Id), dimension.Code + " unexpected node " + value.Id);
        foreach (var node in reference.EnumerateArray())
        {
            var id = node.GetProperty("id").GetGuid(); var value = Verify.One(values, x => x.Id == id, dimension.Code + " node " + id); var row = Verify.One(hierarchy, x => x.Id == id, dimension.Code + " hierarchy node " + id);
            var context = dimension.Code + " node " + id;
            Verify.That(node.GetProperty("description").ValueKind == JsonValueKind.Null, context + ".no legacy description lost");
            var position = node.GetProperty("generation_order").GetInt32() - 1;
            Verify.That(position < values.Count && values[position].Id == id, context + ".generation order " + position);
            Verify.That(value.Name == node.GetProperty("name").GetString(), context + ".name");
            Verify.That(value.ParentId == NullableId(node.GetProperty("parent_id")), context + ".parent");
            Verify.That(value.AccountId == node.GetProperty("account_id").GetGuid() && value.AccountId == dimension.AccountId, context + ".account");
            Verify.That(value.IsActive == node.GetProperty("is_active").GetBoolean() && value.SortOrder == 0 && value.Revision == id, context + ".activity/order/revision");
            Verify.That(row.Depth + 1 == node.GetProperty("level").GetInt32() && row.Path == node.GetProperty("path").GetString() && row.Selectable, context + ".depth/path/selectable");
            var ancestors = Ancestors(id, values).Reverse().SkipLast(1);
            Verify.That(ancestors.SequenceEqual(node.GetProperty("ancestor_ids").EnumerateArray().Select(x => x.GetGuid())), context + ".ancestors");
            // Stable namespace/number-derived codes, independent of translated display labels.
            var number = Convert.ToInt32(id.ToString()[^12..], 16);
            var prefix = id.ToString().Substring(9, 4) switch { "0006" => "DIVISION", "0007" => "DEPARTMENT", "0008" => "CONTINENT", "0009" => "COUNTRY", "000a" => "CAPITAL", _ => throw new Exception(context) };
            Verify.That(value.Code == $"{prefix}-{number:000}", context + ".code");
        }
        Verify.That(values.Count == count && reference.GetArrayLength() == count, dimension.Code + " exact node count");
    }
    static void Agreements(JsonElement reference, MagidaDemoGraph graph, Dimension geo, Dimension dept)
    {
        foreach (var entry in reference.GetProperty("agreements").EnumerateArray())
        {
            var fields = entry.GetProperty("properties"); var id = fields.GetProperty("Id").GetGuid();
            var context = "agreement " + entry.GetProperty("number") + " " + id; var agreement = Verify.One(graph.Agreements, x => x.Id == id, context);
            Verify.Fields(fields, agreement, context, legacyReplaced: true);
            var assigned = graph.Assignments.Where(x => x.AgreementId == id).ToList();
            Verify.That(assigned.Count == 2 && assigned.All(x => x.AccountId == agreement.AccountId), context + ".exactly two account-owned explicit assignments");
            Verify.That(assigned.Single(x => x.DimensionId == geo.Id).ValueId == entry.GetProperty("geography_id").GetGuid(), context + ".GEO mapping");
            Verify.That(assigned.Single(x => x.DimensionId == dept.Id).ValueId == entry.GetProperty("organization_element_id").GetGuid(), context + ".DEPT mapping");
            foreach (var (dimension, field) in new[] { (geo, "geography_membership"), (dept, "organization_membership") })
            {
                var selected = assigned.Single(x => x.DimensionId == dimension.Id);
                Verify.That(!graph.Values.Any(x => x.ParentId == selected.ValueId), context + ".only explicit mapped leaf, no redundant ancestor assignment");
                Verify.That(Ancestors(selected.ValueId, graph.Values).Reverse().SequenceEqual(entry.GetProperty(field).EnumerateArray().Select(x => x.GetGuid())), context + ".computed membership " + field);
            }
            Verify.Fields(entry.GetProperty("index_selection"), Verify.One(graph.IndexSelections, x => x.AgreementId == id, context + ".index selection"), context + ".index selection");
            foreach (var line in entry.GetProperty("lines").EnumerateArray())
            {
                var lineId = line.GetProperty("entity").GetProperty("Id").GetGuid();
                Verify.Fields(line.GetProperty("entity"), Verify.One(graph.Lines, x => x.Id == lineId, context + ".line " + lineId), context + ".line " + lineId);
                Verify.Fields(line.GetProperty("version"), Verify.One(graph.LineVersions, x => x.LineId == lineId, context + ".line version " + lineId), context + ".line version " + lineId);
                Verify.Fields(line.GetProperty("price"), Verify.One(graph.PriceVersions, x => x.LineId == lineId, context + ".price version " + lineId), context + ".price version " + lineId);
            }
        }
        Verify.That(graph.Agreements.Count == 300 && graph.Assignments.Count == 600 && graph.IndexSelections.Count == 300 && graph.Lines.Count == 600 && graph.LineVersions.Count == 600 && graph.PriceVersions.Count == 600, "full agreement/assignment/financial graph counts");
        Verify.Section("every field/relationship of all 300 agreements and 600 lines/versions/prices matches; only legacy references replaced");
    }
    static void Collection<T>(JsonElement reference, List<T> actual, Func<T, Guid> id, string context, bool exact = true)
    {
        Verify.That(reference.GetArrayLength() == actual.Count, context + ".count");
        foreach (var entry in reference.EnumerateArray())
        {
            var key = entry.GetProperty("Id").GetGuid();
            Verify.Fields(entry, Verify.One(actual, x => id(x) == key, context + " " + key)!, context + " " + key, exact: exact);
        }
    }
    static void Distribution(JsonElement expected, Dimension dimension, MagidaDemoGraph graph)
    {
        var selections = graph.Assignments.Where(x => x.DimensionId == dimension.Id).ToList();
        foreach (var node in expected.EnumerateArray())
        {
            var id = node.GetProperty("id").GetGuid();
            var explicitCount = selections.Where(x => x.ValueId == id).Select(x => x.AgreementId).Distinct().Count();
            var inclusive = selections.Where(x => Ancestors(x.ValueId, graph.Values).Contains(id)).Select(x => x.AgreementId).Distinct().Count();
            Verify.That(explicitCount == node.GetProperty("explicit_agreements").GetInt32(), dimension.Code + " " + id + ".explicit distribution");
            Verify.That(inclusive == node.GetProperty("inclusive_unique_agreements").GetInt32(), dimension.Code + " " + id + ".inclusive unique distribution");
        }
    }
    static IEnumerable<Guid> Ancestors(Guid id, List<DimensionValue> values)
    {
        Guid? current = id; var visited = new HashSet<Guid>();
        while (current.HasValue)
        {
            Verify.That(visited.Add(current.Value), "acyclic ancestry at " + current);
            yield return current.Value;
            current = values.Single(x => x.Id == current.Value).ParentId;
        }
    }
    static void Identity(MagidaDemoGraph graph)
    {
        var ids = graph.Dimensions.Select(x => x.Id).Concat(graph.Translations.Select(x => x.Id)).Concat(graph.Values.Select(x => x.Id))
            .Concat(graph.Counterparties.Select(x => x.Id)).Concat(graph.Indices.Select(x => x.Id)).Concat(graph.IndexValues.Select(x => x.Id))
            .Concat(graph.Agreements.Select(x => x.Id)).Concat(graph.Assignments.Select(x => x.Id)).Concat(graph.IndexSelections.Select(x => x.Id))
            .Concat(graph.Lines.Select(x => x.Id)).Concat(graph.LineVersions.Select(x => x.Id)).Concat(graph.PriceVersions.Select(x => x.Id)).ToList();
        Verify.That(ids.Distinct().Count() == ids.Count && !ids.Contains(SeedIds.MagidaDimension(1)) && !ids.Contains(SeedIds.MagidaDimension(2)) && !ids.Contains(SeedIds.NordicPropertyAccount), "all candidate entity IDs unique and outside PROJ/EQUIP/Nordic namespaces");
        Verify.That(ids.All(id =>
        {
            var kind = Convert.ToInt32(id.ToString().Substring(9, 4), 16);
            return kind is < 19 or > 33 || id == SeedIds.MagidaDimension(3) || id == SeedIds.MagidaDimension(4);
        }), "candidate IDs do not occupy any Leasing namespace 19–33 except reserved dimension numbers 3/4");
        Verify.That(graph.Translations.Select(x => (x.DimensionId, x.LanguageCode)).Distinct().Count() == graph.Translations.Count && graph.Values.Select(x => (x.DimensionId, x.Code)).Distinct().Count() == graph.Values.Count && graph.Assignments.Select(x => (x.AgreementId, x.DimensionId)).Distinct().Count() == 600, "unique translation/code/one-value-per-dimension keys");
        Verify.Section("all stable IDs and natural keys are unique; no ID remapping or Leasing auto-activation");
    }
}
