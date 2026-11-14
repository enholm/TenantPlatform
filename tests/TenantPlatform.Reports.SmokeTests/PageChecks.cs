using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class PageChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var ownerService = f.Pages(f.OwnerA);
        var page = await ownerService.CreateAsync(f.Account, new("My report"));
        Check.That(page.OwnerUserId == f.OwnerA && page.Cards.Count == 0,
            "an eligible member creates a private empty report page owned by the server-side actor");
        Check.That((await ownerService.GetAsync(f.Account, page.Id)).Id == page.Id,
            "the owner reads the private report page by stable id");
        Check.That((await f.Pages(f.OwnerB).ListAsync(f.Account)).All(x => x.Id != page.Id),
            "ordinary page listing excludes another user's private pages");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Pages(f.OwnerB).GetAsync(f.Account, page.Id),
            "another ordinary member cannot read a private report page");
        Check.That((await f.Pages(f.Admin).GetAsync(f.Account, page.Id)).OwnerUserId == f.OwnerA,
            "an account administrator reads a report page in the same account without changing ownership");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Pages(f.Admin, f.ForeignAccount)
            .GetAsync(f.ForeignAccount, page.Id), "cross-account page reads are rejected");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Pages(f.Admin, f.ForeignAccount)
            .DeleteAsync(f.ForeignAccount, page.Id, page.Revision),
            "cross-account mutation and forged account context are rejected");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Pages(f.NoModuleAccess)
            .CreateAsync(f.Account, new("Forbidden")), "an active member without Agreements or Leasing access cannot create pages");

        var definition = AgreementDefinition("alpha");
        var saved = (await ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(page.Id, page.Revision, "Renamed report",
            [
                new(null, "Agreement card", definition, 1, 1, 6, 4, 0)
            ])
        ]))).Single();
        var card = saved.Cards.Single();
        Check.That(saved.Id == page.Id && saved.Name == "Renamed report" && saved.Revision != page.Revision,
            "page rename keeps the stable id and advances revision");
        Check.That(DefinitionEqual(card.Definition, definition) && card.GridColumn == 1 && card.GridWidth == 6 && card.GridHeight == 4,
            "supported filter, period, grouping and layout roundtrip through persistence");

        var second = await ownerService.CreateAsync(f.Account, new("Second report"));
        var moved = await ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(saved.Id, saved.Revision, saved.Name, []),
            new(second.Id, second.Revision, second.Name,
            [
                Edit(card)
            ])
        ]));
        var firstAfterMove = moved.Single(x => x.Id == saved.Id);
        var secondAfterMove = moved.Single(x => x.Id == second.Id);
        Check.That(firstAfterMove.Cards.Count == 0 && secondAfterMove.Cards.Single().Id == card.Id,
            "a workspace save atomically moves a card between pages while preserving its id");

        var staleFirst = firstAfterMove;
        var changedFirst = (await ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(staleFirst.Id, staleFirst.Revision, "Concurrent name", [])
        ]))).Single();
        await Check.ThrowsAsync<ReportPageConflictException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(staleFirst.Id, staleFirst.Revision, "Must not persist", []),
            new(secondAfterMove.Id, secondAfterMove.Revision, "Also must not persist", [Edit(card)])
        ])), "stale workspace revision returns a dedicated conflict");
        var afterConflict = await ownerService.GetAsync(f.Account, secondAfterMove.Id);
        Check.That(afterConflict.Name == secondAfterMove.Name && afterConflict.Cards.Single().Id == card.Id &&
            (await ownerService.GetAsync(f.Account, changedFirst.Id)).Name == "Concurrent name",
            "a stale multi-page workspace leaves every page unchanged");

        await Check.ThrowsAsync<ReportPageValidationException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(afterConflict.Id, afterConflict.Revision, afterConflict.Name,
            [new(Guid.NewGuid(), "Forged", definition, 1, 1, 6, 4, 0)])
        ])), "unknown or foreign card ids are rejected");
        await Check.ThrowsAsync<ReportPageValidationException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(afterConflict.Id, afterConflict.Revision, afterConflict.Name,
            [new(card.Id, card.Name, definition, 0, 1, 6, 4, 0)])
        ])), "invalid card layout is rejected before mutation");
        var invalidFilter = definition with
        {
            Filters =
            [
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Owner),
                    new ReportEntityValue(Guid.NewGuid()))
            ]
        };
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(afterConflict.Id, afterConflict.Revision, afterConflict.Name,
            [new(card.Id, card.Name, invalidFilter, 1, 1, 6, 4, 0)])
        ])), "page save validates filter identifiers and values against the authorized module catalog");
        var invalidDisplay = definition with { DisplayJson = "{\"titlePlacement\":\"diagonal\"}" };
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(afterConflict.Id, afterConflict.Revision, afterConflict.Name,
            [new(card.Id, card.Name, invalidDisplay, 1, 1, 6, 4, 0)])
        ])), "page save rejects an unknown explicit title placement");
        var unsupported = definition with { Aggregation = ReportAggregation.Sum };
        await Check.ThrowsAsync<ReportPageValidationException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(afterConflict.Id, afterConflict.Revision, afterConflict.Name,
            [new(card.Id, card.Name, unsupported, 1, 1, 6, 4, 0)])
        ])), "incompatible aggregation is rejected instead of accepted by assumption");

        var adminCopy = await f.Pages(f.Admin).DuplicateAsync(f.Account, afterConflict.Id, afterConflict.Revision);
        Check.That(adminCopy.Id != afterConflict.Id && adminCopy.OwnerUserId == f.Admin &&
            adminCopy.Cards.Single().Id != card.Id && DefinitionEqual(adminCopy.Cards.Single().Definition, card.Definition),
            "administrator duplication creates an independent deep copy owned by the copying user");
        await f.Pages(f.Admin).DeleteAsync(f.Account, adminCopy.Id, adminCopy.Revision);
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Pages(f.Admin).GetAsync(f.Account, adminCopy.Id),
            "deleting a page explicitly removes the page and its copied cards");

        await RemoveAgreementAccessAsync(f);
        var preserved = await ownerService.GetAsync(f.Account, afterConflict.Id);
        var preservedSave = (await ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(preserved.Id, preserved.Revision, "Kept inaccessible card", [Edit(preserved.Cards.Single())])
        ]))).Single();
        Check.That(DefinitionEqual(preservedSave.Cards.Single().Definition, preserved.Cards.Single().Definition),
            "a card from a module whose access was lost can be preserved unchanged");
        var changedDefinition = preserved.Cards.Single().Definition with
        {
            Filters =
            [
                new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search),
                    new ReportTextValue("changed"))
            ]
        };
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => ownerService.SaveWorkspaceAsync(f.Account, new(
        [
            new(preservedSave.Id, preservedSave.Revision, preservedSave.Name,
            [new(card.Id, card.Name, changedDefinition, 1, 1, 6, 4, 0)])
        ])), "lost module access cannot be bypassed by modifying the stored card definition");

        await RemoveMembershipAsync(f, f.OwnerA);
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => ownerService.ListAsync(f.Account),
            "removed account membership revokes report page access");
    }

    private static ReportCardDefinition AgreementDefinition(string search) => new(
        ReportModule.Agreements,
        1,
        [
            new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search),
                new ReportTextValue(search))
        ],
        new(ReportPeriodRule.CurrentYear),
        ReportDateBasis.AgreementPeriodOverlap,
        ReportMetric.AgreementCount,
        ReportAggregation.CountDistinct,
        Grouping: new AgreementReportGroupingKey(AgreementReportGrouping.Type),
        Visualization: ReportVisualization.Bar,
        DisplayJson: "{\"showLegend\":false,\"titlePlacement\":\"center\"}");

    private static ReportCardEdit Edit(ReportCardDto card) => new(card.Id, card.Name, card.Definition,
        card.GridColumn, card.GridRow, card.GridWidth, card.GridHeight, card.SortOrder);

    private static bool DefinitionEqual(ReportCardDefinition left, ReportCardDefinition right) =>
        left.Module == right.Module && left.DefinitionVersion == right.DefinitionVersion &&
        left.Filters.SequenceEqual(right.Filters) && left.Period == right.Period &&
        left.DateBasis == right.DateBasis && left.Metric == right.Metric &&
        left.Aggregation == right.Aggregation && left.Grouping == right.Grouping &&
        left.TimeGrouping == right.TimeGrouping && left.Visualization == right.Visualization &&
        JsonEquivalent(left.DisplayJson, right.DisplayJson);

    private static bool JsonEquivalent(string left, string right)
    {
        using var leftDocument = System.Text.Json.JsonDocument.Parse(left);
        using var rightDocument = System.Text.Json.JsonDocument.Parse(right);
        return System.Text.Json.JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static async Task RemoveAgreementAccessAsync(Fixture f)
    {
        await using var db = f.Open();
        foreach (var agreement in await db.Agreements.Where(x => x.AccountId == f.Account && x.OwnerUserId == f.OwnerA).ToListAsync())
            agreement.OwnerUserId = f.OwnerB;
        db.AgreementAccess.RemoveRange(db.AgreementAccess.Where(x => x.AccountId == f.Account && x.UserId == f.OwnerA));
        await db.SaveChangesAsync();
    }

    private static async Task RemoveMembershipAsync(Fixture f, Guid userId)
    {
        await using var db = f.Open();
        var membership = await db.UserAccounts.SingleAsync(x => x.AccountId == f.Account && x.UserId == userId);
        db.UserAccountRoles.RemoveRange(db.UserAccountRoles.Where(x => x.UserAccountId == membership.Id));
        db.UserAccounts.Remove(membership);
        await db.SaveChangesAsync();
    }
}
