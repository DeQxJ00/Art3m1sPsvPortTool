using System.Text;
using System.Text.Json;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class ConversionAnalysisTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"conversion-analysis-"+Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(root,"input");
    private string Output => Path.Combine(root,"output");
    private const string Ini="[WINDOWS]\nWIDTH=1280\nHEIGHT=720\nBOOT=system/first.iet\n";
    private const string Init="game.scale=game.width/init.game_scale\ngame.size=init.game_size[osno]\ne:setMagicPath{'bg','image/'..game.size}";
    private const string Helper="function getScale(n) if n then n=math.floor(n*game.scale) end return n end";
    private async Task Pack(string name,params (string Name,string Text)[] files)
    {
        Directory.CreateDirectory(Input);
        string work=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var entries=new List<PfsEntry>();
        foreach(var (path,text) in files)
        {
            string file=Path.Combine(work,path);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file,text,new UTF8Encoding(false));
            entries.Add(new(Encoding.UTF8.GetBytes(path),path,0,0,file));
        }
        await new PfsCodec().PackPf8Async(new('8',entries),Path.Combine(Input,name));
    }
    private async Task<string> Read(string archive,string entry) => Encoding.UTF8.GetString(
        (await new PfsCodec().ReadSmallEntryAsync(Path.Combine(Output,archive),entry))!);
    private Task Convert() => new ConversionService().ConvertAsync(new(Input,Output,Ratio:.75,Categories:AssetCategories.Text));

    [Fact] public async Task SharesLogicalCoordinatesAndDirectoryIdsAcrossArchivesAndLooseFiles()
    {
        await Pack("root.pfs",("system.ini",Ini),("system/init.lua",Init),("system/func.lua",Helper),
            ("system/csv_windows.tbl","init={game_scale=960,game_size={1280,1280,960}}"),
            ("ui/1280/panel.png","image fixture"));
        await Pack("root.pfs.001",("scenario/chapter.ast","ast={{{'CG_XY',x=960,y=440}}}"),
            ("system/ui.lua","local path='ui/'..game.width..'/panel.png'"));
        File.WriteAllText(Path.Combine(Input,"loose.ast"),"ast={{{'BG_XY',x=480,y=270}}}");
        await Convert();
        Assert.Contains("x=960,y=440",await Read("root.pfs.001","scenario/chapter.ast"));
        Assert.Contains("game.size",await Read("root.pfs.001","system/ui.lua"));
        Assert.Contains("x=480,y=270",File.ReadAllText(Path.Combine(Output,"loose.ast")));
        Assert.Contains("game_size={1280,1280,960}",await Read("root.pfs","system/csv_windows.tbl"));
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Output,"art3m1s_conversion.json")));
        Assert.Equal(1280,doc.RootElement.GetProperty("Profile").GetProperty("SourceWidth").GetInt32());
        Assert.Equal(960,doc.RootElement.GetProperty("Profile").GetProperty("TargetWidth").GetInt32());
    }
    [Fact] public async Task FindsCsvCompanionInAnotherArchive()
    {
        await Pack("root.pfs",("system.ini",Ini),("any/layout/item.csv","700:494"));
        await Pack("root.pfs.001",("any/layout/item.png","image fixture"));
        await Convert();
        Assert.Equal("525:370",await Read("root.pfs","any/layout/item.csv"));
    }
    [Fact] public async Task LooseOverrideDoesNotEnableAProfileFromShadowedCode()
    {
        await Pack("root.pfs",("system.ini",Ini),("system/init.lua",Init),("system/func.lua",Helper),
            ("chapter.ast","ast={{{'CG_XY',x=960}}}"));
        Directory.CreateDirectory(Path.Combine(Input,"system"));
        File.WriteAllText(Path.Combine(Input,"system/init.lua"),"-- no runtime scale here");
        await Convert();
        Assert.Contains("x=720",await Read("root.pfs","chapter.ast"));
    }
    [Fact]
    public async Task RepackUsesLooseIetByFullPathInEveryMatchingArchive()
    {
        const string entry = "scenario/scene_007.iet";
        const string sameNameElsewhere = "backup/scene_007.iet";
        const string overrideText = "*entry\n[lyc x=\"400\" y=\"200\"]";
        await Pack("root.pfs", (entry, "*original"), (sameNameElsewhere, "*unrelated"));
        await Pack("root.pfs.001", (entry.Replace('/', '\\'), "*older overlay"));
        string looseDirectory = Path.Combine(Input, "Scenario");
        Directory.CreateDirectory(looseDirectory);
        string loose = Path.Combine(looseDirectory, "scene_007.IET");
        await File.WriteAllTextAsync(loose, overrideText);
        await File.WriteAllTextAsync(Path.Combine(looseDirectory, "extra.iet"), "*loose only");

        await Convert();

        string converted = await File.ReadAllTextAsync(Path.Combine(Output, "Scenario", "scene_007.IET"));
        Assert.Contains("x=\"300\"", converted);
        Assert.Equal(converted, await Read("root.pfs", entry));
        Assert.Equal(converted, await Read("root.pfs.001", entry));
        Assert.Equal("*unrelated", await Read("root.pfs", sameNameElsewhere));
        Assert.Equal("*loose only", await File.ReadAllTextAsync(Path.Combine(Output, "Scenario", "extra.iet")));
        Assert.Null(await new PfsCodec().ReadSmallEntryAsync(Path.Combine(Output, "root.pfs"), "scenario/extra.iet"));
        Assert.Equal(overrideText, await File.ReadAllTextAsync(loose));
    }
    [Fact] public async Task RejectsConvertedArchivesEvenIfLooseManifestWasNotCopied()
    {
        await Pack("root.pfs",("system.ini",Ini),("data.txt","fixture"));await Convert();
        string copied=Path.Combine(root,"copied");Directory.CreateDirectory(copied);
        File.Copy(Path.Combine(Output,"root.pfs"),Path.Combine(copied,"root.pfs"));
        string next=Path.Combine(root,"next");Directory.CreateDirectory(next);File.WriteAllText(Path.Combine(next,"keep"),"original");
        await Assert.ThrowsAsync<InvalidDataException>(()=>new ConversionService().ConvertAsync(
            new(copied,next,Ratio:.75,Categories:AssetCategories.Text,OverwriteExisting:true)));
        Assert.Equal("original",File.ReadAllText(Path.Combine(next,"keep")));
    }
    [Fact] public async Task ReportsMissingAndUnknownReferencesWithoutChangingExpressions()
    {
        await Pack("root.pfs",("system.ini",Ini),("system/first.iet","*top\n[lyc file=\"image/missing.png\"]"),
            ("system/layout.lua","local t={file='image/'..unknown..'.png',left=unknown*2}\n-- file='ignore.png'\nlocal s=\"file='ignore2.png'\""));
        await Convert();
        string report=File.ReadAllText(Path.Combine(Output,"art3m1s_conversion_report.json"));
        Assert.Contains("RESOURCE_MISSING",report);Assert.Contains("RESOURCE_DYNAMIC",report);Assert.Contains("GEOMETRY_PRESERVED",report);
        Assert.DoesNotContain("ignore.png",report);Assert.DoesNotContain("ignore2.png",report);
        Assert.Contains("left=unknown*2",await Read("root.pfs","system/layout.lua"));
    }
    [Fact] public async Task PreservesRuntimeBasesAndReportsUnverifiedPlatformDirectoryIds()
    {
        await Pack("root.pfs",("system.ini",Ini),("init.lua",Init),("func.lua",Helper),
            ("layout.tbl","init={game_scale={960,960},game_size={1280,960}}"),
            ("ui/1280/panel.png","fixture"),
            ("ui.lua","local t={file='ui/'..game.width..'/panel.png'}"));
        await Convert();
        Assert.Contains("game_scale={960,960}",await Read("root.pfs","layout.tbl"));
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Output,"art3m1s_conversion_report.json")));
        var findings=doc.RootElement.GetProperty("Findings").EnumerateArray().ToArray();
        Assert.Contains(findings, f=>f.GetProperty("Code").GetString()=="RESOURCE_DIRECTORY_UNVERIFIED");
        Assert.DoesNotContain(findings, f=>f.GetProperty("Path").GetString()=="ui.lua");
    }
    [Fact] public async Task ReportsTruncatedAsbAndUnknownSourceWithoutChangingInput()
    {
        await Pack("root.pfs",("bad.asb","ASB\0"));
        byte[] original=await File.ReadAllBytesAsync(Path.Combine(Input,"root.pfs"));
        await Convert();
        Assert.Equal("ASB\0",await Read("root.pfs","bad.asb"));
        Assert.Equal(original,await File.ReadAllBytesAsync(Path.Combine(Input,"root.pfs")));
        string report=File.ReadAllText(Path.Combine(Output,"art3m1s_conversion_report.json"));
        Assert.Contains("AUDIT_UNSUPPORTED",report);Assert.Contains("SOURCE_SIZE_UNKNOWN",report);
    }
    [Fact] public async Task PatchIniOverridesBaseResolution()
    {
        await Pack("root.pfs",("system.ini",Ini));
        await Pack("root.pfs.001",("system.ini",Ini.Replace("1280","1920").Replace("720","1080")));
        await Convert();
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Output,"art3m1s_conversion.json")));
        Assert.Equal(1920,doc.RootElement.GetProperty("Profile").GetProperty("SourceWidth").GetInt32());
        Assert.Equal(1440,doc.RootElement.GetProperty("Profile").GetProperty("TargetWidth").GetInt32());
    }
    [Fact] public async Task OldLooseMarkerIsRejectedWithoutChangingOutput()
    {
        await Pack("root.pfs",("system.ini",Ini));
        File.WriteAllText(Path.Combine(Input,"art3m1s_conversion.json"),"{}");
        await Assert.ThrowsAsync<InvalidDataException>(Convert);
        Assert.False(Directory.Exists(Output));
    }
    [Fact] public async Task NumericOverlayOrderControlsResolutionAndHelpers()
    {
        await Pack("root.pfs",("system.ini",Ini),("func.lua",Helper),("chapter.ast","ast={{{'CG_XY',x=960}}}"));
        await Pack("root.pfs.2",("init.lua","-- no scale"),("system.ini",Ini));
        await Pack("root.pfs.10",("init.lua",Init),("system.ini",Ini.Replace("1280","1920").Replace("720","1080")));
        await Convert();
        Assert.Contains("x=960",await Read("root.pfs","chapter.ast"));
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Output,"art3m1s_conversion.json")));
        Assert.Equal(1920,doc.RootElement.GetProperty("Profile").GetProperty("SourceWidth").GetInt32());
    }
    [Fact] public async Task AmbiguousScriptOverlayRequiresResolutionButLooseOverrideResolvesIt()
    {
        await Pack("root.pfs",("system.ini",Ini),("init.lua",Init),("func.lua",Helper));
        await Pack("other.pfs",("init.lua","-- conflicting source"));
        await Assert.ThrowsAsync<InvalidDataException>(Convert);
        Assert.False(Directory.Exists(Output));
        File.WriteAllText(Path.Combine(Input,"init.lua"),Init);
        await Convert();
        Assert.True(File.Exists(Path.Combine(Output,"root.pfs")));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
