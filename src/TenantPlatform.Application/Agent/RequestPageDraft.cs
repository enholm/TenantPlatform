using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Application.ServiceRequests;

namespace TenantPlatform.Application.Agent;

// Typed request declaration, independent of agreement filter interpretation. No model-supplied identifiers.
internal static class RequestPageDraft
{
    public static PageDefinition Build(ReportPageProposal intent, Guid connection, PageSource source, AgentReportDraft? previous)
    {
        if (intent.Filter != new ReportPageFilter() || intent.RequestFilter is null) throw new ReadAgentException("DraftInvalid");
        var filter = (previous?.Definition.RequestFilter ?? new ServiceRequestReportFilter()) with { Search = intent.RequestFilter.Search, Status = intent.RequestFilter.Status };
        var definition = new PageDefinition(3, intent.Title, connection, previous?.Definition.QueryVersion ?? source.QueryVersion,
            previous?.Definition.AggregateVersion ?? source.AggregateVersion, intent.Period, new AgreementReportFilter(), intent.Components,
            intent.Layout, previous?.Definition.ProfileVersion ?? source.ProfileVersion, intent.GroupBy!.Value, intent.Columns,
            ReportArea.ServiceRequests, filter);
        PersonalPageService.Validate(definition);
        return definition;
    }
}
