using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class ChartLayoutChecks
{
    public static void Run()
    {
        var grouped = Result(
            groups:
            [
                new("a", ReportExecutionLabel.Dynamic("Alpha"), [new(10, "NOK"), new(5, "EUR")], 1),
                new("b", ReportExecutionLabel.Dynamic("Beta"), [new(3, "NOK")], 1)
            ], currency: ReportCurrencyRule.PartitionByCurrency);
        var bar = Map(ReportVisualization.Bar, grouped);
        Check.That(bar.Categories.Select(x => x.Key).SequenceEqual(["a", "b"]) &&
            bar.Series.Select(x => x.Currency).SequenceEqual(["EUR", "NOK"]),
            "chart mapping preserves stable group keys and separate deterministic currency series");
        Check.That(bar.Series.Single(x => x.Currency == "EUR").Values.SequenceEqual([5m, null]),
            "chart mapping preserves a missing currency value as null instead of manufacturing zero");

        var time = Result(time:
        [
            new("2026-02", 2026, 2, new(2026, 2, 1), new(2026, 2, 28), new(2026, 2, 1), new(2026, 2, 28), [new(2)], 2),
            new("2025-12", 2025, 12, new(2025, 12, 1), new(2025, 12, 31), new(2025, 12, 1), new(2025, 12, 31), [new(1)], 1)
        ]);
        var line = Map(ReportVisualization.Line, time);
        Check.That(line.Categories.Select(x => x.Key).SequenceEqual(["2025-12", "2026-02"]),
            "time-chart mapping sorts chronologically by stable year and bucket identity");

        var overlapping = Result(groups:
            [new("a", ReportExecutionLabel.Dynamic("A"), [new(2)], 2)],
            additive: false, multiple: true);
        Check.That(Map(ReportVisualization.Bar, overlapping).NonAdditive,
            "chart mapping carries non-additive multi-membership metadata to presentation");
        Check.Throws<ReportChartMappingException>(() => Map(ReportVisualization.Donut, overlapping),
            "donut mapping rejects overlapping non-additive groups", "ReportChartInvalidDonut");
        Check.Throws<ReportChartMappingException>(() => Map(ReportVisualization.Donut,
                Result(groups: [new("a", ReportExecutionLabel.Dynamic("A"), [new(-1)], 1)])),
            "donut mapping rejects negative results", "ReportChartInvalidDonut");
        Check.Throws<ReportChartMappingException>(() => Map(ReportVisualization.Donut, grouped),
            "donut mapping rejects currency-partitioned results", "ReportChartInvalidDonut");
        var donut = Map(ReportVisualization.Donut,
            Result(groups: [new("a", ReportExecutionLabel.Dynamic("A"), [new(2)], 2),
                new("b", ReportExecutionLabel.Dynamic("B"), [new(1)], 1)]));
        Check.That(donut.Series.Count == 1 && donut.Series[0].Values.SequenceEqual([2m, 1m]),
            "valid donut mapping keeps the server-provided additive values unchanged");

        var first = new ReportGridItem(Guid.NewGuid(), 1, 1, 6, 4, 0);
        var second = new ReportGridItem(Guid.NewGuid(), 1, 1, 6, 4, 1);
        var normalized = ReportLayoutEngine.Normalize([first, second]);
        Check.That(normalized.Single(x => x.ClientId == first.ClientId).Column == 1 &&
            normalized.Single(x => x.ClientId == second.ClientId).Column == 7,
            "layout resolves a collision deterministically into the first free column by sort order");
        var preferred = ReportLayoutEngine.Normalize([first, second], second.ClientId);
        Check.That(preferred.Single(x => x.ClientId == second.ClientId).Column == 1 &&
            preferred.Single(x => x.ClientId == first.ClientId).Column == 7,
            "the actively moved card owns its snapped target and displaces later placement deterministically");
        var clamped = ReportLayoutEngine.ClampInteraction(new(Guid.NewGuid(), 12, -4, 1, 99, 0));
        Check.That(clamped.Width == 3 && clamped.Height == ReportLayoutEngine.MaximumInteractiveHeight &&
            clamped.Column == 10 && clamped.Row == 1,
            "interactive resize clamps to the configured card-height and twelve-column bounds");

        var saved = Page(first.ClientId);
        var workspace = new ReportWorkspaceState();
        workspace.Begin(saved);
        var generation = 7L;
        workspace.UpdateLayout(saved.Id, new(first.ClientId, 5, 3, 5, 6, generation));
        var edit = workspace.BuildRequest().Pages.Single().Cards.Single();
        Check.That(edit.GridColumn == 5 && edit.GridRow == 3 && edit.GridWidth == 5 && edit.GridHeight == 6,
            "layout changes roundtrip through the existing workspace persistence contract");
        workspace.Cancel();
        var restored = workspace.Page(saved.Id).Cards.Single();
        Check.That(!workspace.IsDirty && restored.GridColumn == 1 && restored.GridRow == 1 &&
            restored.GridWidth == 6 && restored.GridHeight == 4,
            "cancel restores saved position and size after move and resize");
        workspace.ResizeCard(saved.Id, first.ClientId, 1, 1);
        var keyboardResize = workspace.BuildRequest().Pages.Single().Cards.Single();
        Check.That(keyboardResize.GridWidth == 7 && keyboardResize.GridHeight == 5,
            "keyboard resize uses the same clamped working-copy layout path as pointer resize");

    }

    private static ReportChartModel Map(ReportVisualization visualization, ReportExecutionResult result) =>
        ReportChartMapper.Map(visualization, result, x => x.Text ?? x.ResourceKey ?? "", x => x.Key,
            "count", "chart");

    private static ReportExecutionResult Result(
        IReadOnlyList<ReportGroupResult>? groups = null,
        IReadOnlyList<ReportTimeResult>? time = null,
        ReportCurrencyRule currency = ReportCurrencyRule.None,
        bool additive = true,
        bool multiple = false) =>
        new(ReportModule.Agreements, ReportMetric.AgreementCount,
            new(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31)), new(2026, 11, 13), "Europe/Oslo",
            [new(3)], groups ?? [], time ?? [], null,
            new(ReportGrain.Agreement, ReportValueUnit.Count, currency,
                ReportAdditivity.UniqueBeforeGroupExpansion, true, additive, multiple, false, 3, 3, "test", []));

    private static ReportPageDto Page(Guid cardId)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new ReportCardDefinition(ReportModule.Agreements, 1, [],
            new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
            ReportMetric.AgreementCount, ReportAggregation.CountDistinct,
            Visualization: ReportVisualization.KeyFigure);
        return new(Guid.NewGuid(), Guid.NewGuid(), "Layout", Guid.NewGuid(), now, Guid.NewGuid(), now, Guid.NewGuid(),
            [new(cardId, "Card", definition, 1, 1, 6, 4, 0)]);
    }
}
