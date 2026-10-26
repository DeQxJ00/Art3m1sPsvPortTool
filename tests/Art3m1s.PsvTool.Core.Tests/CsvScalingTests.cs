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
    [InlineData("_data/image/menu/save/slot_pos.csv", "40,80")]
    [InlineData("_data/image/custom.csv", "720,130")]
    [InlineData("_data/fgimage/offset.csv", "720,130\n40,50")]
    public async Task PreservesUnrelatedData(string path, string source) =>
        Assert.Equal(source, await Convert(path, source));

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
        string ini = Path.Combine(input, "system.ini");
        await File.WriteAllTextAsync(ini, "[WINDOWS]\nWIDTH=1280\nHEIGHT=720\n");
        var codec = new PfsCodec();
        await codec.PackPf8Async(new ExtractedArchive('8', [
            new PfsEntry(Encoding.UTF8.GetBytes("system.ini"), "system.ini", 0, (uint)new FileInfo(ini).Length, ini),
            new PfsEntry(Encoding.UTF8.GetBytes(name), name, 0, 5, loose),
            new PfsEntry(Encoding.UTF8.GetBytes(name[..^4] + ".lua"), name[..^4] + ".lua", 0,
                (uint)new FileInfo(companion).Length, companion)
        ]), Path.Combine(input, "root.pfs"));
        await new ConversionService().ConvertAsync(new ConversionOptions(input, output, Ratio: .75,
            Categories: enabled ? AssetCategories.Text : AssetCategories.None));
        string expected = enabled ? "30,60" : "40,80";
        Assert.Equal(expected, await File.ReadAllTextAsync(Path.Combine(output, name)));
        Assert.Equal(expected, Encoding.UTF8.GetString((await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), name))!));
        Assert.Equal("40,80", await File.ReadAllTextAsync(loose));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
