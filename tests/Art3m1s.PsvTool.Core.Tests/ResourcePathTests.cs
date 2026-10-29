using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;
namespace Art3m1s.PsvTool.Core.Tests;
public sealed class ResourcePathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string Put(string path, string text) { string p=Path.Combine(root,path); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p,text); return p; }
    [Fact]
    public async Task KeepsResourceIdentityAndGeometrySeparate()
    {
        string init=Put("system/init.lua", "function storage_path()\n game.size = init.game_size[osno]\n e:setMagicPath{'bg', 'image/'..game.size}\nend");
        string ui=Put("system/ui.lua", "local p='ui/'..game.width..'/manual'\nlocal label='title'..game.width\nlocal x=game.width/2\nlocal native='ui/960/loading'\n-- 'ui/'..game.width\nlocal text=\"'ui/'..game.width\"");
        await ArtemisTextProcessor.RepairResourceDirectoriesAsync(root);
        Assert.Contains("name=\"game.size\"",File.ReadAllText(init));
        string result=File.ReadAllText(ui);
        Assert.Contains("'ui/'..game.size",result);
        Assert.Contains("'title'..game.size",result);
        Assert.Contains("x=game.width/2",result);
        Assert.Contains("'ui/960/loading'",result);
        Assert.Contains("-- 'ui/'..game.width",result);
        Assert.Contains("\"'ui/'..game.width\"",result);
        string before=File.ReadAllText(init);
        await ArtemisTextProcessor.RepairResourceDirectoriesAsync(root);
        Assert.Equal(before,File.ReadAllText(init));
    }
    [Fact]
    public async Task DoesNotInventResourceSizeForUnknownScripts()
    {
        string p=Put("system/ui.lua","local p='ui/'..game.width");
        await ArtemisTextProcessor.RepairResourceDirectoriesAsync(root);
        Assert.Equal("local p='ui/'..game.width",File.ReadAllText(p));
    }
    [Fact]
    public async Task RewritesAsbValuesAndLengthsButNotKeysOrLabels()
    {
        Put("system/init.lua","game.size = init.game_size[osno]\ne:setMagicPath{'bg','image/'..game.size}");
        Put("ui/1280/manual_en.png", "fixture");
        using MemoryStream m=new(); using BinaryWriter w=new(m);
        w.Write("ASB\0"u8); w.Write((byte)0); w.Write(1u); w.Write(0u);
        void S(string s) { byte[] b=Encoding.UTF8.GetBytes(s); w.Write(b.Length); w.Write(b); w.Write((byte)0); }
        S("lyc2"); w.Write(0u); w.Write(1u); S("file"); S("$'ui/'+game.width+'/manual'");
        string p=Put("system/ui.asb", ""); File.WriteAllBytes(p,m.ToArray());
        await ArtemisTextProcessor.RepairResourceDirectoriesAsync(root);
        using BinaryReader r=new(new MemoryStream(File.ReadAllBytes(p)));
        r.BaseStream.Position=13;
        string Read() { int n=r.ReadInt32(); string s=Encoding.UTF8.GetString(r.ReadBytes(n)); Assert.Equal(0,r.ReadByte()); return s; }
        Assert.Equal("lyc2",Read()); r.ReadUInt32(); r.ReadUInt32(); Assert.Equal("file",Read());
        Assert.Equal("$'ui/'+game.size+'/manual**'",Read()); Assert.Equal(r.BaseStream.Length,r.BaseStream.Position);
    }
    [Fact]
    public async Task TruncatedAsbRemainsUnchanged()
    {
        Put("system/init.lua", "game.size=init.game_size[osno]\ne:setMagicPath{'bg','image/'..game.size}");
        string p=Put("system/broken.asb", "");
        byte[] bytes=[65,83,66,0,0,1,0,0,0,0,0,0,0,255,255,255,255];
        File.WriteAllBytes(p,bytes);
        await ArtemisTextProcessor.RepairResourceDirectoriesAsync(root);
        Assert.Equal(bytes,File.ReadAllBytes(p));
    }
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
}
