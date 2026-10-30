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
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
}
