using System.Text.Json;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Reports;

internal static class ReportFilterJson
{
    private sealed record StoredFilter(
        string Kind,
        int? Field = null,
        int? Scope = null,
        Guid? DimensionId = null,
        string? ValueKind = null,
        string? Text = null,
        Guid? EntityId = null,
        int? EnumValue = null,
        Guid? ValueId = null,
        bool IncludeUnspecified = false);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<ReportFilterSelection> filters) =>
        JsonSerializer.Serialize(filters.Select(Store).ToList(), Options);

    public static IReadOnlyList<ReportFilterSelection> Deserialize(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<StoredFilter>>(json, Options) ?? []).Select(Restore).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new ReportPageValidationException("ReportFilterInvalid");
        }
    }

    private static StoredFilter Store(ReportFilterSelection selection) => selection switch
    {
        ReportDimensionFilterSelection x => new("dimension", Scope: (int)x.DimensionKey.Scope,
            DimensionId: x.DimensionKey.DimensionId, ValueId: x.ValueId, IncludeUnspecified: x.IncludeUnspecified),
        ReportValueFilterSelection { Key: AgreementReportFilterKey key } x =>
            Value("agreement", (int)key.Field, x.Value),
        ReportValueFilterSelection { Key: LeasingReportFilterKey key } x =>
            Value("leasing", (int)key.Field, x.Value),
        _ => throw new ReportPageValidationException("ReportFilterInvalid")
    };

    private static StoredFilter Value(string kind, int field, ReportFilterValue value) => value switch
    {
        ReportTextValue x => new(kind, field, ValueKind: "text", Text: x.Value),
        ReportEntityValue x => new(kind, field, ValueKind: "entity", EntityId: x.Id),
        ReportEnumValue<AgreementStatus> x => new(kind, field, ValueKind: "enum", EnumValue: (int)x.Value),
        ReportEnumValue<AgreementType> x => new(kind, field, ValueKind: "enum", EnumValue: (int)x.Value),
        ReportEnumValue<AgreementAttention> x => new(kind, field, ValueKind: "enum", EnumValue: (int)x.Value),
        ReportEnumValue<LeasingCatalogKind> x => new(kind, field, ValueKind: "enum", EnumValue: (int)x.Value),
        ReportEnumValue<LeasingCatalogStatus> x => new(kind, field, ValueKind: "enum", EnumValue: (int)x.Value),
        _ => throw new ReportPageValidationException("ReportFilterInvalid")
    };

    private static ReportFilterSelection Restore(StoredFilter stored)
    {
        if (stored.Kind == "dimension" && stored.Scope.HasValue && stored.DimensionId.HasValue)
            return new ReportDimensionFilterSelection(
                new((ReportDimensionScope)stored.Scope.Value, stored.DimensionId.Value),
                stored.ValueId, stored.IncludeUnspecified);
        if (!stored.Field.HasValue) throw new ReportPageValidationException("ReportFilterInvalid");
        return stored.Kind switch
        {
            "agreement" => Agreement((AgreementReportFilter)stored.Field.Value, stored),
            "leasing" => Leasing((LeasingCatalogFilter)stored.Field.Value, stored),
            _ => throw new ReportPageValidationException("ReportFilterInvalid")
        };
    }

    private static ReportFilterSelection Agreement(AgreementReportFilter field, StoredFilter stored) =>
        new ReportValueFilterSelection(new AgreementReportFilterKey(field), field switch
        {
            AgreementReportFilter.Search => Text(stored),
            AgreementReportFilter.Status => Enum<AgreementStatus>(stored),
            AgreementReportFilter.Type => Enum<AgreementType>(stored),
            AgreementReportFilter.Owner => Entity(stored),
            AgreementReportFilter.Attention => Enum<AgreementAttention>(stored),
            _ => throw new ReportPageValidationException("ReportFilterInvalid")
        });

    private static ReportFilterSelection Leasing(LeasingCatalogFilter field, StoredFilter stored) =>
        new ReportValueFilterSelection(new LeasingReportFilterKey(field), field switch
        {
            LeasingCatalogFilter.Search => Text(stored),
            LeasingCatalogFilter.Kind => Enum<LeasingCatalogKind>(stored),
            LeasingCatalogFilter.Status => Enum<LeasingCatalogStatus>(stored),
            LeasingCatalogFilter.Owner => Entity(stored),
            _ => throw new ReportPageValidationException("ReportFilterInvalid")
        });

    private static ReportTextValue Text(StoredFilter x) => x.ValueKind == "text" && x.Text is not null
        ? new(x.Text) : throw new ReportPageValidationException("ReportFilterInvalid");

    private static ReportEntityValue Entity(StoredFilter x) => x.ValueKind == "entity" && x.EntityId.HasValue
        ? new(x.EntityId.Value) : throw new ReportPageValidationException("ReportFilterInvalid");

    private static ReportEnumValue<T> Enum<T>(StoredFilter x) where T : struct, Enum
    {
        if (x.ValueKind != "enum" || !x.EnumValue.HasValue || !System.Enum.IsDefined(typeof(T), x.EnumValue.Value))
            throw new ReportPageValidationException("ReportFilterInvalid");
        return new((T)System.Enum.ToObject(typeof(T), x.EnumValue.Value));
    }
}
