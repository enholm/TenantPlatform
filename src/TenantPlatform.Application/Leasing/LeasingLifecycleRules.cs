using System.Text.Json;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public static class LeasingLifecycleRules
{
    public static List<string> ClosureIssues(LeasingLifecycleState graph, LeasingLifecycle state, LeasingLifecyclePartyInvoices party)
    {
        var a=graph.Acquisition!;var issues=new List<string>();
        if(graph.Equipment.Any(x=>x.Status is LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn))issues.Add("LifeOpenEquipment");
        if(graph.Followups.Any(x=>x.Status is LeasingFollowupStatus.Open or LeasingFollowupStatus.InProgress))issues.Add("LifeOpenFollowups");
        var plans=graph.Plans.Where(x=>x.Status==LeasingPlanStatus.Active).ToList();var terms=plans.SelectMany(x=>x.Terms).ToList();
        if(plans.Count==0||state.PaymentPlanReviewRequired||terms.Any(x=>x.NeedsReview))issues.Add("LifePlanIncomplete");
        if(terms.Any(x=>x.DueDate>=(state.ClosedDate??state.AgreedEndDate)))issues.Add("LifeRemainingTerms");
        var approved=graph.ApprovedInvoices.ToDictionary(x=>x.Id);
        foreach(var term in terms)
        {
            var links=graph.Allocations.Where(x=>!x.Reversed&&x.InstallmentId==term.InstallmentId&&approved.ContainsKey(x.InvoiceId)).ToList();
            var net=links.Sum(x=>(approved[x.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1)*x.Net);var vat=links.Sum(x=>(approved[x.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1)*x.Vat);
            var status=LeasingPaymentCalculator.Status(term,graph.Installments.Single(x=>x.Id==term.InstallmentId),net,vat,links.Count>0);
            if(status is not(LeasingBillingStatus.Reconciled or LeasingBillingStatus.Accepted))issues.Add("LifeDocumentIssues");
        }
        foreach(var item in a.Items.Where(x=>x.CountableEquipment))if(graph.Dispositions.Where(x=>x.ItemId==item.Id&&!x.Reversed).Sum(x=>x.Quantity)<item.Quantity)issues.Add("LifeOpenEquipment");
        foreach(var invoice in party.Invoices)
        {
            var links=party.Allocations.Where(x=>x.InvoiceId==invoice.Id&&!x.Reversed).ToList();
            if(invoice.Status==LeasingInvoiceStatus.Review||links.Sum(x=>x.Net)!=invoice.Net||links.Sum(x=>x.Vat)!=invoice.Vat){issues.Add("LifeUnallocatedPartyInvoices");break;}
        }
        return issues.Distinct().ToList();
    }
    // Called inside the enclosing invoice transaction. These are raw account-bounded facts,
    // never a second permission model or an independent commit.
    public static void InvalidateDocumentReview(LeasingInvoice invoice,IReadOnlyList<LeasingFinancingRevision> revisions,IReadOnlyList<LeasingAcquisition> acquisitions,IReadOnlyList<LeasingLifecycle> lifecycles)
    {
        var revised=revisions.Where(x=>x.AccountId==invoice.AccountId).Where(x=>{var f=JsonSerializer.Deserialize<LeasingFinancingSnapshot>(x.SnapshotJson)!;return f.FinanceOrganizationId==invoice.FinanceOrganizationId&&f.Currency==invoice.Currency;}).Select(x=>x.AcquisitionId).ToHashSet();
        var candidates=acquisitions.Where(x=>x.AccountId==invoice.AccountId&&x.Currency==invoice.Currency&&(x.FinanceOrganizationId==invoice.FinanceOrganizationId||revised.Contains(x.Id))).Select(x=>x.Id).ToHashSet();
        foreach(var state in lifecycles.Where(x=>x.AccountId==invoice.AccountId&&candidates.Contains(x.AcquisitionId)&&x.DocumentReviewComplete)){state.DocumentReviewComplete=false;state.Revision=Guid.NewGuid();}
    }
}
