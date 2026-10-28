using TenantPlatform.Application.PersonalPages;

namespace TenantPlatform.Application.Agent;

public enum ReadAgentArea { Agreements, ServiceRequests, External, AgreementDetails, AgreementFinance, Structure }
public sealed record AgentAreaOption(ReadAgentArea Area, int ToolCount);

public static class ReadAgentAreas
{
    // Exact transport contracts, not prefixes or inferred permissions. Intersect with the
    // registry's personally authorized, approved AutomaticRead set on every operation.
    private static readonly IReadOnlyDictionary<string, ReadAgentArea[]> OwnTools = new Dictionary<string, ReadAgentArea[]>(StringComparer.Ordinal)
    {
        ["buildings_query"] = [ReadAgentArea.Structure], ["buildings_aggregate"] = [ReadAgentArea.Structure],
        ["agreements_bases"] = [ReadAgentArea.AgreementFinance], ["agreements_basis"] = [ReadAgentArea.AgreementFinance],
        ["agreements_indices"] = [ReadAgentArea.AgreementFinance], ["agreements_index_values"] = [ReadAgentArea.AgreementFinance],
        ["agreements_financial_lines"] = [ReadAgentArea.AgreementFinance], ["agreements_forecast"] = [ReadAgentArea.AgreementFinance],
        ["agreements_list"] = [ReadAgentArea.Agreements], ["agreements_attention"] = [ReadAgentArea.Agreements],
        ["agreements_query"] = [ReadAgentArea.Agreements, ReadAgentArea.AgreementDetails, ReadAgentArea.AgreementFinance],
        ["agreements_aggregate"] = [ReadAgentArea.Agreements],
        ["agreements_details"] = [ReadAgentArea.AgreementDetails, ReadAgentArea.AgreementFinance],
        ["agreements_followup"] = [ReadAgentArea.AgreementDetails],
        ["agreements_deadlines"] = [ReadAgentArea.AgreementDetails],
        ["agreements_prioritized"] = [ReadAgentArea.Agreements], ["agreements_insights"] = [ReadAgentArea.Agreements],
        ["agreements_followup_history"] = [ReadAgentArea.AgreementDetails],
        ["agreements_relations"] = [ReadAgentArea.Agreements, ReadAgentArea.AgreementDetails, ReadAgentArea.AgreementFinance],
        ["service_requests_query"] = [ReadAgentArea.ServiceRequests], ["service_requests_aggregate"] = [ReadAgentArea.ServiceRequests]
    };
    // A selection is one immutable value in the existing conversation binding. Agreement
    // sub-selections share a parent; they do not add a second, independently mutable scope.
    public static ReadAgentArea Parent(ReadAgentArea selection) => selection is ReadAgentArea.AgreementDetails or ReadAgentArea.AgreementFinance ? ReadAgentArea.Agreements : selection;
    public static bool Reports(ReadAgentArea selection) => selection is ReadAgentArea.Agreements or ReadAgentArea.ServiceRequests;
    public static bool Known(string tool) => OwnTools.ContainsKey(tool);
    public static bool Includes(ReadAgentArea area, bool external, string tool) => Enum.IsDefined(area) &&
        (external ? area == ReadAgentArea.External : OwnTools.TryGetValue(tool, out var mapped) && mapped.Contains(area));
    public static ReadAgentArea FromReport(ReportArea area) => area switch
    {
        ReportArea.Agreements => ReadAgentArea.Agreements, ReportArea.ServiceRequests => ReadAgentArea.ServiceRequests,
        _ => throw new ReadAgentException("AreaInvalid")
    };
    public static ReportArea ToReport(ReadAgentArea area) => area switch
    {
        ReadAgentArea.Agreements => ReportArea.Agreements, ReadAgentArea.ServiceRequests => ReportArea.ServiceRequests,
        _ => throw new ReadAgentException("AreaInvalid")
    };
}
