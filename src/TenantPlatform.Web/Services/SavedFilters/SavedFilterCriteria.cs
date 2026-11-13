using System.Text.Json;
using System.Text.Json.Serialization;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

namespace TenantPlatform.Web.Services.SavedFilters;

public sealed record SavedDimensionCriterion(Guid DimensionId, Guid? ValueId, bool IncludeUnspecified);

public sealed record AgreementSavedCriteria(
    string? Search,
    AgreementStatus? Status,
    AgreementType? Type,
    Guid? OwnerUserId,
    AgreementAttention? Attention,
    List<SavedDimensionCriterion> Dimensions);

public sealed record AgreementFollowupSavedCriteria(
    string? Search,
    int Range,
    int HorizonDays,
    Guid? AssignedUserId,
    AgreementType? AgreementType,
    AgreementDeadlineKind? Kind,
    AgreementFollowupStatus? Status,
    AgreementDeadlineState? State,
    bool Mine,
    bool IncludeCompleted,
    DateOnly? From,
    DateOnly? To);

public sealed record LeasingSavedCriteria(
    string? Search,
    string Kind,
    string? Status,
    Guid? Owner,
    List<SavedDimensionCriterion> Dimensions);

public static class SavedFilterCriteria
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Capture(AgreementFilter filter) => Serialize(new AgreementSavedCriteria(
        filter.Search, filter.Status, filter.Type, filter.OwnerUserId, filter.Attention, Capture(filter.Dimensions)));

    public static void Apply(string json, AgreementFilter filter)
    {
        var criteria = Read<AgreementSavedCriteria>(json);
        Defined(criteria.Status);
        Defined(criteria.Type);
        Defined(criteria.Attention);
        var dimensions = Restore(criteria.Dimensions);
        filter.Search = criteria.Search;
        filter.Status = criteria.Status;
        filter.Type = criteria.Type;
        filter.OwnerUserId = criteria.OwnerUserId;
        filter.Attention = criteria.Attention;
        Replace(filter.Dimensions, dimensions);
        filter.Page = 1;
    }

    public static string Capture(AgreementDeadlineFilter filter, int range) => Serialize(
        new AgreementFollowupSavedCriteria(filter.Search, range, filter.HorizonDays,
            filter.AssignedUserId, filter.AgreementType, filter.Kind, filter.Status, filter.State,
            filter.Mine, filter.IncludeCompleted, range == 0 ? filter.From : null, range == 0 ? filter.To : null));

    public static int Apply(string json, AgreementDeadlineFilter filter)
    {
        var criteria = Read<AgreementFollowupSavedCriteria>(json);
        if (criteria.Range is not (0 or 30 or 60 or 90) || criteria.HorizonDays is < 1 or > 3650)
            throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
        Defined(criteria.AgreementType);
        Defined(criteria.Kind);
        Defined(criteria.Status);
        Defined(criteria.State);
        filter.Search = criteria.Search;
        filter.AssignedUserId = criteria.AssignedUserId;
        filter.AgreementType = criteria.AgreementType;
        filter.Kind = criteria.Kind;
        filter.Status = criteria.Status;
        filter.State = criteria.State;
        filter.Mine = criteria.Mine;
        filter.IncludeCompleted = criteria.IncludeCompleted;
        filter.CustomDates = criteria.Range == 0;
        filter.HorizonDays = criteria.HorizonDays;
        filter.From = criteria.From;
        filter.To = criteria.To;
        filter.Page = 1;
        return criteria.Range;
    }

    public static string Capture(LeasingListFilter filter) => Serialize(new LeasingSavedCriteria(
        filter.Search, filter.Kind, filter.Status, filter.Owner, Capture(filter.Dimensions)));

    public static void Apply(string json, LeasingListFilter filter)
    {
        var criteria = Read<LeasingSavedCriteria>(json);
        if (criteria.Kind is not ("all" or "frameworks" or "acquisitions" or "orders"))
            throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
        var dimensions = Restore(criteria.Dimensions);
        filter.Search = criteria.Search;
        filter.Kind = criteria.Kind;
        filter.Status = criteria.Status;
        filter.Owner = criteria.Owner;
        Replace(filter.Dimensions, dimensions);
        filter.Page = 1;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Read<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
        }
        catch (JsonException)
        {
            throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
        }
    }

    private static List<SavedDimensionCriterion> Capture(IEnumerable<DimensionListFilterSelection> dimensions) =>
        dimensions.Select(x => new SavedDimensionCriterion(x.DimensionId, x.ValueId, x.IncludeUnspecified)).ToList();

    private static List<DimensionListFilterSelection> Restore(IEnumerable<SavedDimensionCriterion>? dimensions)
    {
        var restored = new List<DimensionListFilterSelection>();
        var seen = new HashSet<Guid>();
        foreach (var item in dimensions ?? [])
        {
            if (item.DimensionId == Guid.Empty || !seen.Add(item.DimensionId) || item.ValueId == Guid.Empty ||
                (item.IncludeUnspecified && item.ValueId.HasValue) ||
                (!item.IncludeUnspecified && !item.ValueId.HasValue))
                throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
            restored.Add(new DimensionListFilterSelection
            {
                DimensionId = item.DimensionId,
                ValueId = item.IncludeUnspecified ? null : item.ValueId,
                IncludeUnspecified = item.IncludeUnspecified
            });
        }
        return restored;
    }

    private static void Replace(List<DimensionListFilterSelection> target,
        IEnumerable<DimensionListFilterSelection> dimensions)
    {
        target.Clear();
        target.AddRange(dimensions);
    }

    private static void Defined<T>(T? value) where T : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw new SavedFilterValidationException("SavedFilterCriteriaInvalid");
    }
}
