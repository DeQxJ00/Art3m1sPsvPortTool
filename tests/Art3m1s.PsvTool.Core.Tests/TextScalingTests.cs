using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class TextScalingTests : IDisposable
{
    [Fact]
    public async Task LegacyCsvLayoutScalesAtlasAndMessageGeometryTogether()
    {
        const string source = "init={mw_path=\"image/main/\",mw_pos=\"0,500,1280,720\",backlog_pos=\"340,80,848,560\",mw_time=250,glyph_size=\"28,28,2,10\"}; csv={ui={button={file=\"atlas\",x=456,y=314,w=368,h=52,cx=\"0\",cy=\"468\",cw=\"368\",ch=\"52\",id=2.4,p1=100}}}";
        string result = await Convert(source, ".lua", .75);
        Assert.Contains("mw_pos=\"0,375,960,540\"", result);
        Assert.Contains("backlog_pos=\"255,60,636,420\"", result);
        Assert.Contains("w=276,h=39,cx=\"0\",cy=\"351\",cw=\"276\",ch=\"39\"", result);
        Assert.Contains("mw_time=250", result);
        Assert.Contains("glyph_size=\"21,21,2,10\"", result);
        Assert.Contains("id=2.4,p1=100", result);
    }

    [Theory]
    [InlineData("init={mw_pos=\"0,500,1280,720\"}; csv={data={w=368,h=52,cw=\"368\"}}")]
    [InlineData("init={game_os=\"vita\",mw_path=\"image/main/\",mw_pos=\"0,500,1280,720\"}; csv={data={w=368,h=52,cw=\"368\"}}")]
    public async Task LegacyLayoutRequiresSchemaAndPreservesNativePlatform(string source)
    {
        Assert.Equal(source, await Convert(source, ".lua", .75));
    }

    [Theory]
    [InlineData("endlua")]
    [InlineData("/lua")]
    public async Task EmbeddedLuaScalesFontTablesAndModeOverrides(string closing)
    {
        const string source = "[lua]\nlocal font={[1]=\"font\",left=316,top=560,width=720,height=160,size=45,rubysize=12}; font.left=336; font.top=628; font.spacemiddle=-4; local name={[1]=\"font\",top=527,size=26}; local other={size=100}; other.top=90; local example=\"[endlua]\"\n[endlua]\n[wait time=1000]";
        string result = await Convert(source.Replace("\n[endlua]", "\n[" + closing + "]"), ".iet");
        Assert.Contains("left=237,top=420,width=540,height=120,size=33,rubysize=9",result);
        Assert.Contains("font.left=252; font.top=471; font.spacemiddle=-3",result);
        Assert.Contains("top=395,size=19",result);
        Assert.Contains("other.top=90",result);
        Assert.Contains("other={size=100}",result);
        Assert.Contains("[wait time=1000]",result);
    }

    private const string PreloadCopy = "if not param[\"prvline\"] then if game.os ~= \"vita\" then e:tag{\"lyc\",id=(\"0.\"..id..\".0\"),file=(p.path..file)} end end lyc2{id=(id..\".0\"),file=(p.path..file),eq=(true)}";

    [Theory]
    [InlineData(1)]
    [InlineData(.5)]
    public async Task HidesOnlyGuardedBackgroundPreloadCopy(double ratio)
    {
        string result = await Convert(PreloadCopy, ".lua", ratio);
        Assert.Contains("e:tag{\"lyprop\",id=(\"0.\"..id..\".0\"),visible=\"0\"}", result);
        Assert.Contains("lyc2{id=(id..\".0\"),file=(p.path..file),eq=(true)}", result);
        Assert.Equal(result, await Convert(result, ".lua", 1));
    }

    [Theory]
    [InlineData("prvline", "other")]
    [InlineData("eq=(true)", "eq=(false)")]
    [InlineData("lyc2", "otherCreate")]
    public async Task LeavesUnrecognizedBackgroundCopiesUnchanged(string from, string to)
    {
        string source = PreloadCopy.Replace(from, to);
        Assert.Equal(source, await Convert(source, ".lua", 1));
    }

    [Fact]
    public async Task BackgroundPreloadMatchingIgnoresCommentsAndQuotedExamples()
    {
        string source = "-- " + PreloadCopy + "\nlocal example=[=[" + PreloadCopy + "]=]";
        Assert.Equal(source, await Convert(source, ".lua", 1));
        string spaced = PreloadCopy.Replace("e:tag", "e -- comment\n : tag").Replace('"', '\'');
        Assert.Contains("visible=\"0\"", await Convert(spaced, ".lua", 1));
    }

    [Theory]
    [InlineData("config={panel_left=100}; function draw(panel_left) e:tag{left=panel_left} end")]
    [InlineData("local panel_left=100; function draw(panel_left) e:tag{left=panel_left} end")]
    [InlineData("function setup() local panel_left=100 end; function draw() e:tag{left=panel_left} end")]
    [InlineData("if ready then local panel_left=100 end; e:tag{left=panel_left}")]
    [InlineData("local panel_left=100; panel_left,other=get_scaled_left(),0; e:tag{left=panel_left}")]
    [InlineData("local panel_left=100; for panel_left=1,10 do e:tag{left=panel_left} end")]
    public async Task LuaPixelAliasDoesNotInferOtherBindings(string source)
    {
        Assert.Equal(source, await Convert(source, ".lua", .5));
    }

    [Fact]
    public async Task LuaPixelAliasesScaleAtUseWithoutChangingSharedLocals()
    {
        const string source = "local log_left=505; local log_width=1920; e:tag{\"font\",left=log_left,width=log_width,size=font_size}; e:tag{\"lyprop\",top=(top-3+80)}";
        string result = await Convert(source, ".lua", .5);
        Assert.Contains("local log_left=505", result);
        Assert.Contains("left=(log_left*0.5)", result);
        Assert.Contains("width=(log_width*0.5)", result);
        Assert.Contains("size=font_size", result);
        Assert.Contains("top=(top-1+40)", result);
    }

    [Fact]
    public async Task ChatLayoutScalesPixelSlotsButPreservesIdsModesAndTiming()
    {
        const string source = "csv={line={base={fade=200,scroll={40,100}},line={pos={80,40},area={400,200,320,480,24},bg={0,\"back\",400,-40},front={5,\"frame\",360,-120},title={6,\"font\",\"font\"},line01pa={\"bubble\",\"icon\",0,0,8,8,\"arrow\",\"top\",-16,4,24},person={1,\"none\",40,-40,\"icon\",-60,-8}}},other={area={400,200,320,480,24}}}";
        string result = await Convert(source, ".tbl");
        Assert.Contains("scroll={30,100}", result);
        Assert.Contains("pos={60,30}", result);
        Assert.Contains("area={300,150,240,360,18}", result);
        Assert.Contains("bg={0,\"back\",300,-30}", result);
        Assert.Contains("front={5,\"frame\",270,-90}", result);
        Assert.Contains("title={6,\"font\",\"font\"}", result);
        Assert.Contains("line01pa={\"bubble\",\"icon\",0,0,6,6,\"arrow\",\"top\",-12,3,18}", result);
        Assert.Contains("person={1,\"none\",30,-30,\"icon\",-45,-6}", result);
        Assert.Contains("other={area={400,200,320,480,24}}", result);
    }
    [Fact]
    public async Task LuaLayoutTablesScaleGeometryWithoutChangingControlValues()
    {
        const string source = "init={device_resolution_w=1920,device_resolution_h=1080,mw_pos=\"0,728\",save_date=\"340,227,331,100,42,ffffff,000000,000000,right,0\",mw_time=250,select_pos=\"338,171,1261,169,160,select\"};csv={fg={unit={pose={near={x=0,y=30,fx=286,fy=178}}}},fgpos={center={35,-412,20,0}},bg={room={lx=-960,ly=-540}},ui_title={button={x=10,y=20,w=317,h=51}},config_text={label={70,155,240,70,\"other\",\"text\"}}}";
        string result = await Convert(source, ".lua", .5);
        Assert.Contains("device_resolution_w=960,device_resolution_h=540", result);
        Assert.Contains("mw_pos=\"0,364\"", result);
        Assert.Contains("save_date=\"170,113,165,50,21,ffffff,000000,000000,right,0\"", result);
        Assert.Contains("mw_time=250", result);
        Assert.Contains("select_pos=\"169,85,630,84,80,select\"", result);
        Assert.Contains("fx=143,fy=89", result);
        Assert.Contains("center={35,-206,10,0}", result);
        Assert.Contains("lx=-480,ly=-270", result);
        Assert.Contains("w=158,h=25", result);
        Assert.Contains("label={35,77,120,35,\"other\",\"text\"}", result);
    }

    [Fact]
    public async Task LuaFontTableScalesSizesButPreservesNativeVariantAndColors()
    {
        string result=await Convert("fontsize_table={{small=16,medium=40,name=30,sample=24}};fontsize_table_psv={{medium=20}};local font={[1]=\"font\",size=40,rubysize=14,color=16777215,speed=30}", ".lua", .5);
        Assert.Contains("small=8,medium=20,name=15,sample=12",result);
        Assert.Contains("fontsize_table_psv={{medium=20}}",result);
        Assert.Contains("size=20,rubysize=7,color=16777215,speed=30",result);
    }

    [Fact]
    public async Task NativeLuaLayoutIsNotRescaled()
    {
        const string source="init={game_os=\"vita\",device_resolution_w=960};csv={ui={button={x=80,y=20,w=100,h=30}}}";
        Assert.Equal(source,await Convert(source,".lua",.5));
    }

    [Fact]
    public async Task LuaLongStringCoordinatesPreserveTextAndTiming()
    {
        const string source="tags={{[1]=\"camera\",x=[[-80]],y=[=[-30]=],zoom=[[130]]},{[1]=\"ev\",locate=[[-100,140,200,-80]],speed=[[30000]]}};text=[[x=80, y=30]]";
        string result=await Convert(source,".lua",.5);
        Assert.Contains("x=[[-40]],y=[=[-15]=],zoom=[[130]]",result);
        Assert.Contains("locate=[[-50,70,100,-40]],speed=[[30000]]",result);
        Assert.Contains("text=[[x=80, y=30]]",result);
    }

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
    public async Task IetTitleAnimationScalesGeometryWithoutChangingTimingOrComments()
    {
        const string source = "// [tween x=\"1280,956\"]\r\n;[lyprop anchorx=640]\r\n"
            + "[lyprop id=\"menu\" anchorx=640 visible=1]\r\n"
            + "[tween id=\"menu.2\" x=\"1280,956\" y='-40,20' time=\"1000\" delay=\"200\" zoom=\"200,100\"]\r\n"
            + "[tween id=\"menu.3\" x=\"1280,982\" time=\"1000\" delay=\"400\"]\r\n"
            + "[wait time=1000]\r\nDialogue x=1280 [tween x=1280]\r\n"
            + "[custom x=1280]\r\n[lua]\r\n[tween x=1280]\r\n[endlua]\r\n";
        string expected = source.Replace("anchorx=640 visible", "anchorx=480 visible")
            .Replace("x=\"1280,956\" y='-40,20'", "x=\"960,717\" y='-30,15'")
            .Replace("x=\"1280,982\" time", "x=\"960,736\" time");
        Assert.Equal(expected, await Convert(source, ".iet"));
        Assert.Equal("[tween x=1280/2 y=top]\n[lyc width=1 height=1]",
            await Convert("[tween x=1280/2 y=top]\n[lyc width=1 height=1]", ".iet"));
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
        byte[] before = [.. cp932.GetBytes("local t={x=1280, text=\"テスト前半"), 0x81, 0x2F,
            .. cp932.GetBytes("テスト後半\", y=720}\r\n")];
        await File.WriteAllBytesAsync(path, before);

        await new ArtemisTextProcessor().ProcessAsync(path, .5);

        byte[] after = await File.ReadAllBytesAsync(path);
        byte[] expected = [.. cp932.GetBytes("local t={x=640, text=\"テスト前半"), 0x81, 0x2F,
            .. cp932.GetBytes("テスト後半\", y=360}\r\n")];
        Assert.Equal(expected, after);
    }

    [Fact]
    public async Task Case004MalformedAstCanBeConvertedWhenArchiveIsAvailable()
    {
        string? archive = Environment.GetEnvironmentVariable("ART3M1S_CASE004_PFS");
        string? entry = Environment.GetEnvironmentVariable("ART3M1S_CASE004_ENTRY");
        if (string.IsNullOrWhiteSpace(archive) || string.IsNullOrWhiteSpace(entry) || !File.Exists(archive)) return;
        byte[]? original = await new PfsCodec().ReadSmallEntryAsync(archive, entry);
        Assert.NotNull(original);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "case004.ast");
        await File.WriteAllBytesAsync(path, original);

        await new ArtemisTextProcessor().ProcessAsync(path, .5);

        byte[] converted = await File.ReadAllBytesAsync(path);
        Assert.True(converted.AsSpan().IndexOf(new byte[] { 0x81, 0x2F }) >= 0);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
