using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Localization;
namespace TenantPlatform.Web.Services.Leasing;
public static class LeasingReportExport
{
    public static readonly string[] Columns=["Name","Reference","Finance","Owner","Currency","Status","Basis","PurchaseDate","EndDate","NoticeDate","ReturnDate","From","To","DueDate","InvoiceDate","EventDate","Net","Vat","Gross","Limit","Used","Reserved","Available","Expected","Invoiced","Difference","Unallocated","Version","NeedsReview","Dimensions","Serial","Location","Notes","OriginalEndDate","ClosedDate","ClosureKind","Quantity","BuyoutAmount","UnitReference"];
    public static object? Value(LeasingReportRow row,string column)=>typeof(LeasingReportRow).GetProperty(column)!.GetValue(row);
    public static string Text(object? value)=>value switch{null=>"",DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),decimal n=>n.ToString(CultureInfo.InvariantCulture),_=>Convert.ToString(value,CultureInfo.InvariantCulture)??""};
    public static string SafeText(string text)=>text.FirstOrDefault(c=>!char.IsWhiteSpace(c)&&!char.IsControl(c)) is '=' or '+' or '-' or '@'?"'"+text:text;
    public static byte[] Create(LeasingReportResult report,LeasingReportFilter filter,bool xlsx,IStringLocalizer<TenantPlatformResources> l)
    {
        var headers=Columns.Select(c=>(object?)l["LifeColumn"+c].Value).ToArray();
        object?[] Values(LeasingReportRow r)=>Columns.Select(c=>{var v=Value(r,c);return v is string s&&s.StartsWithAny("Life","Payment","Leasing","Rental")?l[s].Value:v;}).ToArray();
        var meta=new List<object?[]>{new object?[]{l["LifeGenerated"].Value,report.GeneratedUtc.ToString("O")},new object?[]{l["LifeFilters"].Value,JsonSerializer.Serialize(filter)},new object?[]{l["LifeReport"+report.Kind].Value,l["LifeReportLimitations"].Value}};
        if(!xlsx){var b=new StringBuilder("\uFEFF");void Row(IEnumerable<object?> values)=>b.AppendLine(string.Join(';',values.Select(x=>"\""+(x is string s?SafeText(s):Text(x)).Replace("\"","\"\"")+"\"")));foreach(var m in meta)Row(m);Row(headers);foreach(var r in report.Rows)Row(Values(r));return Encoding.UTF8.GetBytes(b.ToString());}
        using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string text){using var w=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));w.Write(text);}
            Entry("[Content_Types].xml","""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Entry("_rels/.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/workbook.xml","""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Data" sheetId="1" r:id="r1"/><sheet name="Metadata" sheetId="2" r:id="r2"/></sheets></workbook>""");
            Entry("xl/_rels/workbook.xml.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="r2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="r3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Entry("xl/styles.xml","""<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></numFmts><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");
            void Sheet(string path,IEnumerable<object?[]> rows){using var output=zip.CreateEntry(path).Open();using var x=XmlWriter.Create(output,new(){Encoding=new UTF8Encoding(false)});const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";x.WriteStartElement("worksheet",ns);x.WriteStartElement("sheetData",ns);int rn=0;foreach(var row in rows){rn++;x.WriteStartElement("row",ns);x.WriteAttributeString("r",rn.ToString());for(int col=0;col<row.Length;col++){var value=row[col];if(value==null)continue;x.WriteStartElement("c",ns);x.WriteAttributeString("r",Column(col)+rn);if(value is DateOnly d){x.WriteAttributeString("s","1");x.WriteElementString("v",ns,d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture));}else if(value is decimal or int){x.WriteElementString("v",ns,Text(value));}else if(value is bool b){x.WriteAttributeString("t","b");x.WriteElementString("v",ns,b?"1":"0");}else{x.WriteAttributeString("t","inlineStr");x.WriteStartElement("is",ns);x.WriteStartElement("t",ns);x.WriteAttributeString("xml","space",null,"preserve");x.WriteString(new string(Text(value).Where(c=>XmlConvert.IsXmlChar(c)).Take(32767).ToArray()));x.WriteEndElement();x.WriteEndElement();}x.WriteEndElement();}x.WriteEndElement();}x.WriteEndElement();x.WriteEndElement();}
            Sheet("xl/worksheets/sheet1.xml",new[]{headers}.Concat(report.Rows.Select(Values)));Sheet("xl/worksheets/sheet2.xml",meta);
        }
        return stream.ToArray();
    }
    private static string Column(int i){var s="";for(i++;i>0;i=(i-1)/26)s=(char)('A'+(i-1)%26)+s;return s;}
    private static bool StartsWithAny(this string value,params string[] starts)=>starts.Any(value.StartsWith);
}
