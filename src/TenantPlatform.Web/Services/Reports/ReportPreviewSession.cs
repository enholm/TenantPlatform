using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportPreviewOptions
{
    public TimeSpan MaximumAge { get; set; } = TimeSpan.FromMinutes(5);
    public int MaximumEntries { get; set; } = 64;
    public int MaximumConcurrentExecutions { get; set; } = 4;
    public int TreePageSize { get; set; } = 12;
}

public sealed record ReportTreePreviewResult(
    ReportTreeNode Root,
    IReadOnlyList<ReportTreeNode> Children,
    int? NextOffset)
{
    public bool IsTruncated => NextOffset.HasValue;
}

public sealed record ReportPreviewSessionSnapshot(
    int CachedEntries,
    int ActiveExecutions,
    int MaximumObservedExecutions);

public interface IReportPreviewSession
{
    Task<ReportExecutionResult> ExecuteAsync(Guid sessionId, Guid accountId,
        ReportExecutionRequest request, bool forceRefresh = false, CancellationToken ct = default);
    Task<ReportTreePreviewResult> LoadTreeAsync(Guid sessionId, Guid accountId,
        ReportCardDefinition definition, bool forceRefresh = false, CancellationToken ct = default);
    void EndSession(Guid sessionId);
    ReportPreviewSessionSnapshot Snapshot { get; }
}

// Scoped to the current Blazor circuit/request. Entries are additionally partitioned by
// authenticated user, selected account and report-page session, and never leave this scope.
public sealed class ReportPreviewSession : IReportPreviewSession, IAsyncDisposable
{
    private readonly IReportExecutionService execution;
    private readonly IReportTreeService trees;
    private readonly IReportCatalogService catalogs;
    private readonly IReportDefinitionCompatibilityValidator compatibility;
    private readonly IReportLocalDateService localDates;
    private readonly ITenantAuthorizationService authorization;
    private readonly ICurrentUserContextService userContext;
    private readonly TimeProvider clock;
    private readonly ReportPreviewOptions options;
    private readonly SemaphoreSlim executionSlots;
    private readonly object gate = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private ContextIdentity? identity;
    private long accessSequence;
    private int activeExecutions;
    private int maximumObservedExecutions;
    private bool disposed;

    public ReportPreviewSession(
        IReportExecutionService execution,
        IReportTreeService trees,
        IReportCatalogService catalogs,
        IReportDefinitionCompatibilityValidator compatibility,
        IReportLocalDateService localDates,
        ITenantAuthorizationService authorization,
        ICurrentUserContextService userContext,
        TimeProvider clock,
        IOptions<ReportPreviewOptions> options)
    {
        this.execution = execution;
        this.trees = trees;
        this.catalogs = catalogs;
        this.compatibility = compatibility;
        this.localDates = localDates;
        this.authorization = authorization;
        this.userContext = userContext;
        this.clock = clock;
        this.options = options.Value;
        if (this.options.MaximumAge <= TimeSpan.Zero || this.options.MaximumEntries < 1 ||
            this.options.MaximumConcurrentExecutions < 1 || this.options.TreePageSize is < 1 or > 100)
            throw new InvalidOperationException("Invalid ReportPreview configuration.");
        executionSlots = new(this.options.MaximumConcurrentExecutions,
            this.options.MaximumConcurrentExecutions);
    }

    public ReportPreviewSessionSnapshot Snapshot
    {
        get
        {
            lock (gate)
                return new(entries.Count, activeExecutions, maximumObservedExecutions);
        }
    }

    public async Task<ReportExecutionResult> ExecuteAsync(Guid sessionId, Guid accountId,
        ReportExecutionRequest request, bool forceRefresh = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await PrepareAsync(sessionId, accountId, request.Definition, ct);
        var key = ReportPreviewCacheKey.Execution(prepared, request);
        var value = await GetAsync(key, sessionId, forceRefresh,
            token => ExecuteLimitedAsync(inner => execution.ExecuteAsync(accountId, request, inner), token), ct);
        var result = Clone((ReportExecutionResult)value);
        ValidateResult(request.Definition, prepared.Catalog, result);
        return result;
    }

