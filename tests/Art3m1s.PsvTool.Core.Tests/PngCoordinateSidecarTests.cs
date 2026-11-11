using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PngCoordinateSidecarTests : IDisposable
{
    private const string Reader = """
        function getfgfilepos(px)
            local r = { x = 0, y = 0 }
            if isFile(px) then
                local p = e:loadPngComments(px)
                local a = p and explode(",", p.comment)
                if p and a[1] == "pos" then
                    r.x = tn(a[2])
                    r.y = tn(a[3])
                end
            end
            return r
        end
        local position = fgpos[file] or getfgfilepos(path)
        """;
    private readonly string root = Path.Combine(Path.GetTempPath(), "fg-sidecar-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(NativeTextureFormat.Auto, true, "DXT5")]
    [InlineData(NativeTextureFormat.AutoLinear, false, "DXT1")]
    public async Task ConversionExportsPairedCoordinatesInPfsAndLooseFiles(
        NativeTextureFormat format, bool alpha, string expectedFourCc)
    {
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        string png = Path.Combine(input, "image/fg/loose.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await WritePng(png, "pos,-13,25", alpha);
        string script = Path.Combine(input, "fg.lua");
        await File.WriteAllTextAsync(script, Reader);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [Entry("image/fg/archived.png", png)]), Path.Combine(input, "root.pfs"));

        var images = await new TextureScanner().ScanAsync(input);
        Assert.All(images, image =>
        {
            Assert.True(image.CanExportPngCoordinates);
            Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Resolve(image, format, false));
            Assert.Equal(NativeTextureFormat.Preserve,
                NativeTextureFormats.Resolve(image, NativeTextureFormat.AutoWithoutMetadata, false, true));
        });
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Images | AssetCategories.Text,
            NativeTextures: new(Rules: [new("image/fg", true, format)], ExportPngCoordinates: true)));

        string archive = Path.Combine(output, "root.pfs");
        byte[] archivedDds = (await codec.ReadSmallEntryAsync(archive, "image/fg/archived.dds"))!;
        Assert.Equal(expectedFourCc, Encoding.ASCII.GetString(archivedDds.AsSpan(84, 4)));
        Assert.Equal(format == NativeTextureFormat.Auto, NativeDdsLayout.IsGxmSwizzled(archivedDds));
        Assert.Null(await codec.ReadSmallEntryAsync(archive, "image/fg/archived.png"));
        Assert.Equal("art3m1s_png_coordinates_v1\ncomment,pos,-6,12\n",
            Encoding.ASCII.GetString((await codec.ReadSmallEntryAsync(archive, "image/fg/archived.csv"))!));
        Assert.Equal("art3m1s_png_coordinates_v1\ncomment,pos,-6,12\n",
            await File.ReadAllTextAsync(Path.Combine(output, "image/fg/loose.csv")));
        Assert.True(File.Exists(Path.Combine(output, "image/fg/loose.dds")));
        Assert.False(File.Exists(Path.Combine(output, "image/fg/loose.png")));
        string adapted = await File.ReadAllTextAsync(Path.Combine(output, "fg.lua"));
        Assert.Contains("engine:file(csv)", adapted);
        Assert.Contains("if isFile(px) or art3m1s_png_csv_comments(e, px) then", adapted);
        Assert.Contains("fgpos[file] or getfgfilepos(path)", adapted);
        Assert.Contains("local p = art3m1s_png_comments(e, px)", adapted);
        Assert.Equal(Reader, await File.ReadAllTextAsync(script));
    }

    [Theory]
    [InlineData("pos,13,25", true, false, true, false)]
    [InlineData("pos,13,25", false, false, false, true)]
    [InlineData("pos,13,25", true, true, false, true)]
    [InlineData("pos,13,25,64,64,2", true, false, false, true)]
    public async Task KeepsPngWhenTextIsOffOrSidecarIsUnsafe(string comment, bool recognizedReader, bool csvExists,
        bool eligible, bool processText)
    {
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        string png = Path.Combine(input, "image/fg/sample.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await WritePng(png, comment, alpha: true);
        if (recognizedReader) await File.WriteAllTextAsync(Path.Combine(input, "fg.lua"), Reader);
        if (csvExists) await File.WriteAllTextAsync(Path.ChangeExtension(png, ".csv"), "existing");
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        Assert.Equal(eligible, Assert.Single(await new TextureScanner().ScanAsync(input)).CanExportPngCoordinates);
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: processText ? AssetCategories.Text | AssetCategories.Images : AssetCategories.Images,
            NativeTextures: new(ExportPngCoordinates: true)));
        Assert.True(File.Exists(Path.Combine(output, "image/fg/sample.png")));
        Assert.False(File.Exists(Path.Combine(output, "image/fg/sample.dds")));
        Assert.Equal(csvExists, File.Exists(Path.Combine(output, "image/fg/sample.csv")));
    }

    [Theory]
    [InlineData("image/ev", "comment", "pos,13,-25,32,16", "pos,6,-12,16,8")]
    [InlineData("image/cg", "crop", "12,4,16,12", "6,2,8,6")]
    [InlineData("image/face", "offset", "-13,25", "-6,12")]
    [InlineData("image/bg", "Left", "13", "6")]
    [InlineData("image/fg", "comment", "pos,-13,25,32,7,2,ani", "pos,-6,12,16,3,2,ani")]
    public async Task AutoExportsGeometryAndPreservesMetadataKeysAndFrameCount(
        string category, string key, string value, string expected)
    {
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        string relative = category + "/sample";
        string png = Path.Combine(input, relative + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await WritePng(png, value, alpha: true, key);
        await File.WriteAllTextAsync(Path.Combine(input, "image.lua"), """
            function metadata(path)
                if e:isFileExists(path) then
                    local result = e:loadPngComments(path)
                    return result
                end
            end
            function readComments(path)
                return e:loadPngComments(path)
            end
            """);
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));

        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: AssetCategories.Text | AssetCategories.Images, NativeTextures: new(ExportPngCoordinates: true)));

        Assert.True(File.Exists(Path.Combine(output, relative + ".dds")));
        Assert.False(File.Exists(Path.Combine(output, relative + ".png")));
        Assert.Equal($"art3m1s_png_coordinates_v1\n{key},{expected}\n",
            await File.ReadAllTextAsync(Path.Combine(output, relative + ".csv")));
        string script = await File.ReadAllTextAsync(Path.Combine(output, "image.lua"));
        Assert.Contains("if e:isFileExists(path) or art3m1s_png_csv_comments(e, path) then", script);
        Assert.Contains("return art3m1s_png_comments(e, path)", script);
    }

    [Fact]
    public async Task DisabledCoordinateExportUsesOriginalPngScalingAndAutoRules()
    {
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        const string relative = "image/bg/sample";
        string png = Path.Combine(input, relative + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await WritePng(png, "pos,-13,25,32,7,2,ani", alpha: true);
        using (var plain = new Image<Rgba32>(32, 16, new Rgba32(40, 90, 130, 255)))
            await plain.SaveAsPngAsync(Path.Combine(input, "image/bg/plain.png"));
        await File.WriteAllTextAsync(Path.Combine(input, "fg.lua"), Reader);
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));

        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: AssetCategories.Text | AssetCategories.Images, NativeTextures: new()));

        Assert.False(File.Exists(Path.Combine(output, relative + ".dds")));
        Assert.False(File.Exists(Path.Combine(output, relative + ".csv")));
        Assert.True(File.Exists(Path.Combine(output, "image/bg/plain.dds")));
        Assert.Equal(Reader, await File.ReadAllTextAsync(Path.Combine(output, "fg.lua")));
        using var converted = await Image.LoadAsync(Path.Combine(output, relative + ".png"));
        Assert.Equal((16, 8), (converted.Width, converted.Height));
        Assert.Equal("pos,-6,12,16,3,1,ani", Assert.Single(converted.Metadata.GetPngMetadata().TextData).Value);
    }

    [Fact]
    public async Task KeepsPngAndFrameCountWhenResizeCannotMatchFrameStride()
    {
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        const string relative = "image/fg/sample";
        string png = Path.Combine(input, relative + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        await WritePng(png, "pos,-13,25,32,4,3,ani", alpha: true);
        await File.WriteAllTextAsync(Path.Combine(input, "fg.lua"), Reader);
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        Assert.True(Assert.Single(await new TextureScanner().ScanAsync(input)).CanExportPngCoordinates);

        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: AssetCategories.Text | AssetCategories.Images, NativeTextures: new(ExportPngCoordinates: true)));

        Assert.False(File.Exists(Path.Combine(output, relative + ".dds")));
        Assert.False(File.Exists(Path.Combine(output, relative + ".csv")));
        using var converted = await Image.LoadAsync(Path.Combine(output, relative + ".png"));
        Assert.Equal("pos,-6,12,16,2,3,ani", Assert.Single(converted.Metadata.GetPngMetadata().TextData).Value);
    }

    private static PfsEntry Entry(string name, string path) => new(Encoding.UTF8.GetBytes(name), name, 0, 0, path);

    private static async Task WritePng(string path, string comment, bool alpha, string key = "comment")
    {
        using var image = new Image<Rgba32>(32, 16, new Rgba32(40, 90, 130, alpha ? (byte)180 : (byte)255));
        image.Metadata.GetPngMetadata().TextData.Add(new PngTextData(key, comment, "", ""));
        await image.SaveAsPngAsync(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
