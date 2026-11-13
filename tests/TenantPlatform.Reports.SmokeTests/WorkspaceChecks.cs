using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class WorkspaceChecks
{
    public static void Run()
    {
        var first = Page("First", 2);
        var second = Page("Second", 0);
        var state = new ReportWorkspaceState();
        state.Begin(first);
        Check.That(!state.IsDirty && state.Page(first.Id).Cards.Count == 2,
            "workspace starts as an unchanged deep working copy");
        Check.That(!ReferenceEquals(first.Cards[0].Definition.Filters, state.Page(first.Id).Cards[0].Definition.Filters) &&
            !ReferenceEquals(first.Cards[0].Definition.Period, state.Page(first.Id).Cards[0].Definition.Period),
            "workspace definition collections and period have no shared mutable references");

        state.Rename(first.Id, "Renamed");
        var added = state.AddCard(first.Id, Definition("added"), "Added");
        var duplicate = state.DuplicateCard(first.Id, first.Cards[0].Id, "Duplicate");
        state.MoveCardInPage(first.Id, duplicate.ClientId, 1);
        state.DeleteCard(first.Id, first.Cards[1].Id);
        Check.That(state.IsDirty && state.Page(first.Id).Name == "Renamed" &&
            state.Page(first.Id).Cards.Any(x => x.ClientId == added.ClientId),
            "create, rename, duplicate, delete and reorder affect only the local workspace");
        state.Cancel();
        Check.That(!state.IsDirty && state.Page(first.Id).Name == first.Name &&
            state.Page(first.Id).Cards.Select(x => x.Id).SequenceEqual(first.Cards.Select(x => (Guid?)x.Id)),
            "cancel restores the exact saved page after all local card operations");

        state.AddSnapshot(second);
        var movedId = state.Page(first.Id).Cards[0].ClientId;
        var persistedId = state.Page(first.Id).Cards[0].Id;
        state.MoveCard(first.Id, second.Id, movedId);
        var request = state.BuildRequest();
        Check.That(request.Pages.Count == 2 && request.Pages.Select(x => x.PageId).ToHashSet().SetEquals([first.Id, second.Id]),
            "cross-page move builds one workspace request containing source and target");
        Check.That(request.Pages.Single(x => x.PageId == first.Id).ExpectedRevision == first.Revision &&
            request.Pages.Single(x => x.PageId == second.Id).ExpectedRevision == second.Revision,
            "workspace save carries each affected page's expected revision");
        Check.That(request.Pages.Single(x => x.PageId == second.Id).Cards.Single().Id == persistedId,
            "cross-page move preserves the persisted card identity");
        state.Cancel();
        Check.That(!state.IsDirty && state.Page(first.Id).Cards.Count == 2 && state.Page(second.Id).Cards.Count == 0,
            "cancel reverses a cross-page move without persistence");

        var edited = state.Page(first.Id).Cards[0] with { Name = "Locally edited" };
        state.UpdateCard(first.Id, edited.ClientId, edited);
        var beforeConflict = state.BuildRequest();
        Check.That(state.IsDirty && beforeConflict.Pages.Single().Cards.Single(x => x.Id == edited.Id).Name == "Locally edited",
            "a rejected save can retain the complete local working copy for explicit conflict handling");

        var actionState = new ReportWorkspaceState();
        actionState.Begin(first);
        actionState.AddSnapshot(second);
        var selection = new ReportCardSelectionState();
        var originalId = actionState.Page(first.Id).Cards[0].ClientId;
        selection.Select(first.Id, originalId);
        actionState.MoveCardInPage(first.Id, originalId, 1);
        Check.That(selection.Resolve(actionState.Pages)?.ClientId == originalId,
            "palette card reference follows the stable id after workspace reordering");
        var original = selection.Resolve(actionState.Pages)!;
        var copy = actionState.DuplicateCard(first.Id, originalId, "Copy");
        selection.Select(first.Id, copy.ClientId);
        Check.That(copy.ClientId != originalId && copy.Id is null && selection.Resolve(actionState.Pages)?.ClientId == copy.ClientId &&
            !ReferenceEquals(original.Definition.Filters, copy.Definition.Filters),
            "palette duplication creates an independent working-copy card and selects its new identity");
        actionState.DeleteCard(first.Id, copy.ClientId);
        if (selection.Matches(first.Id, copy.ClientId)) selection.Clear();
        Check.That(!selection.HasSelection && actionState.Page(first.Id).Cards.All(x => x.ClientId != copy.ClientId),
            "palette deletion removes the selected copy and clears selection");
        selection.Select(first.Id, originalId);
        actionState.MoveCard(first.Id, second.Id, originalId);
        if (selection.Matches(first.Id, originalId)) selection.Clear();
        Check.That(!selection.HasSelection && actionState.Page(second.Id).Cards.Any(x => x.ClientId == originalId),
            "palette cross-page move targets the stable selected card and clears source-page selection");
        actionState.Cancel();
        Check.That(!actionState.IsDirty && actionState.Page(first.Id).Cards.Any(x => x.ClientId == originalId) &&
            actionState.Page(second.Id).Cards.Count == 0,
            "full cancel still reverses palette duplication, deletion, reorder and cross-page movement");
    }

    private static ReportPageDto Page(string name, int cards)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new(id, Guid.NewGuid(), name, Guid.NewGuid(), now, Guid.NewGuid(), now, Guid.NewGuid(),
            Enumerable.Range(0, cards).Select(i => new ReportCardDto(Guid.NewGuid(), $"Card {i}", Definition($"q{i}"),
                1, i + 1, 12, 4, i)).ToList());
    }

    private static ReportCardDefinition Definition(string search) => new(
        ReportModule.Agreements, 1,
        [new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue(search))],
        new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap, ReportMetric.AgreementCount,
        ReportAggregation.CountDistinct, Visualization: ReportVisualization.KeyFigure);
}
