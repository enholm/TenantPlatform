using System.Diagnostics.CodeAnalysis;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Reports;

public enum ReportFilterValueKind { Text, SingleChoice }
public enum ReportDimensionScope { Agreement, LeasingAgreement, LeasingLineAllocation }
public enum AgreementReportFilter { Search, Status, Type, Owner, Attention }
public enum LeasingCatalogFilter { Search, Kind, Status, Owner }
public enum AgreementReportGrouping { Type, Counterparty, Status, Owner }
public enum LeasingReportGrouping { Kind, Status, Owner }
public enum LeasingCatalogKind { All, Frameworks, Acquisitions, StandaloneAcquisitions }
public enum LeasingCatalogStatus { Open, Closed, Finished, Draft, Registered, Cancelled }

public abstract record ReportFilterKey;
public sealed record AgreementReportFilterKey(AgreementReportFilter Field) : ReportFilterKey;
public sealed record LeasingReportFilterKey(LeasingCatalogFilter Field) : ReportFilterKey;
public sealed record ReportDimensionFilterKey(ReportDimensionScope Scope, Guid DimensionId) : ReportFilterKey;

public abstract record ReportGroupingKey;
public sealed record AgreementReportGroupingKey(AgreementReportGrouping Field) : ReportGroupingKey;
public sealed record LeasingReportGroupingKey(LeasingReportGrouping Field) : ReportGroupingKey;
public sealed record ReportDimensionGroupingKey(ReportDimensionScope Scope, Guid DimensionId) : ReportGroupingKey;

public abstract record ReportFilterValue;
public sealed record ReportTextValue(string Value) : ReportFilterValue;
public sealed record ReportEntityValue(Guid Id) : ReportFilterValue;
public sealed record ReportEnumValue<TEnum>(TEnum Value) : ReportFilterValue where TEnum : struct, Enum;

public sealed record ReportCatalogLabel(string? ResourceKey, string? Text)
{
    public static ReportCatalogLabel Resource(string key) => new(key, null);
    public static ReportCatalogLabel Dynamic(string text) => new(null, text);
}

public sealed record ReportFilterOption(ReportFilterValue Value, ReportCatalogLabel Label);
public sealed record ReportFilterDefinition(ReportFilterKey Key, ReportCatalogLabel Label,
    ReportFilterValueKind ValueKind, IReadOnlyList<ReportFilterOption> Options);
public sealed record ReportDimensionFilterDefinition(ReportDimensionFilterKey Key, DimensionListFilterOption Definition);
public sealed record ReportGroupingDefinition(ReportGroupingKey Key, ReportCatalogLabel Label);

public abstract record ReportFilterSelection(ReportFilterKey Key);
public sealed record ReportValueFilterSelection(ReportFilterKey Key, ReportFilterValue Value) : ReportFilterSelection(Key);
public sealed record ReportDimensionFilterSelection(ReportDimensionFilterKey DimensionKey, Guid? ValueId, bool IncludeUnspecified)
    : ReportFilterSelection(DimensionKey);

public sealed class ReportCatalogValidationException(string resourceKey) : Exception(resourceKey);

public sealed record ReportModuleCatalog(
    ReportModule Module,
    IReadOnlyList<ReportFilterDefinition> Filters,
    DimensionListFilterCatalog DimensionFilters,
    ReportDimensionScope DimensionScope,
    IReadOnlyList<ReportGroupingDefinition> Groupings,
    IReadOnlyList<ReportDimensionScope> DeferredDimensionScopes,
    ReportCompatibilityCatalog Compatibility)
{
    public IReadOnlyList<ReportDimensionFilterDefinition> DynamicFilters => DimensionFilters.Dimensions
        .Select(x => new ReportDimensionFilterDefinition(new ReportDimensionFilterKey(DimensionScope, x.DimensionId), x))
        .ToList();

    public void Validate(IReadOnlyCollection<ReportFilterSelection> selections)
    {
        var seen = new HashSet<ReportFilterKey>();
        foreach (var selection in selections)
        {
            if (selection is null || !seen.Add(selection.Key)) Invalid();
            if (selection is ReportDimensionFilterSelection dimensionSelection)
            {
                ValidateDimension(dimensionSelection);
                continue;
            }
            var valueSelection = selection as ReportValueFilterSelection;
            if (valueSelection is null) Invalid();
            var definition = Filters.SingleOrDefault(x => x.Key == valueSelection.Key);
            if (definition is null) Invalid();
            if (definition.ValueKind == ReportFilterValueKind.Text)
            {
                if (valueSelection.Value is not ReportTextValue) Invalid();
            }
            else if (!definition.Options.Any(x => x.Value == valueSelection.Value)) Invalid();
        }
    }

    public void ValidateGrouping(ReportGroupingKey grouping)
    {
        if (grouping is null || !Groupings.Any(x => x.Key == grouping)) Invalid();
    }

    private void ValidateDimension(ReportDimensionFilterSelection selection)
    {
        if (selection.DimensionKey.Scope != DimensionScope || selection.DimensionKey.DimensionId == Guid.Empty ||
            selection.IncludeUnspecified == selection.ValueId.HasValue)
            Invalid();
        try
        {
            var plan = DimensionListFilters.Plan(DimensionFilters,
            [
                new DimensionListFilterSelection
                {
                    DimensionId = selection.DimensionKey.DimensionId,
                    ValueId = selection.ValueId,
                    IncludeUnspecified = selection.IncludeUnspecified
                }
            ]);
            if (plan.Count != 1) Invalid();
        }
        catch (DimensionValidationException)
        {
            Invalid();
        }
    }

    [DoesNotReturn]
    private static void Invalid() => throw new ReportCatalogValidationException("ReportFilterInvalid");
}

public interface IReportCatalogService
{
    Task<ReportModuleCatalog> GetAsync(Guid accountId, ReportModule module, CancellationToken ct = default);
    Task ValidateAsync(Guid accountId, ReportModule module,
        IReadOnlyCollection<ReportFilterSelection> selections, CancellationToken ct = default);
}
