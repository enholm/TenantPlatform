using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.Agreements;

static class AutomaticIndexChecks
{
    public static void Run()
    {
        var line = Guid.NewGuid(); var index = Guid.NewGuid();
        var versions = new List<AgreementLineVersion> { Version(1, new(2026,1,1), true) };
        var prices = new[] { new AgreementPriceVersion { Id=Guid.NewGuid(), LineId=line, Sequence=1, EffectiveFrom=new(2026,1,1), UnitPrice=1000, Quantity=1 } };
        var selections = new[] { new AgreementIndexSelection { IndexId=index, Sequence=1, EffectiveFrom=new(2026,1,1) } };
        var indices = new[] { new AgreementIndex { Id=index, Name="Annual index" } };
        var levels = new List<AgreementIndexValue> { Level(2026,101), Level(2027,105) };
        List<AgreementAutomaticPrice> Project()=>AgreementAutomaticIndexCalculator.Project(false,versions,prices,selections,indices,levels)[line];
        Check(Project().Last().UnitPrice==1039.60m,"missing future levels carry the latest known price");
        levels.Add(Level(2028,99));
        Check(Project().Last().UnitPrice==980.19m,"automatic pricing includes full index decreases");
        versions.Add(Version(2,new(2027,7,1),false));
        Check(Project().Last().UnitPrice==1039.60m,"turning regulation off freezes the calculated price");
        versions.Add(Version(3,new(2028,7,1),true));levels.Add(Level(2029,110));
        Check(Project().Last().UnitPrice==1155.11m,"re-enabling skips index changes while the line was disabled");
        Check(AgreementAutomaticIndexCalculator.Project(true,versions,prices,selections,indices,levels).Count==0,"unresolved legacy configuration keeps registered prices");
        AgreementLineVersion Version(int sequence, DateOnly date, bool enabled)=>new(){Id=Guid.NewGuid(),LineId=line,Sequence=sequence,
            EffectiveFrom=date,StartDate=new(2026,1,1),Status=AgreementLineStatus.Active,IndexRegulated=enabled};
        AgreementIndexValue Level(int year,decimal value)=>new(){Id=Guid.NewGuid(),PeriodKey=Guid.NewGuid(),IndexId=index,Period=new(year,1,1),Revision=1,Value=value};
    }
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
}
