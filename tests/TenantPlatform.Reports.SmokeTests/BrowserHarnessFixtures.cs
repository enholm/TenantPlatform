using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class BrowserHarnessFixtures
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static object Create()
    {
        var initial = new[]
        {
            new ReportGridItem(FirstId, 1, 1, 6, 4, 0),
            new ReportGridItem(SecondId, 7, 1, 6, 4, 1)
        };
        var collision = ReportLayoutEngine.Normalize(
        [
            initial[0],
            initial[1] with { Column = 1 }
        ], SecondId);

        var workspace = new ReportWorkspaceState();
        var page = Page(initial);
        workspace.Begin(page);
        workspace.MoveCardInPage(page.Id, SecondId, -1);
        var keyboardMoveUp = workspace.Page(page.Id).Cards.Select(CardLayout).ToList();
        workspace.Cancel();
        workspace.ResizeCard(page.Id, SecondId, 1, 0);
        var keyboardWider = workspace.Page(page.Id).Cards.Select(CardLayout).ToList();

        return new
        {
            initial,
            collision,
            maximumResize = ReportLayoutEngine.ClampInteraction(initial[1] with
            {
                Column = 1, Width = 99, Height = 99
            }),
            minimumResize = ReportLayoutEngine.ClampInteraction(initial[1] with
            {
                Column = 1, Width = -99, Height = -99
            }),
            keyboardMoveUp,
            keyboardWider,
            charts = ChartFixtures(),
            tree = TreeFixture(),
            invalidDonutRejected = InvalidDonutRejected(),
            currentGeneration = 5L
        };
    }

    private static ReportTreeChartModel TreeFixture() => new(
        new("1:0:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Account", null, false,
        [
            new("1:3:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Framework with a deliberately long display name", "FRAME-1", false,
            [
                new("1:4:cccccccccccccccccccccccccccccccc", "Acquisition <script>text</script>", "ACQ-1", true, [])
            ]),
            new("1:s:1", "Without framework agreement", null, false, [])
        ]), "Relationship tree fixture");

    private static object ChartFixtures()
    {
        const string hostile = "Very long <img src=x onerror=window.__reportXss=true> & special ‘label’ <script>bad</script> — 012345678901234567890123456789";
        var barResult = new ReportExecutionResult(ReportModule.Agreements, ReportMetric.AgreementIncomeAmount,
            new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), new(2026, 11, 13), "Europe/Oslo",
            [new(13, "NOK"), new(5, "EUR")],
            [
                new("alpha", ReportExecutionLabel.Dynamic(hostile), [new(10, "NOK"), new(5, "EUR")], 1),
                new("beta", ReportExecutionLabel.Dynamic("Beta & Co"), [new(3, "NOK")], 1)
            ], [], null,
            new(ReportGrain.AgreementFinancialPeriod, ReportValueUnit.Amount,
                ReportCurrencyRule.PartitionByCurrency, ReportAdditivity.UniqueBeforeGroupExpansion,
                true, true, false, false, 2, 2, "fixture", []));
        var lineResult = new ReportExecutionResult(ReportModule.Agreements, ReportMetric.AgreementCount,
            barResult.Period, barResult.LocalToday, barResult.TimeZoneId, [new(3)], [],
            [
                new("2026-02", 2026, 2, new(2026, 2, 1), new(2026, 2, 28), new(2026, 2, 1), new(2026, 2, 28), [new(2)], 2),
                new("2025-12", 2025, 12, new(2025, 12, 1), new(2025, 12, 31), new(2025, 12, 1), new(2025, 12, 31), [new(1)], 1),
                new("2026-01", 2026, 1, new(2026, 1, 1), new(2026, 1, 31), new(2026, 1, 1), new(2026, 1, 31), [], 0)
            ], null,
            new(ReportGrain.Agreement, ReportValueUnit.Count, ReportCurrencyRule.None,
                ReportAdditivity.UniqueBeforeGroupExpansion, true, false, false, false, 3, 3, "fixture", []));
        var donutResult = lineResult with
        {
            Groups =
            [
                new("active", ReportExecutionLabel.Dynamic("Active"), [new(2)], 2),
                new("draft", ReportExecutionLabel.Dynamic("Draft"), [new(1)], 1)
            ],
            TimeSeries = [],
            Metadata = lineResult.Metadata with { GroupTotalsAdditive = true }
        };
        return new
        {
            bar = ReportChartMapper.Map(ReportVisualization.Bar, barResult,
                x => x.Text ?? x.ResourceKey ?? "", x => x.Key, "amount", "Bar fixture"),
            line = ReportChartMapper.Map(ReportVisualization.Line, lineResult,
                x => x.Text ?? x.ResourceKey ?? "", x => x.Key, "count", "Line fixture"),
            donut = ReportChartMapper.Map(ReportVisualization.Donut, donutResult,
                x => x.Text ?? x.ResourceKey ?? "", x => x.Key, "count", "Donut fixture")
        };
    }

    private static ReportGridItem CardLayout(ReportCardDraft card) => new(
        card.ClientId, card.GridColumn, card.GridRow, card.GridWidth, card.GridHeight, card.SortOrder);

    private static ReportPageDto Page(IReadOnlyList<ReportGridItem> items)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new ReportCardDefinition(ReportModule.Agreements, 1, [],
            new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
            ReportMetric.AgreementCount, ReportAggregation.CountDistinct,
            Visualization: ReportVisualization.KeyFigure);
        return new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), "Browser fixture",
            Guid.NewGuid(), now, Guid.NewGuid(), now, Guid.NewGuid(), items.Select(x => new ReportCardDto(
                x.ClientId, x.ClientId == FirstId ? "Alpha" : "Beta", definition,
                x.Column, x.Row, x.Width, x.Height, x.SortOrder)).ToList());
    }

    private static bool InvalidDonutRejected()
    {
        var result = new ReportExecutionResult(ReportModule.Agreements, ReportMetric.AgreementCount,
            new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), new(2026, 11, 13), "Europe/Oslo",
            [new(2)], [new("overlap", ReportExecutionLabel.Dynamic("Overlap"), [new(2)], 2)], [], null,
            new(ReportGrain.Agreement, ReportValueUnit.Count, ReportCurrencyRule.None,
                ReportAdditivity.UniqueBeforeGroupExpansion, true, false, true, false, 2, 2, "fixture", []));
        try
        {
            _ = ReportChartMapper.Map(ReportVisualization.Donut, result,
                label => label.Text ?? label.ResourceKey ?? "", bucket => bucket.Key, "count", "fixture");
            return false;
        }
        catch (ReportChartMappingException exception)
        {
            return exception.Message == "ReportChartInvalidDonut";
        }
    }
}
