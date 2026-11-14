using System.Reflection;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Reports;

static class PreviewChecks
{
    public static async Task RunAsync()
    {
        await CacheAndCanonicalizationAsync();
        await SharedRequestAndCancellationAsync();
        await RefreshExpiryCapacityAndDateAsync();
        await ContextAndSessionIsolationAsync();
        await StaleCompletionAndConcurrencyAsync();
    }

    private static async Task CacheAndCanonicalizationAsync()
    {
        var fixture = new PreviewFixture();
        await using var preview = fixture.Create();
        var first = Definition([
            new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("alpha")),
            new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Status), new ReportEnumValue<AgreementStatus>(AgreementStatus.Active))
        ]);
        var equivalent = first with
        {
            Filters = first.Filters.Reverse().Select(x => x is ReportValueFilterSelection
                { Value: ReportTextValue } text
                    ? text with { Value = new ReportTextValue(" ALPHA ") }
                    : x).ToArray()
        };
        var a = await preview.ExecuteAsync(fixture.Session, fixture.Account, new(first));
        var b = await preview.ExecuteAsync(fixture.Session, fixture.Account, new(equivalent));
        Check.That(fixture.Execution.Calls == 1,
            "preview cache canonicalizes semantically equivalent filter order into one execution");
        Check.That(!ReferenceEquals(a.Totals, b.Totals) && !ReferenceEquals(a.Groups, b.Groups),
            "cache consumers receive independent result collections rather than shared mutable UI state");

        var renamedPresentation = first with { Visualization = ReportVisualization.Donut };
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(renamedPresentation));
        Check.That(fixture.Execution.Calls == 1,
            "presentation-only chart kind reuses the compatible aggregate data result");
        var hiddenTitle = first with
        {
            DisplayJson = ReportDisplaySettingsJson.SetTitlePlacement(
                first.DisplayJson, ReportTitlePlacement.Hidden)
        };
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(hiddenTitle));
        Check.That(fixture.Execution.Calls == 1,
            "title placement is presentation-only and does not change the result cache identity");
        await preview.ExecuteAsync(fixture.Session, fixture.Account,
            new(first with { Visualization = ReportVisualization.Table }));
        Check.That(fixture.Execution.Calls == 2,
            "table presentation uses a distinct data-shape cache entry including paging requirements");

        fixture.Execution.Handler = (_, _) => Task.FromResult(Result("unsafe") with
        {
            Totals = [new(-1)],
            Groups = [new("type", ReportExecutionLabel.Dynamic("Type"), [new(-1)], 1)]
        });
        var unsafeBar = WithSearch("unsafe");
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(unsafeBar));
        await Check.ThrowsAsync<ReportDefinitionValidationException>(() => preview.ExecuteAsync(
                fixture.Session, fixture.Account,
                new(unsafeBar with { Visualization = ReportVisualization.Donut })),
            "presentation changes re-run result validation even when aggregate data is reused from cache");

        var treeDefinition = new ReportCardDefinition(ReportModule.Agreements, 1, [],
            new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
            Visualization: ReportVisualization.RelationshipTree);
        var treeA = await preview.LoadTreeAsync(fixture.Session, fixture.Account, treeDefinition);
        var treeB = await preview.LoadTreeAsync(fixture.Session, fixture.Account, treeDefinition);
        Check.That(fixture.Trees.RootCalls == 1 && fixture.Trees.ChildrenCalls == 1 &&
            treeA.Root.Id == treeB.Root.Id,
            "passive and interactive tree presentation can reuse one authorized first-level tree result");
    }

    private static async Task SharedRequestAndCancellationAsync()
    {
        var fixture = new PreviewFixture();
        var release = NewGate();
        fixture.Execution.Handler = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return Result("shared");
        };
        await using var preview = fixture.Create();
        using var cancelledSubscriber = new CancellationTokenSource();
        var first = preview.ExecuteAsync(fixture.Session, fixture.Account, new(Definition()),
            ct: cancelledSubscriber.Token);
        var second = preview.ExecuteAsync(fixture.Session, fixture.Account, new(Definition()));
        await WaitUntilAsync(() => fixture.Execution.Calls == 1);
        cancelledSubscriber.Cancel();
        await Check.ThrowsAsync<OperationCanceledException>(() => first,
            "one preview subscriber can cancel without surfacing a normal report failure");
        Check.That(!fixture.Execution.LastToken.IsCancellationRequested,
            "one subscriber cancellation does not cancel an identical request still used by another card");
        release.TrySetResult();
        Check.That((await second).Metadata.SourceRule == "shared" && fixture.Execution.Calls == 1,
            "parallel identical preview requests are coalesced into one execution");

        var failedOnce = false;
        fixture.Execution.Handler = (_, _) =>
        {
            if (!failedOnce) { failedOnce = true; throw new ReportExecutionException("expected"); }
            return Task.FromResult(Result("retried"));
        };
        var changed = Definition([new ReportValueFilterSelection(
            new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("retry"))]);
        await Check.ThrowsAsync<ReportExecutionException>(() =>
            preview.ExecuteAsync(fixture.Session, fixture.Account, new(changed)),
            "failed preview execution is returned to the current card");
        Check.That((await preview.ExecuteAsync(fixture.Session, fixture.Account, new(changed))).Metadata.SourceRule == "retried",
            "failed preview executions are removed from cache and can be retried");
    }

    private static async Task RefreshExpiryCapacityAndDateAsync()
    {
        var fixture = new PreviewFixture(new ReportPreviewOptions
        {
            MaximumAge = TimeSpan.FromMinutes(1), MaximumEntries = 2,
            MaximumConcurrentExecutions = 2, TreePageSize = 2
        });
        await using var preview = fixture.Create();
        var definition = Definition();
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition), forceRefresh: true);
        Check.That(fixture.Execution.Calls == 2, "explicit preview refresh bypasses a fresh cache entry");

        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        Check.That(fixture.Execution.Calls == 3, "preview cache entries expire at the configured maximum age");

        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(WithSearch("two")));
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(WithSearch("three")));
        Check.That(preview.Snapshot.CachedEntries == 2,
            "preview cache evicts least-recently-used completed entries at its configured capacity");

        var beforeDateChange = fixture.Execution.Calls;
        var relative = Definition(period: new(ReportPeriodRule.CurrentMonth));
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(relative));
        fixture.LocalDates.Today = new(2026, 12, 1);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(relative));
        Check.That(fixture.Execution.Calls == beforeDateChange + 2,
            "relative-period cache identity changes with the resolved local interval across a date boundary");
    }

    private static async Task ContextAndSessionIsolationAsync()
    {
        var fixture = new PreviewFixture();
        await using var preview = fixture.Create();
        var definition = Definition();
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        fixture.Current.UserId = Guid.NewGuid();
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        Check.That(fixture.Execution.Calls == 2,
            "user identity changes invalidate scoped preview entries even in the same service scope");

        fixture.Authorization.Allowed = false;
        await Check.ThrowsAsync<UnauthorizedAccessException>(() =>
            preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition)),
            "cached previews still re-check current module access");
        fixture.Authorization.Allowed = true;
        await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        Check.That(fixture.Execution.Calls == 3,
            "an access denial invalidates cached data before access is restored");

        var cancellationObserved = NewGate();
        fixture.Execution.Handler = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
            return Result("unreachable");
        };
        var pending = preview.ExecuteAsync(fixture.Session, fixture.Account, new(WithSearch("session-end")));
        await WaitUntilAsync(() => fixture.Execution.Active == 1);
        preview.EndSession(fixture.Session);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Check.ThrowsAsync<OperationCanceledException>(() => pending,
            "ending a report-page session cancels its remaining preview work");
    }

    private static async Task StaleCompletionAndConcurrencyAsync()
    {
        var fixture = new PreviewFixture(new ReportPreviewOptions
        {
            MaximumAge = TimeSpan.FromMinutes(5), MaximumEntries = 16,
            MaximumConcurrentExecutions = 2, TreePageSize = 12
        });
        var firstGate = NewGate();
        var secondGate = NewGate();
        fixture.Execution.Handler = async (call, ct) =>
        {
            await (call == 1 ? firstGate.Task : secondGate.Task).WaitAsync(ct);
            return Result(call == 1 ? "stale" : "current");
        };
        await using var preview = fixture.Create();
        var definition = Definition();
        var stale = preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition));
        await WaitUntilAsync(() => fixture.Execution.Calls == 1);
        var current = preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition), forceRefresh: true);
        await WaitUntilAsync(() => fixture.Execution.Calls == 2);
        secondGate.TrySetResult();
        Check.That((await current).Metadata.SourceRule == "current", "new forced execution completes with current data");
        firstGate.TrySetResult();
        Check.That((await stale).Metadata.SourceRule == "stale", "the original subscriber can finish its retired request");
        Check.That((await preview.ExecuteAsync(fixture.Session, fixture.Account, new(definition))).Metadata.SourceRule == "current",
            "a late retired response cannot replace the newer cached result");

        var concurrencyFixture = new PreviewFixture(new ReportPreviewOptions
        {
            MaximumAge = TimeSpan.FromMinutes(5), MaximumEntries = 16,
            MaximumConcurrentExecutions = 2, TreePageSize = 12
        });
        var release = NewGate();
        concurrencyFixture.Execution.Handler = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return Result("limited");
        };
        await using var limited = concurrencyFixture.Create();
        var tasks = Enumerable.Range(0, 4).Select(index => limited.ExecuteAsync(
            concurrencyFixture.Session, concurrencyFixture.Account, new(WithSearch($"parallel-{index}")))).ToArray();
        await WaitUntilAsync(() => concurrencyFixture.Execution.Active == 2);
        Check.That(concurrencyFixture.Execution.MaximumActive == 2,
            "preview session starts no more than the configured number of report executions");
        release.TrySetResult();
        await Task.WhenAll(tasks);
        Check.That(limited.Snapshot.MaximumObservedExecutions == 2,
            "preview concurrency diagnostics confirm the configured ceiling");
    }

    private static ReportCardDefinition WithSearch(string value) => Definition([
        new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search),
            new ReportTextValue(value))
    ]);

    private static ReportCardDefinition Definition(IReadOnlyList<ReportFilterSelection>? filters = null,
        ReportPeriodSelection? period = null) => new(ReportModule.Agreements, 1, filters ?? [],
        period ?? new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
        ReportMetric.AgreementCount, ReportAggregation.CountDistinct,
        Grouping: new AgreementReportGroupingKey(AgreementReportGrouping.Type),
        Visualization: ReportVisualization.Bar);

    private static ReportExecutionResult Result(string source) => new(ReportModule.Agreements,
        ReportMetric.AgreementCount, new(new(2026, 1, 1), new(2026, 12, 31)), new(2026, 11, 13),
        "Europe/Oslo", [new(2)], [new("type", ReportExecutionLabel.Dynamic("Type"), [new(2)], 2)], [], null,
        new(ReportGrain.Agreement, ReportValueUnit.Count, ReportCurrencyRule.None,
            ReportAdditivity.UniqueBeforeGroupExpansion, true, true, false, false, 2, 2, source, []));

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(5);
        if (!predicate()) throw new TimeoutException("Timed out waiting for deterministic preview test state.");
    }

    private sealed class PreviewFixture
    {
        private readonly ReportPreviewOptions options;
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Session { get; } = Guid.NewGuid();
        public MutableContext Current { get; }
        public FakeExecution Execution { get; } = new();
        public FakeTrees Trees { get; } = new();
        public FakeAuthorization Authorization { get; } = new();
        public FakeLocalDates LocalDates { get; } = new();
        public MutableClock Clock { get; } = new(new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero));

        public PreviewFixture(ReportPreviewOptions? options = null)
        {
            this.options = options ?? new();
            Current = new() { UserId = Guid.NewGuid(), AccountId = Account };
        }

        public ReportPreviewSession Create() => new(Execution, Trees, new FakeCatalog(),
            new FakeCompatibility(), LocalDates, Authorization.Service, Current, Clock, Options.Create(options));
    }

    private sealed class FakeExecution : IReportExecutionService
    {
        private int calls;
        private int active;
        private int maximumActive;
        public int Calls => Volatile.Read(ref calls);
        public int Active => Volatile.Read(ref active);
        public int MaximumActive => Volatile.Read(ref maximumActive);
        public CancellationToken LastToken { get; private set; }
        public Func<int, CancellationToken, Task<ReportExecutionResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(Result("default"));

        public async Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request,
            CancellationToken ct = default)
        {
            LastToken = ct;
            var call = Interlocked.Increment(ref calls);
            var now = Interlocked.Increment(ref active);
            int observed;
            while (now > (observed = maximumActive) &&
                   Interlocked.CompareExchange(ref maximumActive, now, observed) != observed) { }
            try { return await Handler(call, ct); }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    private sealed class FakeTrees : IReportTreeService
    {
        private static readonly ReportTreeNode Root = new(
            new(ReportModule.Agreements, ReportTreeNodeType.Account, Guid.NewGuid()), "Account", null, true, null);
        public int RootCalls { get; private set; }
        public int ChildrenCalls { get; private set; }
        public Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition, CancellationToken ct = default)
        {
            RootCalls++;
            return Task.FromResult(Root);
        }
        public Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition, ReportTreeNodeId parentId, int offset = 0, int pageSize = 50, CancellationToken ct = default) =>
            CountChildren();
        private Task<ReportTreeNodePage> CountChildren()
        {
            ChildrenCalls++;
            return Task.FromResult(new ReportTreeNodePage([], 0, 0, null));
        }
        public Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId, ReportCardDefinition definition, string query, int limit = 25, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportTreeSearchHit>>([]);
        public Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition, ReportTreeNodeId nodeId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCatalog : IReportCatalogService
    {
        public Task<ReportModuleCatalog> GetAsync(Guid accountId, ReportModule module, CancellationToken ct = default) =>
            Task.FromResult(new ReportModuleCatalog(module, [], null!, ReportDimensionScope.Agreement,
                [], [], ReportCompatibilityMatrix.For(module)));
        public Task ValidateAsync(Guid accountId, ReportModule module, IReadOnlyCollection<ReportFilterSelection> selections, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeCompatibility : IReportDefinitionCompatibilityValidator
    {
        public ReportDefinitionSemantics Validate(ReportCardDefinition definition, ReportModuleCatalog catalog) =>
            new(ReportGrain.Agreement, ReportCurrencyRule.None, ReportAdditivity.UniqueBeforeGroupExpansion,
                true, true, false, "test");
    }

    private sealed class FakeLocalDates : IReportLocalDateService
    {
        public DateOnly Today { get; set; } = new(2026, 11, 30);
        public Task<ReportLocalDate> GetAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(new ReportLocalDate(Today, "Europe/Oslo"));
    }

    private sealed class MutableContext : ICurrentUserContextService
    {
        public Guid UserId { get; set; }
        public Guid AccountId { get; set; }
        public CurrentUserContext Current => new()
        {
            IsAuthenticated = true, UserId = UserId, CurrentAccountId = AccountId
        };
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan value) => current = current.Add(value);
    }

    private sealed class FakeAuthorization
    {
        private readonly AuthorizationProxy proxy;
        public ITenantAuthorizationService Service { get; }
        public bool Allowed { get => proxy.Allowed; set => proxy.Allowed = value; }
        public FakeAuthorization()
        {
            Service = DispatchProxy.Create<ITenantAuthorizationService, AuthorizationProxy>();
            proxy = (AuthorizationProxy)Service;
        }
    }

    private class AuthorizationProxy : DispatchProxy
    {
        public bool Allowed { get; set; } = true;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.ReturnType == typeof(Task<bool>)) return Task.FromResult(Allowed);
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
