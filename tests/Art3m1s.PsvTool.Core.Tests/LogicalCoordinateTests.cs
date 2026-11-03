using Art3m1s.PsvTool.Core;
using Xunit;
namespace Art3m1s.PsvTool.Core.Tests;
public sealed class LogicalCoordinateTests : IDisposable {
    private readonly string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
    private string Put(string name,string text) { Directory.CreateDirectory(root); string p=Path.Combine(root,name);File.WriteAllText(p,text);return p; }
    [Fact] public async Task RuntimeScaledCommandsKeepLogicalCentersAndMovement() {
        Put("init.lua","game.scale = game.width / init.game_scale");
        Put("func.lua","function getScale(n) if n then n = math.floor(n * game.scale) end return n end");
        bool profile=await ArtemisTextProcessor.DetectRuntimeScaledCoordinatesAsync(root);
        Assert.True(profile);
        string ast=Put("chapter.ast","ast={{{'BG_XY',x=480,y=450},{'ST',x=720,y=0},{'CG_XY',x=480,y=270},{'bg',x=1280,y=720},{'CUSTOM',x=100},text=[[x=1280]]}}");
        await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(ast,.75,default,profile);
        string output=File.ReadAllText(ast);
        Assert.Contains("'BG_XY',x=480,y=450",output);
        Assert.Contains("'ST',x=720,y=0",output);
        Assert.Contains("'CG_XY',x=480,y=270",output);
        Assert.Contains("'bg',x=960,y=540",output);
        Assert.Contains("'CUSTOM',x=75",output);
        Assert.Contains("text=[[x=1280]]",output);
    }
    [Fact] public async Task CommentsAndStringsDoNotEnableLogicalCoordinateProfile() {
        Put("fake.lua","-- game.scale = game.width / init.game_scale\nlocal note='function getScale(n) n = math.floor(n * game.scale) end'");
        Assert.False(await ArtemisTextProcessor.DetectRuntimeScaledCoordinatesAsync(root));
        string ast=Put("chapter.ast","ast={{{'BG_XY',x=480,y=450}}}");
        await new ArtemisTextProcessor().ProcessAsync(ast,.75);
        Assert.Contains("x=360,y=337",File.ReadAllText(ast));
    }
    [Fact] public async Task LayoutTableOffsetsAndAnchorsFollowImagePixels() {
        string tbl=Put("layout.tbl","csv={cgsize={sample={wx=-44,wy=-40,ax=684,ay=400}},fgsize={sample={mwx=-120,mwy=-60}}}");
        await new ArtemisTextProcessor().ProcessAsync(tbl,.75);
        string output=File.ReadAllText(tbl);
        Assert.Contains("wx=-33,wy=-30,ax=513,ay=300",output);
        Assert.Contains("mwx=-90,mwy=-45",output);
    }
    [Theory]
    [InlineData(.75)]
    [InlineData(.5)]
    public async Task InlineMovementScaleUsesTheSameRuntimeHelperAsItsStart(double ratio) {
        string source = "function cg_moveXY(p, name)\n"
            + "local x1 = getScale((scr.img.bg[no] and scr.img.bg[no].x or 480) - 480)\n"
            + "local x2 = ((v.x or 480) - 480) * (1280/960)\n"
            + "local y2 = getScale((v.y or 270) - 270)\n"
            + "local unrelated = 1280/960\nend\n"
            + "-- local x2 = ((v.x or 480) - 480) * (1280/960)\n";
        string path=Put("motion.lua",source);
        await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path,ratio,default,true);
        Assert.Equal(source.Replace("local x2 = ((v.x or 480) - 480) * (1280/960)\nlocal y2",
            "local x2 = getScale((v.x or 480) - 480)\nlocal y2",StringComparison.Ordinal),File.ReadAllText(path));
    }
    [Theory]
    [InlineData(false, .75, "cg_moveXY")]
    [InlineData(true, 1, "cg_moveXY")]
    [InlineData(true, .75, "unrelated")]
    public async Task InlineScaleRequiresMovementProfile(bool profile,double ratio,string function) {
        string source=$"function {function}(p, name)\nlocal x1=getScale(0)\nlocal x2=((v.x or 480)-480)*(1280/960)\nend";
        string path=Put("motion.lua",source);
        await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path,ratio,default,profile);
        Assert.Equal(source,File.ReadAllText(path));
    }
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
}
