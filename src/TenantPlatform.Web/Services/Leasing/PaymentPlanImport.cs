using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;
public sealed record PaymentImportGrid(List<string> Headers, List<List<string>> Rows, bool Excel, bool Date1904, List<string> Sheets);
public sealed class PaymentColumnMap
{
    public int Reference {get;set;}=0;public int PeriodFrom {get;set;}=1;public int PeriodTo {get;set;}=2;public int DueDate {get;set;}=3;
    public int Net {get;set;}=4;public int Vat {get;set;}=5;public int Gross {get;set;}=6;public int Type {get;set;}=7;public int Currency {get;set;}=-1;
}
public sealed record PaymentImportError(int Row, string Key);
public sealed record PaymentImportPreview(List<LeasingPlanTerm> Terms, List<PaymentImportError> Errors);
public static class PaymentPlanImport
{
    public const int MaxRows=600;
    public static PaymentImportGrid Read(Stream stream,string fileName,string? sheetName=null)
    {
        var extension=Path.GetExtension(fileName).ToLowerInvariant();
        if(extension==".csv")return Csv(stream);
        if(extension!=".xlsx")throw new LeasingValidationException("PaymentImportType");
        try{return Xlsx(stream,sheetName);}catch(Exception ex)when(ex is InvalidDataException or XmlException or FormatException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException){throw new LeasingValidationException("PaymentImportInvalid");}
    }
    private static PaymentImportGrid Csv(Stream stream)
    {
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true,4096,leaveOpen:true);var text=reader.ReadToEnd();
        if(text.Length>10_000_000||text.Contains('\0'))throw new LeasingValidationException("PaymentImportInvalid");
        var first=text.Split('\n')[0];var delimiter=first.Contains(';')?";":first.Contains('\t')?"\t":",";
        using var parser=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=true};parser.SetDelimiters(delimiter);
        var rows=new List<List<string>>();
        try{while(!parser.EndOfData){var row=parser.ReadFields()?.ToList()??[];if(row.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(row);if(rows.Count>MaxRows+1||row.Count>100)throw new LeasingValidationException("PaymentImportSize");}}
        catch(MalformedLineException){throw new LeasingValidationException("PaymentImportInvalid");}
        if(rows.Count<2)throw new LeasingValidationException("PaymentImportEmpty");
        return new(rows[0],rows.Skip(1).ToList(),false,false,[]);
    }
    private static XDocument Xml(ZipArchive zip,string name)
    {
        var entry=zip.GetEntry(name)??throw new LeasingValidationException("PaymentImportInvalid");
        using var stream=entry.Open();using var xml=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20_000_000});return XDocument.Load(xml);
    }
    private static PaymentImportGrid Xlsx(Stream stream,string? sheetName)
    {
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read,leaveOpen:true);
        if(zip.Entries.Count>10000||zip.Entries.Sum(x=>x.Length)>200L*1024*1024||zip.Entries.Any(x=>x.FullName.EndsWith("vbaProject.bin",StringComparison.OrdinalIgnoreCase)))throw new LeasingValidationException("PaymentImportInvalid");
        XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var book=Xml(zip,"xl/workbook.xml");var sheets=book.Descendants(n+"sheet").ToList();var sheet=sheetName==null?sheets.FirstOrDefault():sheets.SingleOrDefault(x=>(string?)x.Attribute("name")==sheetName);
        if(sheet==null)throw new LeasingValidationException("PaymentImportEmpty");
        var rid=(string?)sheet.Attribute(rel+"id");var relationship=Xml(zip,"xl/_rels/workbook.xml.rels").Root!.Elements().SingleOrDefault(x=>(string?)x.Attribute("Id")==rid);
        if(relationship==null||(string?)relationship.Attribute("TargetMode")=="External")throw new LeasingValidationException("PaymentImportInvalid");
        var target=(string?)relationship.Attribute("Target")??"";
        if(target.Contains("..")||target.Contains('\\')||target.Contains(':'))throw new LeasingValidationException("PaymentImportInvalid");
        var path=target.StartsWith('/')?target.TrimStart('/'):"xl/"+target;
        var strings=zip.GetEntry("xl/sharedStrings.xml")==null?[]:Xml(zip,"xl/sharedStrings.xml").Descendants(n+"si").Select(x=>string.Concat(x.Descendants(n+"t").Select(t=>t.Value))).ToList();
        var data=Xml(zip,path);var rows=new List<List<string>>();
        foreach(var row in data.Descendants(n+"row"))
        {
            var values=new List<string>();
            foreach(var c in row.Elements(n+"c"))
            {
                if(c.Element(n+"f")!=null)throw new LeasingValidationException("PaymentImportFormula");
                var address=(string?)c.Attribute("r")??"";int column=0;foreach(var ch in address.TakeWhile(char.IsLetter))column=checked(column*26+char.ToUpperInvariant(ch)-'A'+1);
                if(column is <1 or >100)throw new LeasingValidationException("PaymentImportSize");while(values.Count<column)values.Add("");
                var type=(string?)c.Attribute("t");var value=c.Element(n+"v")?.Value??"";
                values[column-1]=type=="s"?strings[int.Parse(value,CultureInfo.InvariantCulture)]:type=="inlineStr"?string.Concat(c.Descendants(n+"t").Select(x=>x.Value)):type=="e"?throw new LeasingValidationException("PaymentImportInvalid"):value;
            }
            if(values.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(values);if(rows.Count>MaxRows+1)throw new LeasingValidationException("PaymentImportSize");
        }
        if(rows.Count<2)throw new LeasingValidationException("PaymentImportEmpty");
        return new(rows[0],rows.Skip(1).ToList(),true,(string?)book.Root?.Element(n+"workbookPr")?.Attribute("date1904") is "1" or "true",sheets.Select(x=>(string?)x.Attribute("name")??"").ToList());
    }
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
