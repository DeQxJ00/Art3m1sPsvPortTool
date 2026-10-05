using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class TextScalingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "text-scaling-" + Guid.NewGuid().ToString("N"));
    private async Task<string> Convert(string text, string extension = ".lua", double ratio = .75)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "fixture" + extension);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
        await new ArtemisTextProcessor().ProcessAsync(path, ratio);
        return await File.ReadAllTextAsync(path);
    }

    [Theory]
    [InlineData("local x = math.floor((sw - game.width) / 2) sx = sx + x")]
    [InlineData("local lyax = math.floor(e:var(\"t.ly.width\") / 2)")]
    [InlineData("local h = e:var(\"t.ly.height\") / 2")]
    [InlineData("if y > 0 then y = math.floor(y / 2) end")]
    [InlineData("if x == 1 then force() elseif x == -1 then pending() end")]
    [InlineData("extra_csfgmove_add(\"x\", -1); extra_csfgmove_add(\"y\", 1)")]
    [InlineData("x = x * -1; m.x = m.x + 1; m.y = m.y + 1")]
    [InlineData("local fx = math.floor((x - m.x) / 10)")]
    [InlineData("local sw = ex.tostring(1 / game.width / 10)")]
    [InlineData("easeout_quad = function() return 1 - (1 - x) * (1 - x) end,")]
    [InlineData("easeinout_quad = function() return x < 0.5 and 2*x*x or 1-math.pow(-2*x+2, 2)/2 end,")]
    [InlineData("return x < 0.5 and (math.pow(2*x,2)*((c2+1)*2*x-c2))/2 or (math.pow(2*x-2,2)*((c2+1)*(x*2-2)+c2)+2)/2")]
    [InlineData("systween{ id=idx, y=(\"0,\"..(y*2)), time=20000, yoyo=-1 }")]
    [InlineData("tag{ param=\"left\", time=0, from=(x-1), to=x }")]
    public async Task PreservesDimensionlessExpressionsAndControlValues(string original) =>
        Assert.Equal(original, await Convert(original));

    [Theory]
    [InlineData(".lua")]
    [InlineData(".ast")]
    [InlineData(".tbl")]
    [InlineData(".ipt")]
    public async Task LeavesCommentsAndDialogueUntouched(string extension)
    {
        const string source = "-- x=1280 mulpos(80)\r\n--[=[ width=1920\n x=50 ]=]\n"
            + "local prose = \"by 1 PM; my 46 degrees; x=1280; mulpos(80)\"\n"
            + "local long = [=[ width=1920 x=30 ]=]\n"
            + "{\"by 1 PM\", \"my 46 degrees\", \"escaped \\\" x=1920\"}\n";
        Assert.Equal(source, await Convert(source, extension));
    }

    [Fact]
    public async Task ScalesCompleteCoordinateSequencesAndEveryField()
    {
        Assert.Equal("tween{x=\"960,690\", y='-30,15', time=200, alpha=255}; tag{width=12,height=3}; tag{width=6}\r\n",
            await Convert("tween{x=\"1280,920\", y='-40,20', time=200, alpha=255}; tag{width=16,height=4}; tag{width=8}\r\n"));
        Assert.Equal("{[\"x\"]=480, [\"y\"]=-30, x2=960, id=1, ratio=.5}",
            await Convert("{[\"x\"]=640, [\"y\"]=-40, x2=1280, id=1, ratio=.5}", ".ast"));
        Assert.Equal("tag{x=(480),y=((-30)),width=('960')}", await Convert("tag{x=(640),y=((-40)),width=('1280')}"));
    }

    [Fact]
    public async Task PreservesPositiveDimensionsAndOffscreenPixels()
    {
        Assert.Equal("lyc2{width=\"1\",height=1,left=-1,top='-1',x=0}",
            await Convert("lyc2{width=\"1\",height=1,left=-1,top='-1',x=0}"));
        Assert.Equal("{w=1,h=1,clip='0,0,1,1',clip_a='0,1,1,1'}",
            await Convert("{w=1,h=1,clip='0,0,1,1',clip_a='0,2,1,1'}", ".tbl"));
        Assert.Equal("tween{x='1,0',y='-1,0',time=0}", await Convert("tween{x='1,0',y='-1,0',time=0}"));
    }

    [Fact]
    public async Task VitaCoordinatesSurviveNestedBranchesWithoutProtectingOtherPlatforms()
    {
        const string source = "if init.crop and game.os == 'vita' then\n"
            + " if mode then tag{width=960,height=4,y=540} else tag{width=960,height=2,y=542} end\n"
            + "elseif game.os == 'windows' then tag{width=1280,height=720} end\n"
            + "tag{width=1280,height=720}";
        string expected = source.Replace("width=1280,height=720", "width=960,height=540", StringComparison.Ordinal);
        Assert.Equal(expected, await Convert(source));
    }

    [Fact]
    public async Task MulposScalesOnceEvenInsideAFieldAndPreservesDynamicExpressions()
    {
        Assert.Equal("tag{x=mulpos(60),y=mulpos(-30)}; local y=mulpos(60); local x=mulpos(v[2]); local x=1",
            await Convert("tag{x=mulpos(80),y=mulpos(-40)}; local y=mulpos(80); local x=mulpos(v[2]); local x=1"));
    }

    [Fact]
    public async Task IptScalesOnlyGeometryAndRectangles()
    {
        Assert.Equal("ipt={base={w=960,h=540,ax=480,ay=270,time=100,loop=-1}, ['one']='0,0,96,54', name='123', note='my 46'}",
            await Convert("ipt={base={w=1280,h=720,ax=640,ay=360,time=100,loop=-1}, ['one']='0,0,128,72', name='123', note='my 46'}", ".ipt"));
    }

    [Fact]
    public async Task NumericExpressionsAreNotPartiallyRewritten()
    {
        const string original = "tag{width=1280/2,height=720*2,x=0x40,y=2^3}; local x=1; local y=-1";
        Assert.Equal(original, await Convert(original));
        Assert.Equal("tag{width=960,height=540,x=-7}", await Convert("tag{width=1.28e3,height=720.0,x=-10.5}"));
    }

    [Fact]
    public async Task ReportsPreservedExpressionsWithOriginalLineNumbers()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "diagnostic.lua");
        const string source = "-- header\r\ntag{width=1280/2}\r\ntag{x=left,y=top}\r\ntag{width=1280}";
        await File.WriteAllTextAsync(path, source);
        IReadOnlyList<int> lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .75);
        Assert.Equal(new[] { 2, 3 }, lines);
        Assert.Equal(source.Replace("width=1280}", "width=960}", StringComparison.Ordinal), await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ScalesMalformedCp932ScriptWithoutChangingOriginalDialogueBytes()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "malformed.ast");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp932 = Encoding.GetEncoding(932);
        byte[] before = [.. cp932.GetBytes("local t={x=1280, text=\"早いところ"), 0x81, 0x2F,
            .. cp932.GetBytes("忘れなければ\", y=720}\r\n")];
        await File.WriteAllBytesAsync(path, before);

        await new ArtemisTextProcessor().ProcessAsync(path, .5);

        byte[] after = await File.ReadAllBytesAsync(path);
        byte[] expected = [.. cp932.GetBytes("local t={x=640, text=\"早いところ"), 0x81, 0x2F,
            .. cp932.GetBytes("忘れなければ\", y=360}\r\n")];
        Assert.Equal(expected, after);
    }

    [Fact]
    public async Task NukitashiMalformedAstCanBeConvertedWhenArchiveIsAvailable()
    {
        string? archive = Environment.GetEnvironmentVariable("ART3M1S_NUKITASHI_PFS");
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive)) return;
        byte[]? original = await new PfsCodec().ReadSmallEntryAsync(archive, "script/05_fk_17h.ast");
        Assert.NotNull(original);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "05_fk_17h.ast");
        await File.WriteAllBytesAsync(path, original);

        await new ArtemisTextProcessor().ProcessAsync(path, .5);

        byte[] converted = await File.ReadAllBytesAsync(path);
        Assert.True(converted.AsSpan().IndexOf(new byte[] { 0x81, 0x2F }) >= 0);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
