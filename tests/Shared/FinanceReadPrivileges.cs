// Test-only SQL for disposable PostgreSQL roles; not used by runtime startup.
static class FinanceReadPrivileges
{
    public static string Sql(string role) => role is "mcp_reader" or "internal_mcp_reader" ? $"""
GRANT SELECT ("Id", "AccountId", "AgreementId", "Direction", "InvoiceDate", "OriginalBasisId", "Status", "Revision", "GeneratedUtc") ON public.agreement_bases TO {role};
GRANT SELECT ("Id", "AccountId", "AgreementId", "BasisId", "Revision", "DataJson") ON public.agreement_basis_snapshots TO {role};
GRANT SELECT ("Code", "Description", "Source", "Resolution") ON public.agreement_indices TO {role};
GRANT SELECT ("Id", "AccountId", "AgreementId") ON public.agreement_lines TO {role};
GRANT SELECT ("Id", "AccountId", "AgreementId", "LineId", "Sequence", "EffectiveFrom", "RecordedUtc", "Name", "Description", "StartDate", "EndDate", "FirstPayableDate", "PayableSourceLineId", "PayableSourceStartDate", "PayableOffsetMonths", "Frequency", "Anchor", "AnchorDate", "BillingTiming", "Status", "IndexRegulated") ON public.agreement_line_versions TO {role};
GRANT SELECT ("Id", "AccountId", "AgreementId", "LineId", "Sequence", "Independent", "EffectiveFrom", "IndexBaseDate", "Quantity", "UnitPrice", "RecordedUtc") ON public.agreement_price_versions TO {role};
GRANT SELECT ("Id", "AccountId", "AgreementId", "IndexId", "Sequence", "EffectiveFrom") ON public.agreement_index_selections TO {role};
GRANT SELECT ("Id", "AccountId", "IndexId", "Period", "Revision", "Superseded", "PeriodKey", "Value", "PublishedDate") ON public.agreement_index_values TO {role};
""" : throw new ArgumentException();
}
