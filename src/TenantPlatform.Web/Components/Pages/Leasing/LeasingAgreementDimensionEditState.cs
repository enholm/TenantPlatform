using TenantPlatform.Web.Services.Leasing;

namespace TenantPlatform.Web.Components.Pages.Leasing;

public sealed class LeasingAgreementDimensionEditState
{
    private readonly Dictionary<Guid, HashSet<Guid>> original;
    private readonly Dictionary<Guid, HashSet<Guid>> selected;
    public IReadOnlyList<LeasingAgreementDimensionOption> Dimensions { get; }
    public bool ReadOnly { get; private set; }
    public bool Inherited { get; private set; }

    public LeasingAgreementDimensionEditState(LeasingAgreementDimensionOptions options)
    {
        Dimensions = options.Dimensions;
        ReadOnly = options.ReadOnly; Inherited = options.Inherited;
        original = Dimensions.ToDictionary(x => x.DimensionId, x => x.Values.Where(v => v.IsAssigned).Select(v => v.Id).ToHashSet());
        selected = original.ToDictionary(x => x.Key, x => new HashSet<Guid>(x.Value));
    }
    public IReadOnlySet<Guid> Selected(Guid dimension) => new HashSet<Guid>(selected[dimension]);
    public bool IsSelected(Guid dimension, Guid value) => selected[dimension].Contains(value);
    public bool CanSelect(Guid dimension, Guid value) => !ReadOnly && Dimensions.Single(x => x.DimensionId == dimension).Values.Any(x => x.Id == value && (x.Selectable || IsSelected(dimension, value)));
    public void SetSingle(Guid dimension, Guid? value)
    {
        if (ReadOnly || value.HasValue && !CanSelect(dimension, value.Value)) return;
        selected[dimension].Clear(); if (value.HasValue) selected[dimension].Add(value.Value);
    }
    public void Toggle(Guid dimension, Guid value, bool include)
    {
        if (ReadOnly) return;
        if (include) { if (CanSelect(dimension, value)) selected[dimension].Add(value); }
        else selected[dimension].Remove(value);
    }
    public void MakeEditable() { ReadOnly = false; Inherited = false; }
    public List<LeasingAgreementDimensionPatch>? Changes(bool creating)
    {
        if (ReadOnly) return null;
        var dimensions = creating ? Dimensions : Dimensions.Where(x => !original[x.DimensionId].SetEquals(selected[x.DimensionId]));
        var result = dimensions.Select(x => new LeasingAgreementDimensionPatch
        {
            DimensionId = x.DimensionId, ValueIds = selected[x.DimensionId].OrderBy(v => v).ToList()
        }).ToList();
        return result.Count == 0 ? null : result;
    }
}
