using System.Globalization;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record PaymentImportGrid(List<string> Headers, List<List<string>> Rows, bool Excel, bool Date1904, List<string> Sheets);
public sealed class PaymentColumnMap
{
    public int Reference {get;set;}=0;public int PeriodFrom {get;set;}=1;public int PeriodTo {get;set;}=2;public int DueDate {get;set;}=3;
    public int Net {get;set;}=4;public int Vat {get;set;}=5;public int Gross {get;set;}=6;public int Type {get;set;}=7;public int Currency {get;set;}=-1;
}
public sealed record PaymentImportError(int Row, string Key);
public sealed record PaymentImportPreview(List<LeasingPlanTerm> Terms, List<PaymentImportError> Errors);
public interface IPaymentPlanSourceReader { PaymentImportGrid Read(Stream stream, string fileName, string? sheetName = null); }
public static class PaymentPlanImportRules
{
    public const int MaxRows=600;
    public static PaymentImportPreview Preview(PaymentImportGrid grid,PaymentColumnMap map,string currency)
    {
        var errors=new List<PaymentImportError>();var terms=new List<LeasingPlanTerm>();var seen=new HashSet<string>();
        var required=new[]{map.Reference,map.PeriodFrom,map.PeriodTo,map.DueDate,map.Net,map.Vat,map.Gross,map.Type};
        if(required.Any(x=>x<0||x>=grid.Headers.Count)||required.Distinct().Count()!=required.Length||map.Currency < -1||map.Currency>=grid.Headers.Count||map.Currency>=0&&required.Contains(map.Currency))return new([], [new(1,"PaymentImportMapping")]);
        for(int i=0;i<grid.Rows.Count;i++)
        {
            var row=grid.Rows[i];string Cell(int index)=>index>=0&&index<row.Count?row[index].Trim():"";
            try
            {
                var reference=Cell(map.Reference).ToUpperInvariant();if(string.IsNullOrWhiteSpace(reference)||reference.Length>100)throw new LeasingValidationException("PaymentInvalidTerm");
                if(!seen.Add(reference))throw new LeasingValidationException("PaymentDuplicateTerm");
                if(map.Currency>=0&&Cell(map.Currency)!=""&&Cell(map.Currency).ToUpperInvariant()!=currency)throw new LeasingValidationException("PaymentImportCurrency");
                var t=new LeasingPlanTerm{Reference=reference,PeriodFrom=Date(Cell(map.PeriodFrom),grid),PeriodTo=Date(Cell(map.PeriodTo),grid),DueDate=Date(Cell(map.DueDate),grid),Net=Money(Cell(map.Net)),Vat=Money(Cell(map.Vat)),Gross=Money(Cell(map.Gross)),Type=Type(Cell(map.Type))};
                if(t.PeriodTo<t.PeriodFrom||t.Net.HasValue&&t.Vat.HasValue&&t.Gross.HasValue&&t.Net+t.Vat!=t.Gross)throw new LeasingValidationException("PaymentImportTotals");
                terms.Add(t);
            }
            catch(LeasingValidationException ex){errors.Add(new(i+2,ex.Message));}
        }
        return new(terms,errors);
    }
    private static DateOnly Date(string value,PaymentImportGrid grid)
    {
        if(DateOnly.TryParseExact(value,new[]{"dd.MM.yyyy","d.M.yyyy","yyyy-MM-dd","dd/MM/yyyy"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))return date;
        if(grid.Excel&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial)&&serial>=1&&serial<2957000&&serial==Math.Truncate(serial))
            return DateOnly.FromDateTime(DateTime.FromOADate(serial+(grid.Date1904?1462:0)));
        throw new LeasingValidationException("PaymentImportDate");
    }
    private static decimal? Money(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;value=string.Concat(value.Where(c=>!char.IsWhiteSpace(c)));
        if(value.Contains(','))value=value.Replace(".","").Replace(',','.');
        if(!decimal.TryParse(value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount)||amount<0||amount>1000000000000m||decimal.Round(amount,2)!=amount)throw new LeasingValidationException("PaymentImportAmount");return amount;
    }
    private static LeasingPaymentType Type(string value)=>value.Trim().ToLowerInvariant() switch
    {
        "1" or "rent" or "leie" or "ordinær leie" or "hyra"=>LeasingPaymentType.Rent,
        "2" or "advance" or "förskott" or "advancerent" or "forskuddsleie" or "startleie" or "förskottshyra"=>LeasingPaymentType.AdvanceRent,
        "3" or "fee" or "gebyr" or "avgift"=>LeasingPaymentType.Fee,
        "4" or "residual" or "residualobligation" or "restverdiforpliktelse" or "restverdi" or "restvärde"=>LeasingPaymentType.ResidualObligation,
        "5" or "other" or "annet" or "övrigt"=>LeasingPaymentType.Other,
        _=>throw new LeasingValidationException("PaymentImportPostType")
    };
}