    public async Task<ReportTreePreviewResult> LoadTreeAsync(Guid sessionId, Guid accountId,
        ReportCardDefinition definition, bool forceRefresh = false, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(sessionId, accountId, definition, ct);
        if (definition.Visualization != ReportVisualization.RelationshipTree)
            throw new ReportTreeException("ReportTreeDefinitionInvalid");
        var key = ReportPreviewCacheKey.Tree(prepared, options.TreePageSize);
        var value = await GetAsync(key, sessionId, forceRefresh, token => ExecuteLimitedAsync(async inner =>
        {
            var root = await trees.GetRootAsync(accountId, definition, inner);
            var page = root.HasChildren
                ? await trees.GetChildrenAsync(accountId, definition, root.Id, 0, options.TreePageSize, inner)
                : new ReportTreeNodePage([], 0, 0, null);
            return new ReportTreePreviewResult(root, page.Items.ToArray(), page.NextOffset);
        }, token), ct);
        return Clone((ReportTreePreviewResult)value);
    }

    public void EndSession(Guid sessionId)
    {
        List<CacheEntry> removed;
        lock (gate)
        {
            removed = entries.Values.Where(x => x.SessionId == sessionId).ToList();
            foreach (var entry in removed)
            {
                entries.Remove(entry.Key);
                entry.Retired = true;
            }
        }
        foreach (var entry in removed) entry.Cancellation.Cancel();
    }

