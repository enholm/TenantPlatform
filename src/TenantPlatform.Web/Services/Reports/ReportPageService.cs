using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportPageService(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization,
    IReportCatalogService catalog,
    IReportDefinitionCompatibilityValidator compatibility,
    TimeProvider clock) : IReportPageService
{
    private const int DefinitionVersion = 1;
    private const int JsonLimit = 65_536;

    public async Task<IReadOnlyList<ReportPageSummaryDto>> ListAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        var pages = db.ReportPages.AsNoTracking().Where(x => x.AccountId == accountId);
        if (!access.IsAdmin) pages = pages.Where(x => x.OwnerUserId == access.UserId);
        return await pages.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new ReportPageSummaryDto(x.Id, x.OwnerUserId, x.Name, x.Revision,
                db.ReportCards.Count(c => c.AccountId == accountId && c.ReportPageId == x.Id), x.UpdatedUtc))
            .ToListAsync(ct);
    }

    public async Task<ReportPageDto> GetAsync(Guid accountId, Guid pageId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        var page = await ReadablePages(db, accountId, access).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == pageId, ct) ?? throw new UnauthorizedAccessException();
        var cards = await db.ReportCards.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ReportPageId == pageId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return Map(page, cards);
    }

    public async Task<ReportPageDto> CreateAsync(Guid accountId, CreateReportPageRequest request,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        var now = clock.GetUtcNow();
        var page = new ReportPage
        {
            Id = Guid.NewGuid(), AccountId = accountId, OwnerUserId = access.UserId,
            Name = Name(request?.Name), Revision = Guid.NewGuid(), CreatedUtc = now,
            CreatedByUserId = access.UserId, UpdatedUtc = now, UpdatedByUserId = access.UserId
        };
        db.ReportPages.Add(page);
        await db.SaveChangesAsync(ct);
        return Map(page, []);
    }

    public async Task<IReadOnlyList<ReportPageDto>> SaveWorkspaceAsync(Guid accountId,
        SaveReportWorkspaceRequest request, CancellationToken ct = default)
    {
        if (request?.Pages is null || request.Pages.Count == 0 || request.Pages.Count > 50 ||
            request.Pages.Any(x => x is null) || request.Pages.Select(x => x.PageId).Distinct().Count() != request.Pages.Count)
            Invalid();

        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        var pageIds = request!.Pages.Select(x => x.PageId).OrderBy(x => x).ToList();
        await using var transaction = await BeginAsync(db, ct);
        await LockPagesAsync(db, accountId, pageIds, ct);

        var pages = await db.ReportPages.Where(x => x.AccountId == accountId && pageIds.Contains(x.Id)).ToListAsync(ct);
        if (pages.Count != pageIds.Count || pages.Any(x => !CanManage(x, access))) throw new UnauthorizedAccessException();
        var pagesById = pages.ToDictionary(x => x.Id);
        foreach (var edit in request.Pages)
            if (pagesById[edit.PageId].Revision != edit.ExpectedRevision) throw new ReportPageConflictException();

        var existing = await db.ReportCards
            .Where(x => x.AccountId == accountId && pageIds.Contains(x.ReportPageId)).ToListAsync(ct);
        var existingById = existing.ToDictionary(x => x.Id);
        var suppliedIds = request.Pages.SelectMany(x => x.Cards ?? []).Where(x => x.Id.HasValue)
            .Select(x => x.Id!.Value).ToList();
        if (suppliedIds.Distinct().Count() != suppliedIds.Count || suppliedIds.Any(x => !existingById.ContainsKey(x)))
            Invalid();

        foreach (var pageEdit in request.Pages)
        {
            _ = Name(pageEdit.Name);
            if (pageEdit.Cards is null || pageEdit.Cards.Count > 200) Invalid();
            foreach (var cardEdit in pageEdit.Cards)
            {
                if (cardEdit is null || cardEdit.Definition is null) Invalid();
                var old = cardEdit.Id.HasValue ? existingById[cardEdit.Id.Value] : null;
                await ValidateCardAsync(accountId, cardEdit, old, access, ct);
            }
        }

        var retained = suppliedIds.ToHashSet();
        db.ReportCards.RemoveRange(existing.Where(x => !retained.Contains(x.Id)));
        var now = clock.GetUtcNow();
        foreach (var pageEdit in request.Pages)
        {
            var page = pagesById[pageEdit.PageId];
            page.Name = Name(pageEdit.Name);
            page.UpdatedUtc = now;
            page.UpdatedByUserId = access.UserId;
            page.Revision = Guid.NewGuid();
            foreach (var cardEdit in pageEdit.Cards)
            {
                var card = cardEdit.Id.HasValue ? existingById[cardEdit.Id.Value] : new ReportCard
                {
                    Id = Guid.NewGuid(), AccountId = accountId
                };
                Apply(card, page.Id, cardEdit);
                if (!cardEdit.Id.HasValue) db.ReportCards.Add(card);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReportPageConflictException();
        }

        return request.Pages.Select(x => Map(pagesById[x.PageId],
                db.ReportCards.Local.Where(c => c.AccountId == accountId && c.ReportPageId == x.PageId &&
                    c.EntryState(db) != EntityState.Deleted).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList()))
            .ToList();
    }

    public async Task<ReportPageDto> DuplicateAsync(Guid accountId, Guid pageId, Guid expectedRevision,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        await using var transaction = await BeginAsync(db, ct);
        await LockPagesAsync(db, accountId, [pageId], ct);
        var source = await db.ReportPages.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == pageId, ct)
            ?? throw new UnauthorizedAccessException();
        if (!CanManage(source, access)) throw new UnauthorizedAccessException();
        if (source.Revision != expectedRevision) throw new ReportPageConflictException();
        var sourceCards = await db.ReportCards.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ReportPageId == pageId).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var copy = new ReportPage
        {
            Id = Guid.NewGuid(), AccountId = accountId, OwnerUserId = access.UserId, Name = source.Name,
            Revision = Guid.NewGuid(), CreatedUtc = now, CreatedByUserId = access.UserId,
            UpdatedUtc = now, UpdatedByUserId = access.UserId
        };
        var copies = sourceCards.Select(x => Copy(x, copy.Id)).ToList();
        db.ReportPages.Add(copy);
        db.ReportCards.AddRange(copies);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Map(copy, copies.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList());
    }

    public async Task DeleteAsync(Guid accountId, Guid pageId, Guid expectedRevision,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var access = await AccessAsync(db, accountId, ct);
        await using var transaction = await BeginAsync(db, ct);
        await LockPagesAsync(db, accountId, [pageId], ct);
        var page = await db.ReportPages.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == pageId, ct)
            ?? throw new UnauthorizedAccessException();
        if (!CanManage(page, access)) throw new UnauthorizedAccessException();
        if (page.Revision != expectedRevision) throw new ReportPageConflictException();
        var cards = await db.ReportCards.Where(x => x.AccountId == accountId && x.ReportPageId == pageId).ToListAsync(ct);
        db.ReportCards.RemoveRange(cards);
        db.ReportPages.Remove(page);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReportPageConflictException();
        }
    }

    private async Task ValidateCardAsync(Guid accountId, ReportCardEdit edit, ReportCard? existing,
        Access access, CancellationToken ct)
    {
        _ = Name(edit.Name);
        if (edit.GridColumn is < 1 or > 12 || edit.GridRow < 1 || edit.GridWidth is < 1 or > 12 ||
            edit.GridHeight < 1 || edit.SortOrder < 0) Invalid();
        if (!Enum.IsDefined(edit.Definition.Module))
            throw new ReportDefinitionValidationException(
                [new ReportValidationIssue("module", "ReportDefinitionModuleInvalid")]);
        var filtersJson = ReportFilterJson.Serialize(edit.Definition.Filters ?? throw InvalidException());
        if (filtersJson.Length > JsonLimit || edit.Definition.DisplayJson is null ||
            edit.Definition.DisplayJson.Length > JsonLimit || !JsonObject(edit.Definition.DisplayJson)) Invalid();

        var definitionChanged = existing is null || !SameDefinition(existing, edit.Definition, filtersJson);
        if (!definitionChanged) return;
        if (!CanUse(edit.Definition.Module, access)) throw new UnauthorizedAccessException();
        if (edit.Definition.DefinitionVersion != DefinitionVersion) Unsupported();
        var moduleCatalog = await catalog.GetAsync(accountId, edit.Definition.Module, ct);
        compatibility.Validate(edit.Definition, moduleCatalog);
    }

    private async Task<Access> AccessAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken ct)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId) throw new UnauthorizedAccessException();
        var member = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId &&
            x.UserId == current.UserId && x.User.IsActive, ct) ?? throw new UnauthorizedAccessException();
        var admin = await db.UserAccountRoles.AsNoTracking().AnyAsync(x => x.UserAccountId == member.Id &&
            x.Role == UserRole.AccountAdmin, ct);
        var agreements = await authorization.CanUseAgreementsAsync(ct);
        var leasing = await authorization.CanUseLeasingAsync(ct);
        if (!agreements && !leasing) throw new UnauthorizedAccessException();
        return new(current.UserId, admin, agreements, leasing);
    }

    private static IQueryable<ReportPage> ReadablePages(TenantPlatformDbContext db, Guid accountId, Access access)
    {
        var pages = db.ReportPages.Where(x => x.AccountId == accountId);
        return access.IsAdmin ? pages : pages.Where(x => x.OwnerUserId == access.UserId);
    }

    private static bool CanManage(ReportPage page, Access access) => access.IsAdmin || page.OwnerUserId == access.UserId;
    private static bool CanUse(ReportModule module, Access access) => module switch
    {
        ReportModule.Agreements => access.Agreements,
        ReportModule.Leasing => access.Leasing,
        _ => false
    };

    private static void Apply(ReportCard card, Guid pageId, ReportCardEdit edit)
    {
        card.ReportPageId = pageId;
        card.Name = Name(edit.Name);
        card.Module = edit.Definition.Module;
        card.DefinitionVersion = edit.Definition.DefinitionVersion;
        card.FiltersJson = ReportFilterJson.Serialize(edit.Definition.Filters);
        card.PeriodRule = edit.Definition.Period.Rule;
        card.CustomFrom = edit.Definition.Period.CustomFrom;
        card.CustomTo = edit.Definition.Period.CustomTo;
        card.DateBasisId = edit.Definition.DateBasis?.ToString();
        card.MetricId = edit.Definition.Metric?.ToString();
        card.AggregationId = edit.Definition.Aggregation?.ToString();
        ApplyGrouping(card, edit.Definition);
        card.Visualization = edit.Definition.Visualization;
        card.DisplayJson = edit.Definition.DisplayJson;
        card.GridColumn = edit.GridColumn;
        card.GridRow = edit.GridRow;
        card.GridWidth = edit.GridWidth;
        card.GridHeight = edit.GridHeight;
        card.SortOrder = edit.SortOrder;
    }

    private static void ApplyGrouping(ReportCard card, ReportCardDefinition definition)
    {
        card.GroupingKind = ReportGroupingKind.None;
        card.GroupingKey = null;
        card.GroupingScope = null;
        card.GroupingDimensionId = null;
        card.TimeGrouping = null;
        if (definition.TimeGrouping.HasValue)
        {
            card.GroupingKind = ReportGroupingKind.Time;
            card.TimeGrouping = definition.TimeGrouping;
        }
        else if (definition.Grouping is AgreementReportGroupingKey agreement)
        {
            card.GroupingKind = ReportGroupingKind.Catalog;
            card.GroupingKey = (int)agreement.Field;
        }
        else if (definition.Grouping is LeasingReportGroupingKey leasing)
        {
            card.GroupingKind = ReportGroupingKind.Catalog;
            card.GroupingKey = (int)leasing.Field;
        }
        else if (definition.Grouping is ReportDimensionGroupingKey dimension)
        {
            card.GroupingKind = ReportGroupingKind.Dimension;
            card.GroupingScope = (int)dimension.Scope;
            card.GroupingDimensionId = dimension.DimensionId;
        }
    }

    private static bool SameDefinition(ReportCard existing, ReportCardDefinition definition, string filtersJson)
    {
        var probe = new ReportCard();
        ApplyGrouping(probe, definition);
        return existing.Module == definition.Module && existing.DefinitionVersion == definition.DefinitionVersion &&
            existing.FiltersJson == filtersJson && existing.PeriodRule == definition.Period.Rule &&
            existing.CustomFrom == definition.Period.CustomFrom && existing.CustomTo == definition.Period.CustomTo &&
            existing.DateBasisId == definition.DateBasis?.ToString() && existing.MetricId == definition.Metric?.ToString() &&
            existing.AggregationId == definition.Aggregation?.ToString() && existing.GroupingKind == probe.GroupingKind &&
            existing.GroupingKey == probe.GroupingKey && existing.GroupingScope == probe.GroupingScope &&
            existing.GroupingDimensionId == probe.GroupingDimensionId && existing.TimeGrouping == probe.TimeGrouping &&
            existing.Visualization == definition.Visualization && existing.DisplayJson == definition.DisplayJson;
    }

    private static ReportPageDto Map(ReportPage page, IReadOnlyList<ReportCard> cards) => new(
        page.Id, page.OwnerUserId, page.Name, page.Revision, page.CreatedUtc, page.CreatedByUserId,
        page.UpdatedUtc, page.UpdatedByUserId, cards.Select(Map).ToList());

    private static ReportCardDto Map(ReportCard card) => new(card.Id, card.Name, Definition(card),
        card.GridColumn, card.GridRow, card.GridWidth, card.GridHeight, card.SortOrder);

    private static ReportCardDefinition Definition(ReportCard card) => new(card.Module, card.DefinitionVersion,
        ReportFilterJson.Deserialize(card.FiltersJson), new(card.PeriodRule, card.CustomFrom, card.CustomTo),
        Parse<ReportDateBasis>(card.DateBasisId), Parse<ReportMetric>(card.MetricId),
        Parse<ReportAggregation>(card.AggregationId), Grouping(card), card.TimeGrouping,
        card.Visualization, card.DisplayJson);

    private static ReportGroupingKey? Grouping(ReportCard card) => card.GroupingKind switch
    {
        ReportGroupingKind.None or ReportGroupingKind.Time => null,
        ReportGroupingKind.Catalog when card.Module == ReportModule.Agreements && card.GroupingKey.HasValue =>
            new AgreementReportGroupingKey((AgreementReportGrouping)card.GroupingKey.Value),
        ReportGroupingKind.Catalog when card.Module == ReportModule.Leasing && card.GroupingKey.HasValue =>
            new LeasingReportGroupingKey((LeasingReportGrouping)card.GroupingKey.Value),
        ReportGroupingKind.Dimension when card.GroupingScope.HasValue && card.GroupingDimensionId.HasValue =>
            new ReportDimensionGroupingKey((ReportDimensionScope)card.GroupingScope.Value, card.GroupingDimensionId.Value),
        _ => throw new ReportPageValidationException("ReportDefinitionInvalid")
    };

    private static ReportCard Copy(ReportCard source, Guid pageId) => new()
    {
        Id = Guid.NewGuid(), AccountId = source.AccountId, ReportPageId = pageId, Name = source.Name,
        Module = source.Module, DefinitionVersion = source.DefinitionVersion, FiltersJson = source.FiltersJson,
        PeriodRule = source.PeriodRule, CustomFrom = source.CustomFrom, CustomTo = source.CustomTo,
        DateBasisId = source.DateBasisId, MetricId = source.MetricId, AggregationId = source.AggregationId,
        GroupingKind = source.GroupingKind, GroupingKey = source.GroupingKey, GroupingScope = source.GroupingScope,
        GroupingDimensionId = source.GroupingDimensionId, TimeGrouping = source.TimeGrouping,
        Visualization = source.Visualization, DisplayJson = source.DisplayJson, GridColumn = source.GridColumn,
        GridRow = source.GridRow, GridWidth = source.GridWidth, GridHeight = source.GridHeight, SortOrder = source.SortOrder
    };

    private static async Task<IDbContextTransaction?> BeginAsync(TenantPlatformDbContext db, CancellationToken ct) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

    private static async Task LockPagesAsync(TenantPlatformDbContext db, Guid accountId,
        IEnumerable<Guid> pageIds, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        foreach (var pageId in pageIds.OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM report_pages WHERE \"AccountId\" = {accountId} AND \"Id\" = {pageId} FOR UPDATE", ct);
    }

    private static string Name(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200) Invalid();
        return trimmed!;
    }

    private static bool JsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static T? Parse<T>(string? value) where T : struct, Enum
    {
        if (value is null) return null;
        if (Enum.TryParse<T>(value, false, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new ReportPageValidationException("ReportDefinitionInvalid");
    }

    private static ReportPageValidationException InvalidException() => new("ReportDefinitionInvalid");
    [DoesNotReturn]
    private static void Invalid() => throw InvalidException();
    [DoesNotReturn]
    private static void Unsupported() => throw new ReportPageValidationException("ReportDefinitionUnsupported");

    private sealed record Access(Guid UserId, bool IsAdmin, bool Agreements, bool Leasing);
}

internal static class ReportCardStateExtensions
{
    internal static EntityState EntryState(this ReportCard card, TenantPlatformDbContext db) => db.Entry(card).State;
}
