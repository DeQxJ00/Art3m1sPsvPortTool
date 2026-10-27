using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class CsvScalingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "csv-scaling-" + Guid.NewGuid().ToString("N"));

    private async Task<string> Convert(string relative, string text, bool bom = false)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(bom));
        await new ArtemisTextProcessor().ProcessAsync(path, .75);
        if (bom) Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        return await File.ReadAllTextAsync(path);
    }

    [Theory]
    [InlineData("_data/fgimage/parts/pose/emo/8.csv", "720,130", "540,97")]
    [InlineData("_data/bgimage/overscan.csv", "-50,-50\r\n", "-37,-37\r\n")]
    [InlineData("_data/image/menu/save/slotnum_pos.csv", "24,24,-26", "18,18,-19")]
    [InlineData("_data/image/menu/vsave/thumb_face.csv", "10,11,50", "7,8,50")]
    [InlineData("_data/image/gui/game/glyph/adv/default/pos.csv", "1017,642,100,0", "762,481,100,0")]
    [InlineData("_data/image/title/version/pos.csv", "1230,683,right,0", "922,512,right,0")]
    [InlineData("_data/image/menu/save/slot_margin.csv", " 28,18 \n", " 21,13 \n")]
    public async Task ScalesOnlyPixelColumns(string path, string before, string after)
    {
        string companion = Path.ChangeExtension(Path.Combine(root, path), ".lua");
        Directory.CreateDirectory(Path.GetDirectoryName(companion)!);
        await File.WriteAllTextAsync(companion, "-- companion");
        Assert.Equal(after, await Convert(path, before));
    }

    [Theory]
    [InlineData("_data/image/menu/save/slot_matrix.csv", "3,4")]
    [InlineData("_data/image/menu/save/fonttext/slot_message_textsize.csv", "47")]
    [InlineData("_data/image/menu/backlog/adv_slotnum.csv", "3")]
    [InlineData("_data/image/menu/vsave/thumb_face_using.csv", "1")]
    [InlineData("_data/image/extra/viewer/pos_num_drawing_time.csv", "2000")]
    [InlineData("_data/image/extra/music/soundlist.csv", "track01,track02")]
    [InlineData("data/stats.csv", "720,130")]
    [InlineData("_data/fgimage/parts/pose/emo/8.csv", "720,130")]
    [InlineData("_data/bgimage/overscan.csv", "-50,-50")]
    [InlineData("_data/image/custom.csv", "720,130")]
    [InlineData("_data/fgimage/offset.csv", "720,130\n40,50")]
    public async Task PreservesUnrelatedData(string path, string source) =>
        Assert.Equal(source, await Convert(path, source));

    [Theory]
    [InlineData("slot_pos.csv", "40,80", "30,60")]
    [InlineData("cn_btn_next_0.csv", "400,80", "300,60")]
    [InlineData("sysbtn_auto_ng .csv", "400,80", "300,60")]
    [InlineData("select_align.csv", "480,76", "360,57")]
    [InlineData("size.csv", "1600,900", "1200,675")]
    [InlineData("slotnum_pos.csv", "540,16,center,4", "405,12,center,3")]
    [InlineData("thumb_face.csv", "260,-12,58", "195,-9,58")]
    [InlineData("adv_face_.csv", "-24,-8,70", "-18,-6,70")]
    [InlineData("pos.csv", "10,12,150,1", "7,9,150,1")]
    [InlineData("pos.csv", "1756,1040,left,4", "1317,780,left,3")]
    [InlineData("pos_num_arrign.csv", "left,-4", "left,-3")]
    [InlineData("base_2_move.csv", "32--,++0\r\n", "24--,++0\r\n")]
    [InlineData("base_3_move.csv", "--32,16++", "--24,12++")]
    [InlineData("btnmove.csv", "left,300,easein_quad,200,easeout_cubic", "left,225,easein_quad,150,easeout_cubic")]
    public async Task IndependentLayoutSchemasScaleOnlyPixels(string name, string source, string expected) =>
        Assert.Equal(expected, await Convert("arbitrary/" + name, source));

    [Fact]
    public async Task ColonSidecarRequiresCompanionAndPreservesSeparator()
    {
        Assert.Equal("700:494\r\n", await Convert("tap.csv", "700:494\r\n"));
        await File.WriteAllBytesAsync(Path.Combine(root, "tap.png"), []);
        Assert.Equal("525:370\r\n", await Convert("tap.csv", "700:494\r\n"));
    }

    [Theory]
    [InlineData("slot_matrix", "2,3")]
    [InlineData("info", "6,24")]
    [InlineData("default", "8")]
    [InlineData("slot_message_textsize", "60")]
    public async Task NonPixelSchemasWinOverCompanion(string stem, string source)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, stem + ".png"), "");
        Assert.Equal(source, await Convert(stem + ".csv", source));
    }

    [Theory]
    [InlineData("base_1_move.csv", "32--,++$dynamic")]
    [InlineData("base_1_move.csv", "++32--,0")]
    [InlineData("slotnum_pos.csv", "40,$dynamic,center,4")]
    [InlineData("btnmove.csv", "left,300,easein_quad,$dynamic,easeout_cubic")]
    [InlineData("rect.csv", "button,10,20,30,40\nother,50,60,$dynamic,80\n")]
    [InlineData("rect.csv", "1,10,20,30,40\n2,50,60,70,80\n")]
    public async Task UnknownOrDynamicSchemasAreNotPartiallyChanged(string name, string source) =>
        Assert.Equal(source, await Convert(name, source));

    [Fact]
    public async Task RectangleTablesPreserveIdsWhitespaceAndLineEndings()
    {
        Assert.Equal("01,72,111,288,33\nbutton_02,369,111,288,33\n",
            await Convert("atlas.csv", "01,96,148,384,45\nbutton_02,492,148,384,45\n", true));
    }

    [Fact]
    public async Task FontLanguagePrefixMayContainDigits()
    {
        Assert.Equal("size=36\r\nwidth=337,\r\nentirexscale=66\r\ncolor=0xFFFFFF",
            await Convert("fonttext/cn2_tag_font.csv", "size=48\r\nwidth=450,\r\nentirexscale=66\r\ncolor=0xFFFFFF", true));
    }

    [Fact]
    public async Task FontLayoutPreservesCommentsBomPercentagesAndColors()
    {
        const string source = "// left=1280\r\nleft = 316\r\ntop = 550 // baseline\r\nwidth=1025\r\nsize=50\r\n"
            + "spacemiddle=-13\r\noutline=1\r\nentirexscale=66\r\nxscale=100\r\ncolor=FF123456\r\n"
            + "anchorcenter=0\r\nalign=center\r\nface=font/example.otf\r\nheight=$dynamic\r\n";
        string expected = source.Replace("left = 316", "left = 237").Replace("top = 550", "top = 412")
            .Replace("width=1025", "width=768").Replace("size=50", "size=37").Replace("spacemiddle=-13", "spacemiddle=-9");
        Assert.Equal(expected, await Convert("_data/image/gui/fonttext/cn_tag_font.csv", source, true));
    }

    [Fact]
    public async Task ImageSidecarOutsideKnownTreesIsRecognized()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "sprite.png"), []);
        Assert.Equal("60,-30", await Convert("sprite.csv", "80,-40"));
    }

    [Theory]
    [InlineData("effect.lua", false)]
    [InlineData("effect.png", false)]
    [InlineData("EFFECT.PNG", false)]
    [InlineData("effect", false)]
    [InlineData("effect", true)]
    [InlineData("effect.anim", true)]
    public async Task SameNameFilesAndFoldersWorkInArbitraryDirectories(string name, bool folder)
    {
        string directory = Path.Combine(root, "arbitrary");
        Directory.CreateDirectory(directory);
        string companion = Path.Combine(directory, name);
        if (folder) Directory.CreateDirectory(companion);
        else await File.WriteAllTextAsync(companion, "placeholder");
        string stem = folder ? name : Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        Assert.Equal("232,22", await Convert("arbitrary/" + stem + ".csv", "310,30"));
    }

    [Theory]
    [InlineData("310,30\n40,50")]
    [InlineData("track01,track02")]
    [InlineData("$dynamic,30")]
    [InlineData("310,30,50")]
    public async Task PairingDoesNotScaleUnknownContent(string content)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "effect.lua"), "-- companion");
        Assert.Equal(content, await Convert("effect.csv", content));
    }

    [Fact]
    public async Task SimilarNamesAndDifferentDirectoriesAreNotCompanions()
    {
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(root, "effect_extra.png"), "");
        await File.WriteAllTextAsync(Path.Combine(root, "nested", "effect.lua"), "");
        Assert.Equal("310,30", await Convert("effect.csv", "310,30"));
    }

    [Fact]
    public async Task FontTableTrailingCommaIsPreserved()
    {
        Assert.Equal("width = 337, // area\r\nheight=187,\r\nsize=36,",
            await Convert("_data/image/menu/backlog/fonttext/tag_font_advname.csv",
                "width = 450, // area\r\nheight=250,\r\nsize=48,"));
    }

    [Theory]
    [InlineData("fonttext", true)]
    [InlineData("FONTTEXT", true)]
    [InlineData("arbitrary", false)]
    public async Task FontLayoutsUseSpecificSchemaAndOtherCoordinatesStillRequireCompanions(string directory, bool font)
    {
        string parent = Path.Combine(root, directory);
        Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(Path.Combine(parent, "tag_font.lua"), "-- companion");
        string relative = directory + "/tag_font.csv";
        Assert.Equal(font ? "size=36\r\nwidth=337," : "size=48\r\nwidth=450,", await Convert(relative, "size=48\r\nwidth=450,"));
        await File.WriteAllTextAsync(Path.Combine(parent, "sprite.lua"), "-- companion");
        Assert.Equal("60,-30", await Convert(directory + "/sprite.csv", "80,-40"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConversionRoutesArchivedAndLooseCsvThroughTextOption(bool enabled)
    {
        string input = Path.Combine(root, "input"), output = Path.Combine(root, "output");
        string name = "_data/image/menu/save/slot_pos.csv";
        string loose = Path.Combine(input, name);
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        await File.WriteAllTextAsync(loose, "40,80");
        string companion = Path.ChangeExtension(loose, ".lua");
        await File.WriteAllTextAsync(companion, "-- companion");
        const string fontName = "_data/image/gui/fonttext/tag_font.csv";
        string font = Path.Combine(input, fontName);
        Directory.CreateDirectory(Path.GetDirectoryName(font)!);
        byte[] fontBytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("width=450,\r\nsize=48,\r\n")];
        await File.WriteAllBytesAsync(font, fontBytes);
        string ini = Path.Combine(input, "system.ini");
        await File.WriteAllTextAsync(ini, "[WINDOWS]\nWIDTH=1280\nHEIGHT=720\n");
        const string moveName = "layout/base_2_move.csv";
        const string moveSource = "32--,++0";
        string move = Path.Combine(input, moveName);
        Directory.CreateDirectory(Path.GetDirectoryName(move)!);
        await File.WriteAllTextAsync(move, moveSource);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new ExtractedArchive('8', [
            new PfsEntry(Encoding.UTF8.GetBytes("system.ini"), "system.ini", 0, (uint)new FileInfo(ini).Length, ini),
            new PfsEntry(Encoding.UTF8.GetBytes(name), name, 0, 5, loose),
            new PfsEntry(Encoding.UTF8.GetBytes(name[..^4] + ".lua"), name[..^4] + ".lua", 0,
                (uint)new FileInfo(companion).Length, companion),
            new PfsEntry(Encoding.UTF8.GetBytes(fontName), fontName, 0, (uint)fontBytes.Length, font),
            new PfsEntry(Encoding.UTF8.GetBytes(moveName), moveName, 0, (uint)Encoding.UTF8.GetByteCount(moveSource), move)
        ]), Path.Combine(input, "root.pfs"));
        await new ConversionService().ConvertAsync(new ConversionOptions(input, output, Ratio: .75,
            Categories: enabled ? AssetCategories.Text : AssetCategories.None));
        string expected = enabled ? "30,60" : "40,80";
        Assert.Equal(expected, await File.ReadAllTextAsync(Path.Combine(output, name)));
        Assert.Equal(expected, Encoding.UTF8.GetString((await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), name))!));
        Assert.Equal("40,80", await File.ReadAllTextAsync(loose));
        byte[] expectedFont = enabled ? [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("width=337,\r\nsize=36,\r\n")] : fontBytes;
        Assert.Equal(expectedFont, await File.ReadAllBytesAsync(Path.Combine(output, fontName)));
        Assert.Equal(expectedFont, await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), fontName));
        Assert.Equal(fontBytes, await File.ReadAllBytesAsync(font));
        string expectedMove = enabled ? "24--,++0" : moveSource;
        Assert.Equal(expectedMove, await File.ReadAllTextAsync(Path.Combine(output, moveName)));
        Assert.Equal(expectedMove, Encoding.UTF8.GetString((await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), moveName))!));
        Assert.Equal(moveSource, await File.ReadAllTextAsync(move));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
