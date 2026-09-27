using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing.Invoices;
public static class UblInvoiceParser
{
    public const string Version = "UBL-2.1-BIS3-v1";
    private static readonly XNamespace C = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace A = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public static InvoiceData Parse(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root ?? throw new LeasingValidationException("InvoiceUnsupportedXml");
        var credit = root.Name == XName.Get("CreditNote", "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2");
        if (!credit && root.Name != XName.Get("Invoice", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2")) throw new LeasingValidationException("InvoiceUnsupportedXml");
        if (Text(root, "UBLVersionID") is not ("" or "2.1") || Text(root, credit ? "CreditNoteTypeCode" : "InvoiceTypeCode") != (credit ? "381" : "380")) throw new LeasingValidationException("InvoiceUnsupportedXml");
        var customization = Text(root, "CustomizationID");
        if (customization.Length > 0 && customization != "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0") throw new LeasingValidationException("InvoiceUnsupportedXml");
        if (root.Descendants(A + "WithholdingTaxTotal").Any() || root.Descendants(A + "SubInvoiceLine").Any() || root.Descendants(A + "SubCreditNoteLine").Any() || root.Element(C + "TaxCurrencyCode") != null) throw new LeasingValidationException("InvoiceUnsupportedXml");
        var supplier = root.Element(A + "AccountingSupplierParty")?.Element(A + "Party");
        var legal = supplier?.Element(A + "PartyLegalEntity");
        var totals = root.Element(A + "LegalMonetaryTotal");
        var d = new InvoiceData { Kind = credit ? LeasingInvoiceKind.CreditNote : LeasingInvoiceKind.Invoice,
            Number = Text(root, "ID"), Date = Date(root, "IssueDate"), DueDate = Date(root, "DueDate"), Currency = Text(root, "DocumentCurrencyCode"),
            SupplierName = Text(legal, "RegistrationName"), SupplierNumber = Text(legal, "CompanyID"),
            OrderReference = Text(root.Element(A + "OrderReference"), "ID"),
            OriginalInvoiceReference = Text(root.Element(A + "BillingReference")?.Element(A + "InvoiceDocumentReference"), "ID"),
            DeliveryReference = Text(root.Element(A + "DespatchDocumentReference"), "ID"), DeliveryDate = Date(root.Element(A + "Delivery"), "ActualDeliveryDate"),
            Net = Number(totals, "TaxExclusiveAmount"), Gross = Number(totals, "TaxInclusiveAmount"), Vat = Number(root.Element(A + "TaxTotal"), "TaxAmount"),
            Prepaid = Number(totals, "PrepaidAmount"), Rounding = Number(totals, "PayableRoundingAmount"), Payable = Number(totals, "PayableAmount") };
        if (d.SupplierName.Length == 0) d.SupplierName = Text(supplier?.Element(A + "PartyName"), "Name");
        if (d.SupplierNumber.Length == 0) d.SupplierNumber = Text(supplier?.Element(A + "PartyTaxScheme"), "CompanyID");
        foreach (var line in root.Elements(A + (credit ? "CreditNoteLine" : "InvoiceLine")))
        {
            var item = line.Element(A + "Item"); var tax = item?.Element(A + "ClassifiedTaxCategory"); var price = line.Element(A + "Price");
            if (line.Elements(A + "Item").Count() != 1 || item!.Elements(A + "ClassifiedTaxCategory").Count() != 1) throw new LeasingValidationException("InvoiceUnsupportedXml");
            var adjustments = line.Elements(A + "AllowanceCharge").ToList();
            d.Lines.Add(new() { SourceId = Text(line, "ID"), Description = Text(item, "Name"), ItemNumber = Text(item.Element(A + "SellersItemIdentification"), "ID"),
                Quantity = Number(line, credit ? "CreditedQuantity" : "InvoicedQuantity"), Unit = (string?)line.Element(C + (credit ? "CreditedQuantity" : "InvoicedQuantity"))?.Attribute("unitCode") ?? "",
                Price = Number(price, "PriceAmount"), BaseQuantity = Number(price, "BaseQuantity") ?? 1,
                Allowance = adjustments.Where(x => Text(x, "ChargeIndicator") == "false").Sum(x => Number(x, "Amount") ?? throw new LeasingValidationException("InvoiceUnsupportedXml")),
                Charge = adjustments.Where(x => Text(x, "ChargeIndicator") == "true").Sum(x => Number(x, "Amount") ?? throw new LeasingValidationException("InvoiceUnsupportedXml")),
                Net = Number(line, "LineExtensionAmount"), VatCategory = Text(tax, "ID"), VatPercent = Number(tax, "Percent") ?? (Text(tax, "ID") == "O" ? 0 : null),
                TaxExemptionReason = Text(tax, "TaxExemptionReason"), Reference = Text(line.Element(A + "OrderLineReference"), "LineID") });
            if (adjustments.Any(x => Text(x, "ChargeIndicator") is not ("true" or "false"))) throw new LeasingValidationException("InvoiceUnsupportedXml");
        }
        // UBL CreditNote carries positive credited quantities/amounts. Never negate twice.
        if (d.Net < 0 || d.Lines.Any(x => x.Quantity < 0 || x.Price < 0 || x.Net < 0)) throw new LeasingValidationException("InvoiceUnsupportedSign");
        foreach (var adjustment in root.Elements(A + "AllowanceCharge"))
        {
            var flag = Text(adjustment, "ChargeIndicator"); if (flag is not ("true" or "false")) throw new LeasingValidationException("InvoiceUnsupportedXml");
            var amount = Number(adjustment, "Amount"); var tax = adjustment.Element(A + "TaxCategory");
            d.Lines.Add(new() { SourceId = "document-adjustment-" + d.Lines.Count, Description = Text(adjustment, "AllowanceChargeReason"),
                DocumentAdjustment = true, IsAllowance = flag == "false", Quantity = 1, Price = amount, Net = flag == "false" ? -amount : amount,
                VatCategory = Text(tax, "ID"), VatPercent = Number(tax, "Percent") ?? (Text(tax, "ID") == "O" ? 0 : null) });
            if (d.Lines[^1].Description.Length == 0) d.Lines[^1].Description = flag == "true" ? "Document charge" : "Document allowance";
        }
        foreach (var tax in root.Elements(A + "TaxTotal").Elements(A + "TaxSubtotal"))
        { var category = tax.Element(A + "TaxCategory"); d.Taxes.Add(new() { Category = Text(category, "ID"), Rate = Number(category, "Percent") ?? 0, Taxable = Number(tax, "TaxableAmount") ?? 0, Amount = Number(tax, "TaxAmount") ?? 0 }); }
        if (root.Descendants().Attributes("currencyID").Any(x => x.Value != d.Currency)) throw new LeasingValidationException("InvoiceUnsupportedXml");
        if (d.Lines.Count == 0 || d.Lines.Count > 500 || d.Lines.Any(x => string.IsNullOrWhiteSpace(x.SourceId)) || d.Lines.Select(x => x.SourceId).Distinct().Count() != d.Lines.Count) throw new LeasingValidationException("InvoiceUnsupportedXml");
        return d;
    }
    private static string Text(XElement? e, string name) => e?.Element(C + name)?.Value.Trim() ?? "";
    private static decimal? Number(XElement? e, string name) { var s = Text(e, name); return s.Length == 0 ? null : decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); }
    private static DateOnly? Date(XElement? e, string name) { var s = Text(e, name); return s.Length == 0 ? null : DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture); }
}
