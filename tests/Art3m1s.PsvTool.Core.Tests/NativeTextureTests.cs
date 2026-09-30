using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class NativeTextureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "native-texture-tests-" + Guid.NewGuid().ToString("N"));
    public NativeTextureTests() => Directory.CreateDirectory(root);
    private async Task<string> Png(string name, bool gray = false, bool alpha = false, bool metadata = false, int width = 32, int height = 16)
    {
        string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Image<Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            image[x, y] = new((byte)(x * 5), gray ? (byte)(x * 5) : (byte)(y * 12), gray ? (byte)(x * 5) : (byte)160,
                alpha ? (byte)(x * 255 / (width - 1)) : (byte)255);
        if (metadata) image.Metadata.GetPngMetadata().TextData.Add(new PngTextData("offset", "12,24", "", ""));
        await image.SaveAsPngAsync(path);
        return path;
    }
    private static async Task<TextureImageInfo> Info(string path, string relative = "image/bg/test.png") =>
        TextureScanner.InspectBytes(await File.ReadAllBytesAsync(path), relative);
    [Fact]
    public async Task RecommendationsPreserveGrayscaleMetadataAndUi()
    {
        var color = await Info(await Png("color.png"));
        var alpha = await Info(await Png("alpha.png", alpha: true));
        var gray = await Info(await Png("gray.png", gray: true));
        var metadata = await Info(await Png("metadata.png", metadata: true));
        Assert.Equal(NativeTextureFormat.Bc1, NativeTextureFormats.Recommend(color, false));
        Assert.Equal(NativeTextureFormat.Bc3, NativeTextureFormats.Recommend(alpha, false));
        Assert.Equal(NativeTextureFormat.Bc1, NativeTextureFormats.Recommend(alpha, true));
        Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Recommend(gray, false));
        Assert.False(NativeTextureFormats.DefaultEnabled(gray));
        Assert.True(metadata.HasMetadata);
        Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Recommend(metadata, true));
        Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Recommend(color with { Category = "system" }, false));
        Assert.NotNull(NativeTextureFormats.Unsuitable(alpha, NativeTextureFormat.Bc1, 1, false));
        Assert.NotNull(NativeTextureFormats.Unsuitable(color, NativeTextureFormat.Bc4, 1, false));
        Assert.NotNull(NativeTextureFormats.Unsuitable(color with { Width = 73 }, NativeTextureFormat.PvrtcRgb4, 1, false));
        Assert.NotNull(NativeTextureFormats.Unsuitable(metadata, NativeTextureFormat.Bc3, 1, false));
    }
    public static IEnumerable<object[]> SupportedColorFormats => NativeTextureFormats.All
        .Where(x => x.PvrCode >= 0 && x.Format is not (NativeTextureFormat.Bc4 or NativeTextureFormat.Bc4Signed or NativeTextureFormat.Bc5 or NativeTextureFormat.Bc5Signed))
        .Select(x => new object[] { x.Format });
    [Theory]
    [MemberData(nameof(SupportedColorFormats))]
    public async Task NativeEncoderWritesValidatedContainers(NativeTextureFormat format)
    {
        string input = await Png("input.png", alpha: NativeTextureFormats.Info(format).HasAlpha && format != NativeTextureFormat.Bc1);
        var info = await Info(input);
        var result = await new NativeTextureProcessor().ConvertAsync(input, info,
            new(Rules: [new(info.GroupKey, true, format)]), 1);
        Assert.True(result.Converted, result.Message); Assert.False(File.Exists(input));
        byte[] bytes = await File.ReadAllBytesAsync(result.OutputPath);
        bool dds = result.OutputPath.EndsWith(".dds", StringComparison.Ordinal);
        Assert.True(bytes.AsSpan().StartsWith(dds ? "DDS "u8 : "PVR\x03"u8));
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dds ? 16 : 28)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dds ? 12 : 24)));
        if (!dds) Assert.Equal((uint)NativeTextureFormats.Info(format).PvrCode, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.InRange(bytes.Length - NativeTextureFormats.PayloadBytes(format, 32, 16), 52, 256);
        Assert.True((await Info(result.OutputPath)).IsNative);
    }
    [Fact]
    public async Task Bc3AlphaAndColorSurviveDecode()
    {
        string p = await Png("alpha.png", alpha: true);
        using Image<Rgba32> before = Image.Load<Rgba32>(p);
        var result = await new NativeTextureProcessor().ConvertAsync(p, await Info(p), new(), 1);
        byte[] dds = await File.ReadAllBytesAsync(result.OutputPath);
        byte[] pixels = PsbProcessor.DecodeDxt5ForTest(dds[128..], 32, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++)
        {
            int at = (y * 32 + x) * 4;
            Assert.InRange(Math.Abs(pixels[at + 3] - before[x, y].A), 0, 8);
            Assert.InRange(Math.Abs(pixels[at] - before[x, y].R), 0, 22);
        }
    }
    [Fact]
    public async Task MetadataAndIncompatibleSelectionsKeepOriginalBytes()
    {
        string p = await Png("meta.png", metadata: true);
        byte[] original = await File.ReadAllBytesAsync(p);
        var info = await Info(p);
        var result = await new NativeTextureProcessor().ConvertAsync(p, info, new(Rules: [new(info.GroupKey, true, NativeTextureFormat.Bc3)]), 1);
        Assert.False(result.Converted); Assert.Equal(original, await File.ReadAllBytesAsync(p));
        string a = await Png("alpha.png", alpha: true);
        info = await Info(a);
        result = await new NativeTextureProcessor().ConvertAsync(a, info, new(Rules: [new(info.GroupKey, true, NativeTextureFormat.Etc1)]), 1);
        Assert.False(result.Converted); Assert.True(File.Exists(a));
    }
    [Fact]
    public async Task PfsScanAndRepackPreserveRawNamesAndScriptReferences()
    {
        string input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        string bg = await Png("bg.png"), fg = await Png("fg.png", alpha: true), gray = await Png("gray.png", gray: true), meta = await Png("meta.png", metadata: true);
        string script = Path.Combine(root, "first.iet"); await File.WriteAllTextAsync(script, "[lyc id=1 file=\"image/bg/背景.png\"]");
        var entries = new[] { ("image/bg/背景.png", bg), ("image/fg/test.png", fg), ("image/bg/mask.png", gray), ("image/bg/offset.png", meta), ("first.iet", script) }
            .Select(x => new PfsEntry(Encoding.UTF8.GetBytes(x.Item1), x.Item1, 0, 0, x.Item2)).ToArray();
        var codec = new PfsCodec(); await codec.PackPf8Async(new('8', entries), Path.Combine(input, "root.pfs.abc"));
        var scan = await new TextureScanner().ScanAsync(input);
        Assert.Equal(4, scan.Count); Assert.Contains(scan, x => x.IsGray && x.GroupKey.EndsWith("#gray", StringComparison.Ordinal));
        string output = Path.Combine(root, "output");
        await new ConversionService().ConvertAsync(new(input, output, Ratio: 1, Categories: AssetCategories.Images,
            ConvertEmotePsbTexturesToDxt5: false, NativeTextures: new()));
        var extracted = await codec.ExtractAsync(Path.Combine(output, "root.pfs.abc"), Path.Combine(root, "unpacked"));
        Assert.Contains(extracted.Entries, x => x.Path == "image/bg/背景.dds");
        Assert.DoesNotContain(extracted.Entries, x => x.Path == "image/bg/背景.png");
        Assert.Contains(extracted.Entries, x => x.Path == "image/fg/test.png");
        Assert.Contains(extracted.Entries, x => x.Path == "image/bg/mask.png");
        Assert.Contains(extracted.Entries, x => x.Path == "image/bg/offset.png");
        Assert.Equal(await File.ReadAllTextAsync(script), await File.ReadAllTextAsync(extracted.Entries.Single(x => x.Path == "first.iet").ExtractedPath));
        Assert.True((await Info(extracted.Entries.Single(x => x.Path == "image/bg/offset.png").ExtractedPath)).HasMetadata);
    }
    [Fact]
    public async Task ExistingNativeOrConflictingNameIsNotOverwritten()
    {
        string p = await Png("image/bg/a.png");
        var info = await Info(p, "image/bg/a.png");
        info = info with { ExistingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IMAGE/BG/A.DDS" } };
        Assert.NotNull(NativeTextureFormats.Unsuitable(info, NativeTextureFormat.Bc1, 1, false));
        var result = await new NativeTextureProcessor().ConvertAsync(p, info, new(), 1);
        Assert.False(result.Converted);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextureScanner().ScanAsync(root, token: cancelled.Token));
    }
    [Fact]
    public async Task GrayCanOptIntoBc4AndBadImageIsPreserved()
    {
        string p = await Png("gray.png", gray: true);
        var i = await Info(p);
        var r = await new NativeTextureProcessor().ConvertAsync(p, i, new(Rules: [new(i.GroupKey, true, NativeTextureFormat.Bc4)]), 1);
        Assert.True(r.Converted);
        Assert.Equal("ATI1", Encoding.ASCII.GetString((await File.ReadAllBytesAsync(r.OutputPath)).AsSpan(84, 4)));
        string input = Path.Combine(root, "broken-input"); Directory.CreateDirectory(input);
        string bad = Path.Combine(root, "broken.png"); await File.WriteAllBytesAsync(bad, [1, 2, 3]);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("image/bg/bad.png"), "image/bg/bad.png", 0, 3, bad)]), Path.Combine(input, "root.pfs"));
        var scan = await new TextureScanner().ScanAsync(input);
        Assert.NotNull(Assert.Single(scan).Error);
        string output = Path.Combine(root, "broken-output");
        await new ConversionService().ConvertAsync(new(input, output, Categories: AssetCategories.Images, ConvertEmotePsbTexturesToDxt5: false, NativeTextures: new()));
        var unpack = await codec.ExtractAsync(Path.Combine(output, "root.pfs"), Path.Combine(root, "broken-unpack"));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Assert.Single(unpack.Entries).ExtractedPath));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
