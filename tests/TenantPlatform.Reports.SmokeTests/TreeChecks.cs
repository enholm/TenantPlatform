using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class TreeChecks
{
    public static async Task RunAsync(Fixture f)
    {
        await AgreementsAsync(f);
        await LeasingAsync(f);
        await ValidationAsync(f);
    }

    private static async Task AgreementsAsync(Fixture f)
    {
        var service = f.Trees(f.Admin);
        var definition = AgreementDefinition();
        var root = await service.GetRootAsync(f.Account, definition);
        Check.That(root.Id.Type == ReportTreeNodeType.Account && root.Id.EntityId == f.Account && root.HasChildren,
            "agreement tree uses the account as a stable root");

        var first = await service.GetChildrenAsync(f.Account, definition, root.Id, 0, 1);
        var second = await service.GetChildrenAsync(f.Account, definition, root.Id, first.NextOffset!.Value, 1);
        Check.That(first.Items.Count == 1 && second.Items.Count == 1 && first.TotalCount == 4 && first.NextOffset == 1 &&
            first.Items[0].Id != second.Items[0].Id,
            "agreement children use explicit continuation without silent truncation");

        var alpha = (await service.SearchAsync(f.Account, definition, "Alpha contract")).Single();
        var lines = await service.GetChildrenAsync(f.Account, definition, alpha.Id);
        Check.That(lines.Items.Count == 1 && lines.Items[0].Id.EntityId == f.AlphaLine && lines.TotalCount == 1,
            "agreement line versions collapse to one stable LineId node");
        var lineHit = (await service.SearchAsync(f.Account, definition, "Fixture line"))
            .Single(x => x.Id.EntityId == f.AlphaLine);
        Check.That(lineHit.Ancestors.Count == 2 && lineHit.Ancestors[1].Id == alpha.Id,
            "agreement search returns the complete typed ancestor path for an unloaded line");
        var lineDetails = await service.GetNodeAsync(f.Account, definition, lines.Items[0].Id);
        Check.That(lineDetails.NavigationTarget == $"/agreements/{f.AgreementAlpha}/lines" &&
            lineDetails.Ancestors.Last().Id == alpha.Id,
            "agreement line details use the existing parent line route");

        var active = definition with { Filters = [new ReportValueFilterSelection(
            new AgreementReportFilterKey(AgreementReportFilter.Status),
            new ReportEnumValue<TenantPlatform.Core.Agreements.AgreementStatus>(TenantPlatform.Core.Agreements.AgreementStatus.Active))] };
        await Check.ThrowsAsync<ReportTreeException>(() => service.GetNodeAsync(f.Account, active,
            new(ReportModule.Agreements, ReportTreeNodeType.Agreement, f.AgreementBeta)),
            "a valid but filtered-out agreement node is rejected server-side");

        var reader = f.Trees(f.Reader);
        var readerRoot = await reader.GetRootAsync(f.Account, definition);
        var readerItems = await reader.GetChildrenAsync(f.Account, definition, readerRoot.Id);
        Check.That(readerItems.Items.Select(x => x.Id.EntityId).ToHashSet().SetEquals([f.AgreementAlpha, f.AgreementBeta]),
            "agreement relationship tree preserves resource grants without leaking other agreements");
        var dimension = definition with { Filters = [new ReportDimensionFilterSelection(
            new(ReportDimensionScope.Agreement, f.DimensionOne), f.Root, false)] };
        var dimensionRoot = await service.GetRootAsync(f.Account, dimension);
        var dimensionItems = await service.GetChildrenAsync(f.Account, dimension, dimensionRoot.Id);
        Check.That(dimensionItems.Items.Select(x => x.Id.EntityId).ToHashSet().SetEquals([f.AgreementAlpha, f.AgreementBeta]),
            "agreement tree reuses hierarchical parent-with-descendants dimension filtering");
    }

    private static async Task LeasingAsync(Fixture f)
    {
        var service = f.Trees(f.Admin);
        var definition = LeasingDefinition();
        var root = await service.GetRootAsync(f.Account, definition);
        var branches = await service.GetChildrenAsync(f.Account, definition, root.Id);
        var framework = branches.Items.Single(x => x.Id.Type == ReportTreeNodeType.LeasingFramework);
        var standalone = branches.Items.Single(x => x.Id.Type == ReportTreeNodeType.Synthetic);
        Check.That(framework.Id.EntityId == f.FrameworkAlpha && standalone.Name == "Uten rammeavtale" &&
            standalone.Id.Synthetic == ReportTreeSyntheticNode.LeasingWithoutFramework,
            "leasing tree puts relevant standalone acquisitions under the approved typed synthetic branch");

        var frameworkChildren = await service.GetChildrenAsync(f.Account, definition, framework.Id);
        var standaloneChildren = await service.GetChildrenAsync(f.Account, definition, standalone.Id);
        Check.That(frameworkChildren.Items.Single().Id.EntityId == f.AcquisitionChild &&
            standaloneChildren.Items.Select(x => x.Id.EntityId).ToHashSet().SetEquals([f.AcquisitionHidden, f.AcquisitionStandalone]),
            $"leasing framework and synthetic branches expose only their relevant acquisitions [framework={string.Join(',', frameworkChildren.Items.Select(x => x.Id.EntityId))}; standalone={string.Join(',', standaloneChildren.Items.Select(x => x.Id.EntityId))}]");

        var acquisition = frameworkChildren.Items.Single();
        var items = await service.GetChildrenAsync(f.Account, definition, acquisition.Id);
        var equipment = await service.GetChildrenAsync(f.Account, definition, items.Items.Single().Id);
        Check.That(items.Items.Single().Id.EntityId == f.ChildItem && equipment.Items.Single().Id.EntityId == f.ChildEquipment,
            "leasing tree uses actual item and EquipmentId nodes instead of item quantity");

        var hit = (await service.SearchAsync(f.Account, definition, "SERIAL-HIDDEN-BRANCH")).Single();
        Check.That(hit.Id.EntityId == f.ChildEquipment && hit.Ancestors.Select(x => x.Id.Type).SequenceEqual(
            [ReportTreeNodeType.Account, ReportTreeNodeType.LeasingFramework,
             ReportTreeNodeType.LeasingAcquisition, ReportTreeNodeType.LeasingItem]),
            "server search finds equipment in an unloaded branch with its complete ancestor path");
        var sameNames = await service.SearchAsync(f.Account, definition, "Shared item name");
        Check.That(sameNames.Count == 2 && sameNames.Select(x => x.Id).Distinct().Count() == 2 &&
            sameNames.Select(x => x.Ancestors[1].Id).Distinct().Count() == 2,
            "equal item names remain distinguishable by stable identity and different paths");

        var syntheticDetails = await service.GetNodeAsync(f.Account, definition, standalone.Id);
        var equipmentDetails = await service.GetNodeAsync(f.Account, definition, hit.Id);
        Check.That(syntheticDetails.NavigationTarget is null &&
            equipmentDetails.NavigationTarget == $"/leasing/equipment?acquisition={f.AcquisitionChild}",
            "synthetic nodes have no false route while equipment uses the existing filtered equipment route");

        var registered = definition with { Filters = [new ReportValueFilterSelection(
            new LeasingReportFilterKey(LeasingCatalogFilter.Status),
            new ReportEnumValue<LeasingCatalogStatus>(LeasingCatalogStatus.Registered))] };
        var registeredRoot = await service.GetRootAsync(f.Account, registered);
        var registeredBranches = await service.GetChildrenAsync(f.Account, registered, registeredRoot.Id);
        Check.That(registeredBranches.Items.Any(x => x.Id.Type == ReportTreeNodeType.Synthetic),
            "synthetic branch is present while a relevant authorised standalone acquisition exists");
        var ownerBRegistered = registered with { Filters = registered.Filters.Append(new ReportValueFilterSelection(
            new LeasingReportFilterKey(LeasingCatalogFilter.Owner), new ReportEntityValue(f.OwnerB))).ToList() };
        var ownerRoot = await service.GetRootAsync(f.Account, ownerBRegistered);
        var ownerBranches = await service.GetChildrenAsync(f.Account, ownerBRegistered, ownerRoot.Id);
        Check.That(ownerBranches.Items.All(x => x.Id.Type != ReportTreeNodeType.Synthetic),
            "empty synthetic branch is omitted after filters are applied");

        var standaloneOnly = definition with { Filters = [new ReportValueFilterSelection(
            new LeasingReportFilterKey(LeasingCatalogFilter.Kind),
            new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.StandaloneAcquisitions))] };
        var standaloneRoot = await service.GetRootAsync(f.Account, standaloneOnly);
        var standaloneBranches = await service.GetChildrenAsync(f.Account, standaloneOnly, standaloneRoot.Id);
        Check.That(standaloneBranches.Items.Count == 1 &&
            standaloneBranches.Items[0].Id.Synthetic == ReportTreeSyntheticNode.LeasingWithoutFramework,
            "leasing tree preserves standalone kind semantics without treating Leasing orders as another entity");

        var inheritedDimension = definition with { Filters = [new ReportDimensionFilterSelection(
            new(ReportDimensionScope.LeasingAgreement, f.DimensionTwo), f.DepartmentA, false)] };
        var dimensionRoot = await service.GetRootAsync(f.Account, inheritedDimension);
        var dimensionBranches = await service.GetChildrenAsync(f.Account, inheritedDimension, dimensionRoot.Id);
        Check.That(dimensionBranches.Items.Count == 1 &&
            dimensionBranches.Items[0].Id.Type == ReportTreeNodeType.LeasingFramework,
            "leasing tree filters acquisitions by the stored effective inherited agreement-dimension snapshot");
    }

    private static async Task ValidationAsync(Fixture f)
    {
        var agreements = f.Trees(f.Admin);
        var outside = AgreementDefinition() with
        {
            Period = new(ReportPeriodRule.Custom, new(2030, 1, 1), new(2030, 12, 31))
        };
        var root = await agreements.GetRootAsync(f.Account, outside);
        Check.That(!root.HasChildren && (await agreements.GetChildrenAsync(f.Account, outside, root.Id)).Items.Count == 0,
            "report period bounds the agreement selection before tree expansion");
        await Check.ThrowsAsync<ReportTreeException>(() => agreements.GetNodeAsync(f.Account, AgreementDefinition(),
            new(ReportModule.Leasing, ReportTreeNodeType.LeasingAcquisition, f.AcquisitionChild)),
            "a manipulated cross-module node identity is rejected");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Trees(f.Admin, f.ForeignAccount)
            .GetRootAsync(f.ForeignAccount, AgreementDefinition()),
            "tree entry points enforce selected-account membership and tenant isolation");
        await Check.ThrowsAsync<ReportTreeException>(() => agreements.SearchAsync(f.Account, AgreementDefinition(), " "),
            "tree search rejects an invalid empty server query");
    }

    private static ReportCardDefinition AgreementDefinition() => new(ReportModule.Agreements, 1, [],
        new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
        Visualization: ReportVisualization.RelationshipTree);

    private static ReportCardDefinition LeasingDefinition() => new(ReportModule.Leasing, 1, [],
        new(ReportPeriodRule.CurrentYear), ReportDateBasis.LeasingAcquisitionPurchaseDate,
        Visualization: ReportVisualization.RelationshipTree);
}
