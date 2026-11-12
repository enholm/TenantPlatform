using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Components.Pages.Agreements;

// Local form state, separate from server-owned DTOs and persisted assignment IDs.
public sealed class AgreementDimensionEditState
{
    private readonly Dictionary<Guid, HashSet<Guid>> original;
    private readonly Dictionary<Guid, HashSet<Guid>> selected;
    public IReadOnlyList<AgreementDimensionOptionDto> Dimensions { get; }
    public Guid Revision { get; }

    public AgreementDimensionEditState(IEnumerable<AgreementDimensionOptionDto> dimensions, Guid revision)
    {
        Dimensions = dimensions.Select(d => d with { Values = d.Values.Select(v => v with { }).ToList() }).ToList();
        Revision = revision;
        original = Dimensions.ToDictionary(d => d.DimensionId, d => d.Values.Where(v => v.IsAssigned).Select(v => v.Id).ToHashSet());
        selected = original.ToDictionary(x => x.Key, x => new HashSet<Guid>(x.Value));
    }

    public bool IsSelected(Guid dimension, Guid value) => selected[dimension].Contains(value);
    public IReadOnlySet<Guid> Selected(Guid dimension) => new HashSet<Guid>(selected[dimension]);
    public bool CanSelect(Guid dimension, Guid value) => Dimensions.Single(d => d.DimensionId == dimension).Values
        .Any(v => v.Id == value && (v.Selectable || IsSelected(dimension, value)));

    public void SetSingle(Guid dimension, Guid? value)
    {
        if (value.HasValue && !CanSelect(dimension, value.Value)) return;
        selected[dimension].Clear();
        if (value.HasValue) selected[dimension].Add(value.Value);
    }

    public void Toggle(Guid dimension, Guid value, bool include)
    {
        if (include)
        {
            if (CanSelect(dimension, value)) selected[dimension].Add(value);
        }
        else selected[dimension].Remove(value);
    }

    public List<AgreementDimensionPatch>? Changes()
    {
        var patches = Dimensions.Where(d => !original[d.DimensionId].SetEquals(selected[d.DimensionId]))
            .Select(d => new AgreementDimensionPatch { DimensionId = d.DimensionId, ValueIds = selected[d.DimensionId].OrderBy(x => x).ToList() }).ToList();
        return patches.Count == 0 ? null : patches;
    }

    public void ApplyTo(SaveAgreementRequest request)
    {
        if (request.Revision != Revision) throw new AgreementValidationException("AgreementConcurrencyConflict");
        request.DimensionChanges = Changes();
    }
}