    private async Task<PreparedRequest> PrepareAsync(Guid sessionId, Guid accountId,
        ReportCardDefinition definition, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sessionId == Guid.Empty || definition is null) throw new ReportDefinitionValidationException(
            [new ReportValidationIssue("definition", "ReportDefinitionMetricInvalid")]);
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.UserId == Guid.Empty ||
            current.CurrentAccountId != accountId)
        {
            InvalidateAll();
            throw new UnauthorizedAccessException();
        }

        try
        {
            var allowed = definition.Module switch
            {
                ReportModule.Agreements => await authorization.CanUseAgreementsAsync(ct),
                ReportModule.Leasing => await authorization.CanUseLeasingAsync(ct),
                _ => false
            };
            if (!allowed) throw new UnauthorizedAccessException();
            var catalog = await catalogs.GetAsync(accountId, definition.Module, ct);
            compatibility.Validate(definition, catalog);
            var local = await localDates.GetAsync(accountId, ct);
            var period = ReportPeriodResolver.Resolve(definition.Period, local.Today);
            SetIdentity(new(current.UserId, accountId));
            return new(sessionId, current.UserId, accountId, definition, catalog, local, period);
        }
        catch (UnauthorizedAccessException)
        {
            InvalidateAll();
            throw;
        }
    }

    private async Task<object> GetAsync(string key, Guid sessionId, bool forceRefresh,
        Func<CancellationToken, Task<object>> factory, CancellationToken callerToken)
    {
        CacheEntry entry;
        lock (gate)
        {
            RemoveExpiredLocked();
            if (forceRefresh && entries.Remove(key, out var existing))
            {
                existing.Retired = true;
                if (existing.Subscribers == 0) existing.Cancellation.Cancel();
            }
            if (!entries.TryGetValue(key, out entry!))
            {
                EnsureCapacityLocked();
                var cancellation = new CancellationTokenSource();
                entry = new CacheEntry(key, sessionId, cancellation, ++accessSequence);
                entries.Add(key, entry);
                entry.Task = StartFactoryAsync(factory, cancellation.Token);
                _ = entry.Task.ContinueWith(completed => CompleteEntry(entry, completed),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            entry.Subscribers++;
            entry.LastAccess = ++accessSequence;
        }

        try
        {
            return await entry.Task.WaitAsync(callerToken);
        }
        finally
        {
            ReleaseSubscriber(entry);
        }
    }

    private static async Task<object> StartFactoryAsync(Func<CancellationToken, Task<object>> factory,
        CancellationToken ct)
    {
        // Never enter an application/database service while the cache lock is held.
        await Task.Yield();
        return await factory(ct);
    }

    private async Task<object> ExecuteLimitedAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        await executionSlots.WaitAsync(ct);
        var active = Interlocked.Increment(ref activeExecutions);
        UpdateMaximumObserved(active);
        try { return (await action(ct))!; }
        finally
        {
            Interlocked.Decrement(ref activeExecutions);
            executionSlots.Release();
        }
    }

    private void CompleteEntry(CacheEntry entry, Task<object> completed)
    {
        lock (gate)
        {
            if (completed.Status == TaskStatus.RanToCompletion && !entry.Retired)
                entry.CompletedUtc = clock.GetUtcNow();
            else if (entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                entries.Remove(entry.Key);
                entry.Retired = true;
            }
        }
    }

    private void ReleaseSubscriber(CacheEntry entry)
    {
        var cancel = false;
        lock (gate)
        {
            entry.Subscribers--;
            if (entry.Subscribers == 0 && !entry.Task.IsCompleted)
            {
                if (entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                    entries.Remove(entry.Key);
                entry.Retired = true;
                cancel = true;
            }
        }
        if (cancel) entry.Cancellation.Cancel();
    }

    private void EnsureCapacityLocked()
    {
        while (entries.Count >= options.MaximumEntries)
        {
            var candidate = entries.Values.Where(x => x.Task.IsCompleted)
                .OrderBy(x => x.LastAccess).FirstOrDefault();
            if (candidate is null) throw new ReportExecutionException("ReportPreviewBusy");
            entries.Remove(candidate.Key);
            candidate.Retired = true;
            candidate.Cancellation.Dispose();
        }
    }

    private void RemoveExpiredLocked()
    {
        var now = clock.GetUtcNow();
        foreach (var entry in entries.Values.Where(x => x.CompletedUtc.HasValue &&
                     now - x.CompletedUtc.Value >= options.MaximumAge).ToList())
        {
            entries.Remove(entry.Key);
            entry.Retired = true;
            entry.Cancellation.Dispose();
        }
    }

    private void InvalidateAll()
    {
        List<CacheEntry> removed;
        lock (gate) removed = InvalidateAllLocked();
        foreach (var entry in removed) entry.Cancellation.Cancel();
    }

    private void SetIdentity(ContextIdentity nextIdentity)
    {
        List<CacheEntry> removed = [];
        lock (gate)
        {
            if (identity.HasValue && identity.Value != nextIdentity)
                removed = InvalidateAllLocked();
            identity = nextIdentity;
        }
        foreach (var entry in removed) entry.Cancellation.Cancel();
    }

    private List<CacheEntry> InvalidateAllLocked()
    {
        var removed = entries.Values.ToList();
        entries.Clear();
        foreach (var entry in removed) entry.Retired = true;
        return removed;
    }

    private void UpdateMaximumObserved(int active)
    {
        int observed;
        while (active > (observed = Volatile.Read(ref maximumObservedExecutions)) &&
               Interlocked.CompareExchange(ref maximumObservedExecutions, active, observed) != observed) { }
    }

    private static ReportExecutionResult Clone(ReportExecutionResult result) => result with
    {
        Totals = result.Totals.ToArray(),
        Groups = result.Groups.Select(x => x with { Values = x.Values.ToArray() }).ToArray(),
        TimeSeries = result.TimeSeries.Select(x => x with { Values = x.Values.ToArray() }).ToArray(),
        Table = result.Table is null ? null : result.Table with { Rows = result.Table.Rows.ToArray() },
        Metadata = result.Metadata with
        {
            RestrictionResourceKeys = result.Metadata.RestrictionResourceKeys.ToArray()
        }
    };

    private static ReportTreePreviewResult Clone(ReportTreePreviewResult result) => result with
    {
        Root = result.Root with { },
        Children = result.Children.Select(x => x with { }).ToArray()
    };

    private static void ValidateResult(ReportCardDefinition definition, ReportModuleCatalog catalog,
        ReportExecutionResult result)
    {
        var metric = catalog.Compatibility.Metrics.SingleOrDefault(x => x.Metric == definition.Metric);
        var currencies = result.Totals.Where(x => x.Currency is not null).Select(x => x.Currency!).ToArray();
        var validation = ReportResultValidator.Validate(definition, metric, new(
            currencies,
            ValuesPartitionedByCurrency: true,
            HasNegativeValues: result.Totals.Any(x => x.Value < 0) ||
                result.Groups.SelectMany(x => x.Values).Any(x => x.Value < 0),
            HasOverlappingGroups: result.Metadata.MultipleGroupMembership,
            HasValues: result.Totals.Count > 0));
        if (!validation.IsValid) throw new ReportDefinitionValidationException(validation.Issues);
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        InvalidateAll();
        return ValueTask.CompletedTask;
    }

    private readonly record struct ContextIdentity(Guid UserId, Guid AccountId);
    private sealed record PreparedRequest(Guid SessionId, Guid UserId, Guid AccountId,
        ReportCardDefinition Definition, ReportModuleCatalog Catalog, ReportLocalDate Local,
        ReportDateRange Period);

    private sealed class CacheEntry(string key, Guid sessionId, CancellationTokenSource cancellation,
        long lastAccess)
    {
        public string Key { get; } = key;
        public Guid SessionId { get; } = sessionId;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<object> Task { get; set; } = null!;
        public int Subscribers { get; set; }
        public long LastAccess { get; set; } = lastAccess;
        public DateTimeOffset? CompletedUtc { get; set; }
        public bool Retired { get; set; }
    }

    private static class ReportPreviewCacheKey
    {
        public static string Execution(PreparedRequest request, ReportExecutionRequest executionRequest) =>
            $"execution|{Common(request)}|shape:{DataShape(request.Definition.Visualization)}|" +
            $"page:{executionRequest.TablePage}|size:{executionRequest.TablePageSize}";

        public static string Tree(PreparedRequest request, int pageSize) =>
            $"tree|{Common(request)}|level:1|size:{pageSize}";

        private static string Common(PreparedRequest request)
        {
            var definition = request.Definition;
            var filters = definition.Filters.Select(Filter)
                .OrderBy(x => x, StringComparer.Ordinal);
            return $"session:{request.SessionId:N}|user:{request.UserId:N}|account:{request.AccountId:N}|" +
                $"module:{(int)definition.Module}|version:{definition.DefinitionVersion}|" +
                $"filters:{string.Join(';', filters)}|period:{(int)definition.Period.Rule}:" +
                $"{request.Period.From:yyyy-MM-dd}:{request.Period.To:yyyy-MM-dd}|" +
                $"local:{request.Local.Today:yyyy-MM-dd}:{request.Local.TimeZoneId}|" +
                $"date:{definition.DateBasis}|metric:{definition.Metric}|aggregation:{definition.Aggregation}|" +
                $"group:{Grouping(definition.Grouping)}|time:{definition.TimeGrouping}";
        }

        private static string DataShape(ReportVisualization? visualization) => visualization switch
        {
            ReportVisualization.Table => "table",
            ReportVisualization.RelationshipTree => "tree",
            _ => "aggregate"
        };

        private static string Grouping(ReportGroupingKey? grouping) => grouping switch
        {
            null => "none",
            AgreementReportGroupingKey x => $"agreement:{(int)x.Field}",
            LeasingReportGroupingKey x => $"leasing:{(int)x.Field}",
            ReportDimensionGroupingKey x => $"dimension:{(int)x.Scope}:{x.DimensionId:N}",
            _ => throw new ReportDefinitionValidationException(
                [new ReportValidationIssue("grouping", "ReportDefinitionGroupingInvalid")])
        };

        private static string Filter(ReportFilterSelection selection) => selection switch
        {
            ReportDimensionFilterSelection x =>
                $"dimension:{(int)x.DimensionKey.Scope}:{x.DimensionKey.DimensionId:N}:" +
                $"{x.ValueId:N}:{x.IncludeUnspecified}",
            ReportValueFilterSelection { Key: AgreementReportFilterKey key } x =>
                $"agreement:{(int)key.Field}:{Value(x.Value)}",
            ReportValueFilterSelection { Key: LeasingReportFilterKey key } x =>
                $"leasing:{(int)key.Field}:{Value(x.Value)}",
            _ => throw new ReportDefinitionValidationException(
                [new ReportValidationIssue("filters", "ReportFilterInvalid")])
        };

        private static string Value(ReportFilterValue value) => value switch
        {
            ReportTextValue x => $"text:{x.Value.Trim().ToLowerInvariant()}",
            ReportEntityValue x => $"entity:{x.Id:N}",
            ReportEnumValue<AgreementStatus> x => $"agreement-status:{(int)x.Value}",
            ReportEnumValue<AgreementType> x => $"agreement-type:{(int)x.Value}",
            ReportEnumValue<AgreementAttention> x => $"agreement-attention:{(int)x.Value}",
            ReportEnumValue<LeasingCatalogKind> x => $"leasing-kind:{(int)x.Value}",
            ReportEnumValue<LeasingCatalogStatus> x => $"leasing-status:{(int)x.Value}",
            _ => throw new ReportDefinitionValidationException(
                [new ReportValidationIssue("filters", "ReportFilterInvalid")])
        };
    }
}
