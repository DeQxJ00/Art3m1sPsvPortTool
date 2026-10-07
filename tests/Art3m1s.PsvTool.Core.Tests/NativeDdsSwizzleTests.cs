using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class NativeDdsSwizzleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dds-swizzle-" + Guid.NewGuid().ToString("N"));
    public NativeDdsSwizzleTests() => Directory.CreateDirectory(_root);

    private async Task<string> Source(string name, int width, int height, bool alpha = true)
    {
        string path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Image<Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            image[x, y] = new((byte)(x * 17), (byte)(y * 19), 160, alpha ? (byte)(x * 13 + y * 7) : (byte)255);
        await image.SaveAsPngAsync(path);
        return path;
    }

    private static async Task<byte[]> Convert(string source, NativeTextureFormat format)
    {
        var info = TextureScanner.InspectBytes(await File.ReadAllBytesAsync(source), "image/bg/test.png");
        var result = await new NativeTextureProcessor().ConvertAsync(source, info, new(Rules: [new(info.GroupKey, true, format)]), 1);
        Assert.True(result.Converted, result.Message);
        return await File.ReadAllBytesAsync(result.OutputPath);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 7)]
    [InlineData(17, 11)]
    [InlineData(32, 16)]
    [InlineData(16, 32)]
    [InlineData(256, 128)]
    public async Task DdsSwizzledPayloadIsLosslessReorderOfLinearWithExactReservedMarker(int width, int height)
    {
        string linearPath = await Source("linear.png", width, height);
        string swizzledPath = Path.Combine(_root, "swizzled.png"); File.Copy(linearPath, swizzledPath);
        byte[] linear = await Convert(linearPath, NativeTextureFormat.Bc3);
        byte[] swizzled = await Convert(swizzledPath, NativeTextureFormat.Bc3Swizzled);
        Assert.False(NativeDdsLayout.IsGxmSwizzled(linear));
        Assert.True(NativeDdsLayout.IsGxmSwizzled(swizzled));
        Assert.Equal("DXT5", Encoding.ASCII.GetString(swizzled, 84, 4));
        Assert.Equal("GXMSW", Encoding.ASCII.GetString(swizzled, 32, 5));
        Assert.All(swizzled[37..76], value => Assert.Equal(0, value));
        Assert.All(swizzled[88..108], value => Assert.Equal(0, value));
        Assert.Equal(124u, BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(4)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(80)));
        Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(16)));
        Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(12)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(28)));
        Assert.Equal((uint)(swizzled.Length - 128), BinaryPrimitives.ReadUInt32LittleEndian(swizzled.AsSpan(20)));
        Assert.Equal(128 + PsbDxt5Storage.SwizzledBytes(width, height), swizzled.Length);
        Assert.Equal(linear[128..], PsbDxt5Storage.Unswizzle(swizzled[128..], width, height));
        Assert.Equal(PvrTextureCodec.Decode(linear[128..], width, height, NativeTextureFormat.Bc3),
            PvrTextureCodec.Decode(swizzled[128..], width, height, NativeTextureFormat.Bc3Swizzled));
        Assert.True(TextureScanner.InspectBytes(swizzled, "image/bg/test.dds").IsNative);
        for (int index = 0; index < 128; index++)
            if (index is not (>= 20 and < 24) and not (>= 32 and < 37)) Assert.Equal(linear[index], swizzled[index]);
    }

    [Theory]
    [InlineData(NativeTextureFormat.Auto, true)]
    [InlineData(NativeTextureFormat.AutoLinear, false)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadata, true)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadataLinear, false)]
    public async Task BothAutoFamiliesOfferCorrectLayoutsAndPreserveProtectionRules(NativeTextureFormat automatic, bool swizzled)
    {
        string path = await Source("auto.png", 17, 11);
        var info = TextureScanner.InspectBytes(await File.ReadAllBytesAsync(path), "image/bg/alpha.png");
        var expected = swizzled ? NativeTextureFormat.Bc3Swizzled : NativeTextureFormat.Bc3;
        Assert.Equal(expected, NativeTextureFormats.Resolve(info, automatic, false));
        var opaque = swizzled ? NativeTextureFormat.Bc1Swizzled : NativeTextureFormat.Bc1;
        Assert.Equal(opaque, NativeTextureFormats.Resolve(info with { HasAlpha = false }, automatic, false));
        Assert.Equal(opaque, NativeTextureFormats.Resolve(info, automatic, true));
        foreach (var protectedImage in new[] { info with { IsGray = true }, info with { HasMetadata = true }, info with { IsNative = true }, info with { Error = "bad" } })
            Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Resolve(protectedImage, automatic, false));
        Assert.Equal(NativeTextureFormats.IsManualAutomatic(automatic) ? expected : NativeTextureFormat.Preserve,
            NativeTextureFormats.Resolve(info with { Category = "system" }, automatic, false));
        byte[] data = await Convert(path, automatic);
        Assert.Equal(swizzled, NativeDdsLayout.IsGxmSwizzled(data));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 7)]
    [InlineData(17, 11)]
    [InlineData(32, 16)]
    [InlineData(16, 32)]
    [InlineData(128, 256)]
    public async Task OpaqueDxt1UsesEightByteSwizzledBlocksAndRetainsColors(int width, int height)
    {
        string path = await Source("opaque.png", width, height, alpha: false);
        string copy = Path.Combine(_root, "linear.png"); File.Copy(path, copy);
        byte[] swizzled = await Convert(path, NativeTextureFormat.Bc1Swizzled);
        byte[] linear = await Convert(copy, NativeTextureFormat.Bc1);
        Assert.True(NativeDdsLayout.IsGxmSwizzled(swizzled));
        Assert.False(NativeDdsLayout.IsGxmSwizzled(linear));
        Assert.Equal("DXT1", Encoding.ASCII.GetString(swizzled, 84, 4));
        Assert.Equal("GXMSW", Encoding.ASCII.GetString(swizzled, 32, 5));
        Assert.All(linear[32..76], value => Assert.Equal(0, value));
        Assert.Equal(128 + GxmBlockTextureStorage.SwizzledBytes(width, height, 8), swizzled.Length);
        Assert.Equal(linear[128..], GxmBlockTextureStorage.Unswizzle(swizzled[128..], width, height, 8));
        byte[] pixels = PvrTextureCodec.Decode(swizzled[128..], width, height, NativeTextureFormat.Bc1Swizzled);
        Assert.Equal(PvrTextureCodec.Decode(linear[128..], width, height, NativeTextureFormat.Bc1), pixels);
        for (int i = 3; i < pixels.Length; i += 4) Assert.Equal(255, pixels[i]);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void Bc1AndBc3ShareYFirstGoldenOrderWithoutChangingBlockContents(int blockBytes)
    {
        byte[] original = Enumerable.Range(0, 16).SelectMany(i => Enumerable.Repeat((byte)i, blockBytes)).ToArray();
        byte[] swizzled = GxmBlockTextureStorage.Swizzle(original, 16, 16, blockBytes);
        byte[] order = [0, 4, 1, 5, 8, 12, 9, 13, 2, 6, 3, 7, 10, 14, 11, 15];
        Assert.Equal(order.SelectMany(i => Enumerable.Repeat(i, blockBytes)), swizzled);
        Assert.Equal(original, GxmBlockTextureStorage.Unswizzle(swizzled, 16, 16, blockBytes));
        Assert.Throws<InvalidDataException>(() => GxmBlockTextureStorage.Swizzle(original, 4097, 16, blockBytes));
        Assert.Throws<InvalidDataException>(() => GxmBlockTextureStorage.Unswizzle(new byte[1], 16, 16, blockBytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => GxmBlockTextureStorage.SwizzledBytes(16, 16, 4));
    }

    [Fact]
    public async Task Dxt1BinaryAlphaSurvivesButSmoothAlphaAndResizingRemainProtected()
    {
        string path = await Source("binary.png", 32, 16, alpha: false);
        using (var image = Image.Load<Rgba32>(path))
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                var pixel = image[x, y]; pixel.A = 0; image[x, y] = pixel;
            }
            await image.SaveAsPngAsync(path);
        }
        var info = TextureScanner.InspectBytes(await File.ReadAllBytesAsync(path), "image/bg/binary.png");
        Assert.True(info.HasAlpha); Assert.False(info.HasSmoothAlpha);
        foreach (var format in new[] { NativeTextureFormat.Bc1, NativeTextureFormat.Bc1Swizzled })
        {
            Assert.Null(NativeTextureFormats.Unsuitable(info, format, 1, false));
            Assert.NotNull(NativeTextureFormats.Unsuitable(info, format, .5, false));
            Assert.NotNull(NativeTextureFormats.Unsuitable(info with { HasSmoothAlpha = true }, format, 1, false));
        }
        byte[] data = await Convert(path, NativeTextureFormat.Bc1Swizzled);
        byte[] pixels = PvrTextureCodec.Decode(data[128..], 32, 16, NativeTextureFormat.Bc1Swizzled);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++)
            Assert.Equal(x < 16 ? 0 : 255, pixels[(y * 32 + x) * 4 + 3]);
    }

    [Theory]
    [InlineData(NativeTextureFormat.Auto, true, true)]
    [InlineData(NativeTextureFormat.AutoLinear, false, true)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadata, true, true)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadataLinear, false, true)]
    [InlineData(NativeTextureFormat.Auto, true, false)]
    [InlineData(NativeTextureFormat.AutoLinear, false, false)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadata, true, false)]
    [InlineData(NativeTextureFormat.AutoWithoutMetadataLinear, false, false)]
    public async Task PfsAndLooseImagesShareLayoutAndRatioRules(NativeTextureFormat format, bool swizzled, bool alpha)
    {
        string input = Path.Combine(_root, "game"), output = Path.Combine(_root, "output"); Directory.CreateDirectory(input);
        string path = await Source("original.png", 34, 22, alpha);
        var codec = new PfsCodec(); string relative = "image/bg/背景.png";
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes(relative), relative, 0, 0, path)]), Path.Combine(input, "root.pfs.010"));
        string loose = Path.Combine(input, "image", "bg"); Directory.CreateDirectory(loose); File.Copy(path, Path.Combine(loose, "loose.png"));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Images,
            ConvertEmotePsbTexturesToDxt5: false, NativeTextures: new(Rules: [new("image/bg", true, format)])));
        var unpacked = await codec.ExtractAsync(Path.Combine(output, "root.pfs.010"), Path.Combine(_root, "unpacked"));
        var entry = Assert.Single(unpacked.Entries);
        Assert.Equal("image/bg/背景.dds", entry.Path);
        byte[] packedDds = await File.ReadAllBytesAsync(entry.ExtractedPath);
        byte[] looseDds = await File.ReadAllBytesAsync(Path.Combine(output, "image", "bg", "loose.dds"));
        Assert.Equal(packedDds, looseDds);
        Assert.Equal(swizzled, NativeDdsLayout.IsGxmSwizzled(packedDds));
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(packedDds.AsSpan(16)));
        Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(packedDds.AsSpan(12)));
        var resolved = alpha ? (swizzled ? NativeTextureFormat.Bc3Swizzled : NativeTextureFormat.Bc3)
            : (swizzled ? NativeTextureFormat.Bc1Swizzled : NativeTextureFormat.Bc1);
        Assert.Equal(alpha ? "DXT5" : "DXT1", Encoding.ASCII.GetString(packedDds, 84, 4));
        Assert.Equal(128 + NativeTextureFormats.PayloadBytes(resolved, 17, 11), packedDds.Length);
        Assert.True(File.Exists(Path.Combine(input, "image", "bg", "loose.png")));
    }

    [Fact]
    public async Task ExactMarkerRequiredAndExistingNativeResourcesRemainUnchanged()
    {
        byte[] data = await Convert(await Source("input.png", 32, 16), NativeTextureFormat.Bc3Swizzled);
        Assert.False(NativeDdsLayout.IsGxmSwizzled(data[..127]));
        foreach (int offset in new[] { 0, 4, 32, 36, 76, 80, 84 })
        {
            byte[] malformed = data.ToArray(); malformed[offset] = 0;
            Assert.False(NativeDdsLayout.IsGxmSwizzled(malformed));
        }
        string path = Path.Combine(_root, "retained.dds"); await File.WriteAllBytesAsync(path, data);
        var info = TextureScanner.InspectBytes(data, "image/bg/retained.dds");
        var result = await new NativeTextureProcessor().ConvertAsync(path, info, new(Rules: [new(info.GroupKey, true, NativeTextureFormat.Bc3)]), 1);
        Assert.False(result.Converted);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
