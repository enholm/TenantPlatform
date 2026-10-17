using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public static class LeasingClassificationRules
{
    private static bool Precision(decimal value, int places) => decimal.Round(value, places) == value;
    public static void Apply(LeasingItem item, LeasingClassificationInput input,
        LeasingClassificationCatalog catalog, bool registered)
    {
        void Invalid(string key = "LeasingInvalidClassification") => throw new LeasingValidationException(key);
        if (!Enum.IsDefined(input.Mode) || input.VaryingDimensions.Distinct().Count() != input.VaryingDimensions.Count) Invalid();
        var options = catalog.Dimensions.ToDictionary(x => x.Dimension.Id);
        foreach (var id in input.VaryingDimensions)
            if (!options.TryGetValue(id, out var d) || !d.Dimension.IsActive || !d.Rule.IsEnabled || !d.Rule.AllowAllocation || d.Dimension.AllowMultiple) Invalid("LeasingAllocationDimensionUnavailable");
        if (input.Mode == LeasingAllocationMode.None && (input.Rows.Count != 0 || input.VaryingDimensions.Count != 0)) Invalid();
        if (registered && input.Mode != LeasingAllocationMode.None && input.VaryingDimensions.Count == 0) Invalid("LeasingAllocationChooseDimension");
        var oldRows = item.AllocationRows.ToDictionary(x => x.Id);
        var suppliedIds = input.Rows.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (suppliedIds.Distinct().Count() != suppliedIds.Count || suppliedIds.Any(x => !oldRows.ContainsKey(x))) Invalid();
        if (input.Rows.Any(x => x.InputValue < 0 || x.InputValue > 1000000000000m || !Precision(x.InputValue, input.Mode == LeasingAllocationMode.NetAmount ? 2 : 4))) Invalid("LeasingInvalidAllocation");
        var selections = new List<LeasingDimensionSelection>();
        void Choices(List<LeasingDimensionChoice> choices, Guid? row)
        {
            if (choices.Distinct().Count() != choices.Count) Invalid();
            foreach (var group in choices.GroupBy(x => x.DimensionId))
            {
                if (!options.TryGetValue(group.Key, out var d)) { Invalid(); return; }
                if (!d.Rule.IsEnabled || !d.Dimension.IsActive || (!d.Dimension.AllowMultiple && group.Count() > 1) ||
                    (row.HasValue != input.VaryingDimensions.Contains(group.Key))) Invalid();
                foreach (var choice in group)
                {
                    var value = d.Values.SingleOrDefault(x => x.Id == choice.ValueId);
                    if (value == null || !value.Selectable) { Invalid(); return; }
                    selections.Add(new() { Id = Guid.NewGuid(), AccountId = item.AccountId, ItemId = item.Id, AllocationRowId = row,
                        DimensionId = d.Dimension.Id, ValueId = value.Id, DimensionName = d.Dimension.Name, DimensionCode = d.Dimension.Code,
                        ValueName = value.Name, ValueCode = value.Code, ValuePath = value.Path });
                }
            }
        }
        Choices(input.Common, null);
        var rows = new List<LeasingAllocationRow>();
        foreach (var (row, position) in input.Rows.Select((r, i) => (r, i)))
        {
            var target = row.Id.HasValue ? oldRows[row.Id.Value] : new LeasingAllocationRow { Id = Guid.NewGuid(), AccountId = item.AccountId, ItemId = item.Id };
            target.Position = position; target.InputValue = row.InputValue; rows.Add(target); Choices(row.Choices, target.Id);
            if (registered && input.VaryingDimensions.Any(d => !row.Choices.Any(c => c.DimensionId == d))) Invalid("LeasingAllocationChooseRowValues");
        }
        var dimensions = input.VaryingDimensions.Select(id => item.AllocationDimensions.SingleOrDefault(x => x.DimensionId == id) ??
            new LeasingAllocationDimension { AccountId = item.AccountId, ItemId = item.Id, DimensionId = id }).ToList();
        foreach (var dim in dimensions) { dim.DimensionName = options[dim.DimensionId].Dimension.Name; dim.DimensionCode = options[dim.DimensionId].Dimension.Code; }
        item.AllocationMode = input.Mode; item.AllocationDimensions = dimensions; item.AllocationRows = rows; item.DimensionSelections = selections;
        if (registered && LeasingClassificationService.MissingDimensions(item, catalog).Count != 0) Invalid("LeasingMissingDimensions");
    }
}
