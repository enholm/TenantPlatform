using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Leasing;

namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportCatalogService(AgreementService agreements, LeasingService leasing) : IReportCatalogService
{
    public async Task<ReportModuleCatalog> GetAsync(Guid accountId, ReportModule module, CancellationToken ct = default) => module switch
    {
        ReportModule.Agreements => Agreements(await agreements.LoadReportCatalogSourceAsync(accountId, ct)),
        ReportModule.Leasing => Leasing(await leasing.LoadReportCatalogSourceAsync(accountId, ct)),
        _ => throw new ReportCatalogValidationException("ReportFilterInvalid")
    };

    public async Task ValidateAsync(Guid accountId, ReportModule module,
        IReadOnlyCollection<ReportFilterSelection> selections, CancellationToken ct = default) =>
        (await GetAsync(accountId, module, ct)).Validate(selections);

    private static ReportModuleCatalog Agreements(AgreementReportCatalogSource source)
    {
        var filters = new List<ReportFilterDefinition>
        {
            Text(new AgreementReportFilterKey(AgreementReportFilter.Search), "Search"),
            Choice(new AgreementReportFilterKey(AgreementReportFilter.Status), "Status",
                Enum.GetValues<AgreementStatus>().Select(x => Option(new ReportEnumValue<AgreementStatus>(x), $"AgreementStatus{x}"))),
            Choice(new AgreementReportFilterKey(AgreementReportFilter.Type), "AgreementType",
                Enum.GetValues<AgreementType>().Select(x => Option(new ReportEnumValue<AgreementType>(x), $"AgreementType{x}"))),
            Choice(new AgreementReportFilterKey(AgreementReportFilter.Owner), "AgreementOwner",
                source.Owners.Select(x => Option(new ReportEntityValue(x.Id), ReportCatalogLabel.Dynamic(x.Name)))),
            Choice(new AgreementReportFilterKey(AgreementReportFilter.Attention), "DashboardAttention",
                Enum.GetValues<AgreementAttention>().Select(x => Option(new ReportEnumValue<AgreementAttention>(x), $"DashboardAttention{x}")))
        };
        var groupings = new List<ReportGroupingDefinition>
        {
            Group(new AgreementReportGroupingKey(AgreementReportGrouping.Type), "DashboardDimensionType"),
            Group(new AgreementReportGroupingKey(AgreementReportGrouping.Counterparty), "DashboardDimensionCounterparty"),
            Group(new AgreementReportGroupingKey(AgreementReportGrouping.Status), "DashboardDimensionStatus"),
            Group(new AgreementReportGroupingKey(AgreementReportGrouping.Owner), "DashboardDimensionOwner")
        };
        groupings.AddRange(source.Dimensions.Dimensions.Where(x => !x.IsHistorical).Select(x =>
            new ReportGroupingDefinition(new ReportDimensionGroupingKey(ReportDimensionScope.Agreement, x.DimensionId),
                ReportCatalogLabel.Dynamic(x.Name))));
        return new(ReportModule.Agreements, filters, source.Dimensions, ReportDimensionScope.Agreement, groupings, [],
            ReportCompatibilityMatrix.For(ReportModule.Agreements));
    }

    private static ReportModuleCatalog Leasing(LeasingReportCatalogSource source)
    {
        var filters = new List<ReportFilterDefinition>
        {
            Text(new LeasingReportFilterKey(LeasingCatalogFilter.Search), "Search"),
            Choice(new LeasingReportFilterKey(LeasingCatalogFilter.Kind), "LeasingKind",
            [
                Option(new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.All), "AgreementAll"),
                Option(new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.Frameworks), "LeasingFrameworks"),
                Option(new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.Acquisitions), "LeasingAcquisitions"),
                Option(new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.StandaloneAcquisitions), "LeasingStandaloneAcquisitions")
            ]),
            Choice(new LeasingReportFilterKey(LeasingCatalogFilter.Status), "Status",
                Enum.GetValues<LeasingCatalogStatus>().Select(x => Option(new ReportEnumValue<LeasingCatalogStatus>(x), $"LeasingStatus{x}"))),
            Choice(new LeasingReportFilterKey(LeasingCatalogFilter.Owner), "LeasingOwner",
                source.Owners.Select(x => Option(new ReportEntityValue(x.Id), ReportCatalogLabel.Dynamic(x.Name))))
        };
        var groupings = new List<ReportGroupingDefinition>
        {
            Group(new LeasingReportGroupingKey(LeasingReportGrouping.Kind), "LeasingKind"),
            Group(new LeasingReportGroupingKey(LeasingReportGrouping.Status), "Status"),
            Group(new LeasingReportGroupingKey(LeasingReportGrouping.Owner), "LeasingOwner")
        };
        groupings.AddRange(source.Dimensions.Dimensions.Where(x => !x.IsHistorical).Select(x =>
            new ReportGroupingDefinition(new ReportDimensionGroupingKey(ReportDimensionScope.LeasingAgreement, x.DimensionId),
                ReportCatalogLabel.Dynamic(x.Name))));
        return new(ReportModule.Leasing, filters, source.Dimensions, ReportDimensionScope.LeasingAgreement,
            groupings, [ReportDimensionScope.LeasingLineAllocation],
            ReportCompatibilityMatrix.For(ReportModule.Leasing));
    }

    private static ReportFilterDefinition Text(ReportFilterKey key, string resourceKey) =>
        new(key, ReportCatalogLabel.Resource(resourceKey), ReportFilterValueKind.Text, []);

    private static ReportFilterDefinition Choice(ReportFilterKey key, string resourceKey,
        IEnumerable<ReportFilterOption> options) =>
        new(key, ReportCatalogLabel.Resource(resourceKey), ReportFilterValueKind.SingleChoice, options.ToList());

    private static ReportFilterOption Option(ReportFilterValue value, string resourceKey) =>
        new(value, ReportCatalogLabel.Resource(resourceKey));

    private static ReportFilterOption Option(ReportFilterValue value, ReportCatalogLabel label) => new(value, label);

    private static ReportGroupingDefinition Group(ReportGroupingKey key, string resourceKey) =>
        new(key, ReportCatalogLabel.Resource(resourceKey));
}
