using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class TextureCategoryDepthTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "texture-depth-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("image/hd/bg/sample.jpg", "image/hd/bg", "image/hd", "image/hd/bg")]
    [InlineData("IMAGES\\HD\\BG\\sample.JPEG", "images/hd/bg", "images/hd", "images/hd/bg")]
    [InlineData("graphics/hd/ui/sample.png", "graphics/hd/ui", "graphics/hd", "graphics/hd/ui")]
    [InlineData("image/bg/sample.jpg", "image/bg", "image/bg", "image/bg")]
    [InlineData("image/bg/nested/sample.jpg", "image/bg", "image/bg", "image/bg/nested")]
    [InlineData("assets/bg/sample.jpg", "assets/bg", "assets/bg", "assets/bg")]
    [InlineData("bg/sample.jpg", "bg", "bg", "bg")]
    [InlineData("sample.jpg", "(root)", "(root)", "(root)")]
    public void AutomaticAndManualDepthsExcludeTheFilename(string path, string automatic, string twoLevels, string threeLevels)
    {
        Assert.Equal(automatic, TextureScanner.CategoryFor(path));
        Assert.Equal(automatic, TextureScanner.CategoryFor(path, 0));
        Assert.Equal(twoLevels, TextureScanner.CategoryFor(path, 2));
        Assert.Equal(threeLevels, TextureScanner.CategoryFor(path, 3));
        Assert.DoesNotContain("sample.", TextureScanner.CategoryFor(path, 8));
    }

    [Theory]
    [InlineData("pc/cn/image/hd/bg/sample.jpg", "pc/cn/image/hd/bg")]
    [InlineData("_data/bgimage/sample.jpg", "_data/bgimage")]
    [InlineData("images/sd/fg/sample.png", "images/sd/fg")]
    [InlineData("assets/4k/ev/sample.jpg", "assets/4k/ev")]
    [InlineData("image/hd/mystery/bg/sample.jpg", "image/hd")]
    [InlineData("unknown/bg/sample.jpg", "unknown")]
    [InlineData("image/bg/ui/sample.jpg", "image/bg/ui")]
    [InlineData("image/ui/bg/sample.jpg", "image/ui")]
    [InlineData("image/hd/mask/bg/sample.png", "image/hd/mask")]
    public void AutomaticModeRecognizesWrappersAndProtectsUiAndUnknownParents(string path, string expected)
    {
        string category = TextureScanner.CategoryFor(path);
        Assert.Equal(expected, category);
        var info = new TextureImageInfo(null, path, category, 32, 16, 100, false, false, false, false);
        bool protectedCategory = path.Contains("unknown") || path.Contains("mystery") || path.Contains("/ui/") || path.Contains("/mask/");
        Assert.Equal(protectedCategory ? NativeTextureFormat.Preserve : NativeTextureFormat.Bc1Swizzled,
            NativeTextureFormats.Recommend(info, false));
        if (protectedCategory) Assert.False(info.IsBackground);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void InvalidManualDepthIsRejected(int depth) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureScanner.CategoryFor("image/bg/sample.jpg", depth));

    private async Task<string> CreateInput()
    {
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        string jpeg = Path.Combine(root, "source.jpg");
        using (var image = new Image<Rgb24>(32, 16, new Rgb24(40, 100, 180)))
            await image.SaveAsJpegAsync(jpeg);
        var codec = new PfsCodec();
        const string name = "image/hd/bg/archived.jpg";
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes(name), name, 0, 0, jpeg)]), Path.Combine(input, "root.pfs"));
        string loose = Path.Combine(input, "image/hd/bg/loose.jpeg");
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        File.Copy(jpeg, loose);
        string ui = Path.Combine(input, "image/hd/ui/menu.png");
        Directory.CreateDirectory(Path.GetDirectoryName(ui)!);
        using (var image = new Image<Rgb24>(32, 16, new Rgb24(30, 120, 200)))
            await image.SaveAsPngAsync(ui);
        return input;
    }

    [Fact]
    public async Task JpegArchiveAndLooseScansUseSameDepthAndRetainOriginalPaths()
    {
        string input = await CreateInput();
        var normal = await new TextureScanner(2).ScanAsync(input);
        var deeper = await new TextureScanner().ScanAsync(input);
        Assert.Equal(3, deeper.Count);
        Assert.All(normal, image => Assert.Equal("image/hd", image.Category));
        Assert.Equal(normal.Select(i => (i.Archive, i.Path)), deeper.Select(i => (i.Archive, i.Path)));
        var backgrounds = deeper.Where(i => i.Category == "image/hd/bg").ToArray();
        Assert.Equal(2, backgrounds.Length);
        Assert.Contains(backgrounds, i => i.Archive == "root.pfs");
        Assert.Contains(backgrounds, i => i.Archive == null);
        Assert.All(backgrounds, image =>
        {
            Assert.True(image.IsBackground);
            Assert.Null(image.Error);
            Assert.Equal(NativeTextureFormat.Bc1Swizzled, NativeTextureFormats.Recommend(image, false));
        });
        Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Recommend(Assert.Single(deeper, i => i.Category == "image/hd/ui"), false));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(8, true)]
    public async Task ConversionUsesSameCategoryKeysAsScannerForArchivedAndLooseJpeg(int depth, bool converted)
    {
        string input = await CreateInput();
        byte[] originalArchive = await File.ReadAllBytesAsync(Path.Combine(input, "root.pfs"));
        string output = Path.Combine(root, "output");
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Images,
            ConvertEmotePsbTexturesToDxt5: false,
            NativeTextures: new(Rules: [new("image/hd/bg", true)], CategoryDepth: depth)));
        string archivedPath = converted ? "image/hd/bg/archived.dds" : "image/hd/bg/archived.jpg";
        byte[] archived = (await new PfsCodec().ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), archivedPath))!;
        string loosePath = Path.Combine(output, converted ? "image/hd/bg/loose.dds" : "image/hd/bg/loose.jpeg");
        Assert.NotNull(archived);
        Assert.True(File.Exists(loosePath));
        if (converted)
        {
            Assert.Equal("DDS ", Encoding.ASCII.GetString(archived, 0, 4));
            Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(archived.AsSpan(16)));
            Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(archived.AsSpan(12)));
            Assert.Equal("DXT1", Encoding.ASCII.GetString(archived, 84, 4));
            Assert.Equal("GXMSW", Encoding.ASCII.GetString(archived, NativeDdsLayout.GxmSwizzledMarkerOffset, 5));
        }
        else Assert.Equal(16, Image.Identify(archived).Width);
        Assert.True(File.Exists(Path.Combine(output, "image/hd/ui/menu.png")));
        Assert.False(File.Exists(Path.Combine(output, "image/hd/ui/menu.dds")));
        Assert.Equal(originalArchive, await File.ReadAllBytesAsync(Path.Combine(input, "root.pfs")));
    }

    [Fact]
    public async Task InspectionErrorsKeepTheSelectedClassificationDepth()
    {
        string input = await CreateInput();
        string broken = Path.Combine(input, "image/hd/bg/broken.jpg");
        await File.WriteAllBytesAsync(broken, [1, 2, 3]);
        var result = await new TextureScanner().ScanAsync(input);
        var failed = Assert.Single(result, i => i.Error != null);
        Assert.Equal("image/hd/bg", failed.Category);
        Assert.EndsWith("broken.jpg", failed.Path);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
