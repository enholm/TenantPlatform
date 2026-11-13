namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportWorkspaceState
{
    private readonly Dictionary<Guid, ReportPageDto> saved = [];
    private readonly Dictionary<Guid, ReportPageDraft> working = [];

    public Guid CurrentPageId { get; private set; }
    public IReadOnlyList<ReportPageDraft> Pages => working.Values
        .OrderBy(x => x.Id == CurrentPageId ? 0 : 1)
        .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.Id)
        .ToList();
    public bool HasPages => working.Count > 0;
    public bool IsDirty => working.Any(x =>
        !saved.TryGetValue(x.Key, out var original) || !Same(x.Value, original));

    public void Begin(ReportPageDto page)
    {
        ArgumentNullException.ThrowIfNull(page);
        saved.Clear();
        working.Clear();
        CurrentPageId = page.Id;
        AddSnapshot(page);
    }

    public void AddSnapshot(ReportPageDto page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (saved.ContainsKey(page.Id)) return;
        saved.Add(page.Id, page);
        working.Add(page.Id, Copy(page));
    }

    public ReportPageDraft Page(Guid pageId) =>
        working.TryGetValue(pageId, out var page) ? page : throw new KeyNotFoundException();

    public void Rename(Guid pageId, string name) => Page(pageId).Name = name;

    public ReportCardDraft AddCard(Guid pageId, ReportCardDefinition definition, string name)
    {
        var page = Page(pageId);
        var card = new ReportCardDraft
        {
            ClientId = Guid.NewGuid(),
            Name = name,
            Definition = Copy(definition),
            GridColumn = 1,
            GridRow = page.Cards.Count + 1,
            GridWidth = 12,
            GridHeight = 4,
            SortOrder = page.Cards.Count
        };
        page.Cards.Add(card);
        NormalizeLayout(page, card.ClientId);
        return card;
    }

    public void UpdateCard(Guid pageId, Guid clientId, ReportCardDraft input)
    {
        var page = Page(pageId);
        var index = page.Cards.FindIndex(x => x.ClientId == clientId);
        if (index < 0) throw new KeyNotFoundException();
        var original = page.Cards[index];
        page.Cards[index] = Copy(input) with { ClientId = original.ClientId, Id = original.Id };
        NormalizeOrder(page);
        NormalizeLayout(page, original.ClientId);
    }

    public ReportCardDraft DuplicateCard(Guid pageId, Guid clientId, string name)
    {
        var page = Page(pageId);
        var original = Find(page, clientId);
        var copy = Copy(original) with
        {
            ClientId = Guid.NewGuid(),
            Id = null,
            Name = name,
            GridRow = original.GridRow + 1,
            SortOrder = original.SortOrder + 1
        };
        page.Cards.Insert(Math.Min(original.SortOrder + 1, page.Cards.Count), copy);
        NormalizeOrder(page);
        NormalizeLayout(page, copy.ClientId);
        return copy;
    }

    public void DeleteCard(Guid pageId, Guid clientId)
    {
        var page = Page(pageId);
        page.Cards.Remove(Find(page, clientId));
        NormalizeOrder(page);
        NormalizeLayout(page);
    }

    public void MoveCard(Guid sourcePageId, Guid targetPageId, Guid clientId)
    {
        if (sourcePageId == targetPageId) return;
        var source = Page(sourcePageId);
        var target = Page(targetPageId);
        var card = Find(source, clientId);
        source.Cards.Remove(card);
        target.Cards.Add(card with { SortOrder = target.Cards.Count, GridRow = target.Cards.Count + 1 });
        NormalizeOrder(source);
        NormalizeOrder(target);
        NormalizeLayout(source);
        NormalizeLayout(target, clientId);
    }

    public void MoveCardInPage(Guid pageId, Guid clientId, int direction)
    {
        var page = Page(pageId);
        var index = page.Cards.FindIndex(x => x.ClientId == clientId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= page.Cards.Count) return;
        var sourcePosition = (page.Cards[index].GridColumn, page.Cards[index].GridRow);
        var targetPosition = (page.Cards[target].GridColumn, page.Cards[target].GridRow);
        page.Cards[index] = page.Cards[index] with { GridColumn = targetPosition.GridColumn, GridRow = targetPosition.GridRow };
        page.Cards[target] = page.Cards[target] with { GridColumn = sourcePosition.GridColumn, GridRow = sourcePosition.GridRow };
        (page.Cards[index], page.Cards[target]) = (page.Cards[target], page.Cards[index]);
        NormalizeOrder(page);
        NormalizeLayout(page, clientId);
    }

    public bool UpdateLayout(Guid pageId, ReportGridLayoutChange change)
    {
        var page = Page(pageId);
        var index = page.Cards.FindIndex(x => x.ClientId == change.ClientId);
        if (index < 0) return false;
        var card = page.Cards[index];
        var clamped = ReportLayoutEngine.ClampInteraction(new(card.ClientId, change.Column, change.Row,
            change.Width, change.Height, card.SortOrder));
        page.Cards[index] = card with
        {
            GridColumn = clamped.Column,
            GridRow = clamped.Row,
            GridWidth = clamped.Width,
            GridHeight = clamped.Height
        };
        NormalizeLayout(page, card.ClientId);
        return true;
    }

    public bool ResizeCard(Guid pageId, Guid clientId, int widthDelta, int heightDelta)
    {
        var card = Find(Page(pageId), clientId);
        return UpdateLayout(pageId, new(clientId, card.GridColumn, card.GridRow,
            card.GridWidth + widthDelta, card.GridHeight + heightDelta, 0));
    }

    public SaveReportWorkspaceRequest BuildRequest()
    {
        var changed = Pages.Where(page => !saved.TryGetValue(page.Id, out var original) || !Same(page, original))
            .Select(page => new ReportWorkspacePageEdit(page.Id, page.ExpectedRevision, page.Name,
                page.Cards.OrderBy(x => x.SortOrder).Select(x => new ReportCardEdit(
                    x.Id, x.Name, Copy(x.Definition), x.GridColumn, x.GridRow,
                    x.GridWidth, x.GridHeight, x.SortOrder)).ToList()))
            .ToList();
        if (changed.Count == 0) throw new InvalidOperationException("ReportWorkspaceUnchanged");
        return new(changed);
    }

    public void ApplySaved(IReadOnlyCollection<ReportPageDto> pages)
    {
        foreach (var page in pages)
        {
            saved[page.Id] = page;
            working[page.Id] = Copy(page);
        }
    }

    public void Cancel()
    {
        foreach (var page in saved.Values)
            working[page.Id] = Copy(page);
    }

    public ReportPageDto SavedCurrent() => saved[CurrentPageId];

    public static ReportPageDraft Copy(ReportPageDto page) => new()
    {
        Id = page.Id,
        OwnerUserId = page.OwnerUserId,
        Name = page.Name,
        ExpectedRevision = page.Revision,
        Cards = page.Cards.OrderBy(x => x.SortOrder).Select(Copy).ToList()
    };

    public static ReportCardDraft Copy(ReportCardDto card) => new()
    {
        ClientId = card.Id,
        Id = card.Id,
        Name = card.Name,
        Definition = Copy(card.Definition),
        GridColumn = card.GridColumn,
        GridRow = card.GridRow,
        GridWidth = card.GridWidth,
        GridHeight = card.GridHeight,
        SortOrder = card.SortOrder
    };

    public static ReportCardDefinition Copy(ReportCardDefinition definition) => definition with
    {
        Filters = definition.Filters.Select(Copy).ToList(),
        Period = definition.Period with { }
    };

    private static ReportFilterSelection Copy(ReportFilterSelection filter) => filter switch
    {
        ReportDimensionFilterSelection dimension => dimension with
        {
            DimensionKey = dimension.DimensionKey with { }
        },
        ReportValueFilterSelection value => value with
        {
            Key = Copy(value.Key),
            Value = Copy(value.Value)
        },
        _ => throw new InvalidOperationException()
    };

    private static ReportFilterKey Copy(ReportFilterKey key) => key switch
    {
        AgreementReportFilterKey agreement => agreement with { },
        LeasingReportFilterKey leasing => leasing with { },
        ReportDimensionFilterKey dimension => dimension with { },
        _ => throw new InvalidOperationException()
    };

    private static ReportFilterValue Copy(ReportFilterValue value) => value switch
    {
        ReportTextValue text => text with { },
        ReportEntityValue entity => entity with { },
        ReportEnumValue<TenantPlatform.Core.Agreements.AgreementStatus> status => status with { },
        ReportEnumValue<TenantPlatform.Core.Agreements.AgreementType> type => type with { },
        ReportEnumValue<TenantPlatform.Web.Services.Agreements.AgreementAttention> attention => attention with { },
        ReportEnumValue<LeasingCatalogKind> kind => kind with { },
        ReportEnumValue<LeasingCatalogStatus> status => status with { },
        _ => throw new InvalidOperationException()
    };

    private static ReportCardDraft Copy(ReportCardDraft card) => card with
    {
        Definition = Copy(card.Definition)
    };

    private static ReportCardDraft Find(ReportPageDraft page, Guid clientId) =>
        page.Cards.Single(x => x.ClientId == clientId);

    private static void NormalizeOrder(ReportPageDraft page)
    {
        for (var i = 0; i < page.Cards.Count; i++)
            page.Cards[i] = page.Cards[i] with { SortOrder = i };
    }

    private static void NormalizeLayout(ReportPageDraft page, Guid? preferredId = null)
    {
        var normalized = ReportLayoutEngine.Normalize(page.Cards.Select(x => new ReportGridItem(
            x.ClientId, x.GridColumn, x.GridRow, x.GridWidth, x.GridHeight, x.SortOrder)), preferredId)
            .ToDictionary(x => x.ClientId);
        for (var i = 0; i < page.Cards.Count; i++)
        {
            var card = page.Cards[i];
            var layout = normalized[card.ClientId];
            page.Cards[i] = card with
            {
                GridColumn = layout.Column,
                GridRow = layout.Row,
                GridWidth = layout.Width,
                GridHeight = layout.Height
            };
        }
    }

    private static bool Same(ReportPageDraft draft, ReportPageDto original) =>
        draft.Name == original.Name &&
        draft.ExpectedRevision == original.Revision &&
        draft.Cards.OrderBy(x => x.SortOrder).Select(CardFingerprint)
            .SequenceEqual(original.Cards.OrderBy(x => x.SortOrder).Select(CardFingerprint));

    private static string CardFingerprint(ReportCardDraft card) =>
        $"{card.Id:N}|{card.Name}|{card.GridColumn}|{card.GridRow}|{card.GridWidth}|{card.GridHeight}|" +
        $"{card.SortOrder}|{DefinitionFingerprint(card.Definition)}";

    private static string CardFingerprint(ReportCardDto card) =>
        $"{card.Id:N}|{card.Name}|{card.GridColumn}|{card.GridRow}|{card.GridWidth}|{card.GridHeight}|" +
        $"{card.SortOrder}|{DefinitionFingerprint(card.Definition)}";

    private static string DefinitionFingerprint(ReportCardDefinition definition) =>
        $"{(int)definition.Module}|{definition.DefinitionVersion}|{ReportFilterJson.Serialize(definition.Filters)}|" +
        $"{(int)definition.Period.Rule}|{definition.Period.CustomFrom:yyyy-MM-dd}|{definition.Period.CustomTo:yyyy-MM-dd}|" +
        $"{definition.DateBasis}|{definition.Metric}|{definition.Aggregation}|{definition.Grouping}|" +
        $"{definition.TimeGrouping}|{definition.Visualization}|{definition.DisplayJson}";
}

public sealed class ReportPageDraft
{
    public Guid Id { get; init; }
    public Guid OwnerUserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid ExpectedRevision { get; init; }
    public List<ReportCardDraft> Cards { get; set; } = [];
}

public sealed record ReportCardDraft
{
    public Guid ClientId { get; init; }
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public required ReportCardDefinition Definition { get; init; }
    public int GridColumn { get; init; }
    public int GridRow { get; init; }
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public int SortOrder { get; init; }
}
