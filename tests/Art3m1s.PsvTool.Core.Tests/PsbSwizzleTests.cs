using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbSwizzleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psb-swizzle-" + Guid.NewGuid().ToString("N"));
    public PsbSwizzleTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(16, 16, new byte[] { 0, 4, 1, 5, 8, 12, 9, 13, 2, 6, 3, 7, 10, 14, 11, 15 })]
    [InlineData(16, 8, new byte[] { 0, 4, 1, 5, 2, 6, 3, 7 })]
    [InlineData(8, 16, new byte[] { 0, 2, 1, 3, 4, 6, 5, 7 })]
    public void SwizzleMatchesGxmYFirstMortonGoldenOrder(int width, int height, byte[] blockOrder)
    {
        byte[] linear = Enumerable.Range(0, blockOrder.Length).SelectMany(block => Enumerable.Repeat((byte)block, 16)).ToArray();
        byte[] swizzled = PsbDxt5Storage.Swizzle(linear, width, height);
        Assert.Equal(blockOrder.SelectMany(block => Enumerable.Repeat(block, 16)), swizzled);
        Assert.Equal(linear, PsbDxt5Storage.Unswizzle(swizzled, width, height));
    }

    [Theory]
    [InlineData(1, 1, 16)]
    [InlineData(3, 7, 32)]
    [InlineData(9, 5, 128)]
    [InlineData(17, 11, 512)]
    [InlineData(256, 128, 32768)]
    [InlineData(128, 256, 32768)]
    public void NonPowerOfTwoAndTinySizesRoundTripWholeBlocksWithZeroPadding(int width, int height, int storageBytes)
    {
        byte[] linear = new byte[PsbDxt5Storage.LinearBytes(width, height)];
        new Random(197).NextBytes(linear);
        byte[] swizzled = PsbDxt5Storage.Swizzle(linear, width, height);
        Assert.Equal(storageBytes, swizzled.Length);
        Assert.Equal(linear, PsbDxt5Storage.Unswizzle(swizzled, width, height));
        if (width == 9 && height == 5) Assert.All(swizzled[96..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task LayoutOnlyConversionMovesBlocksLosslesslyAndResizeCanReadSwizzled(int version)
    {
        string path = Path.Combine(_root, "hero.psb"), reference = Path.Combine(_root, "reference.psb");
        byte[] original = PsbFixture.CreateWithOptions((ushort)version, true,
            (x, y) => ((byte)(x * 13), (byte)(y * 20), (byte)60, (byte)(x * 11)), (17, 11), (8, 16));
        await File.WriteAllBytesAsync(path, original);
        var processor = new PsbProcessor();
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5);
        byte[] linearPsb = await File.ReadAllBytesAsync(path);
        var payloads = new[] { "atlas0", "atlas1" }.ToDictionary(name => name, name => PsbProcessor.InspectTextureResource(linearPsb, name));
        await File.WriteAllBytesAsync(reference, linearPsb);
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5, dxt5Layout: PsbDxt5Layout.Swizzled);
        byte[] swizzledPsb = await File.ReadAllBytesAsync(path);
        foreach (var atlas in (await PsbTextureScanner.InspectFileAsync(path)).Atlases)
        {
            Assert.Equal("DXT5_SWIZZLED", atlas.Format);
            byte[] payload = PsbProcessor.InspectTextureResource(swizzledPsb, atlas.Source);
            Assert.Equal(payloads[atlas.Source], PsbDxt5Storage.Unswizzle(payload, atlas.Width, atlas.Height));
        }
        Assert.Equal(1280, PsbProcessor.InspectNumericMetadata(swizzledPsb, "screenSize", "width"));
        Assert.Equal("RGBA8", PsbProcessor.InspectStringMetadata(swizzledPsb, "metadata", "formatDescription"));
        Assert.False((await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5,
            dxt5Layout: PsbDxt5Layout.Swizzled)).Changed);
        Assert.Equal(swizzledPsb, await File.ReadAllBytesAsync(path));
        await processor.ProcessWithFormatAsync(path, .5, .5, PsbTextureFormat.Dxt5, dxt5Layout: PsbDxt5Layout.Swizzled);
        await processor.ProcessWithFormatAsync(reference, .5, .5, PsbTextureFormat.Dxt5);
        var scaled = await PsbTextureScanner.InspectFileAsync(path);
        Assert.Equal(8, scaled.Width); Assert.Equal(5, scaled.Height);
        Assert.Equal(640, PsbProcessor.InspectNumericMetadata(await File.ReadAllBytesAsync(path), "screenSize", "width"));
        foreach (var atlas in scaled.Atlases)
            Assert.Equal(PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(reference), atlas.Source),
                PsbDxt5Storage.Unswizzle(PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path), atlas.Source), atlas.Width, atlas.Height));
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5);
        Assert.All((await PsbTextureScanner.InspectFileAsync(path)).Atlases, atlas => Assert.Equal("DXT5", atlas.Format));
        await processor.InspectAsync(path);
    }

    [Theory]
    [InlineData(PsbTextureFormat.Pvrtc2_4)]
    [InlineData(PsbTextureFormat.Pvrtc2_2)]
    public async Task SwizzledInputCanConvertToPvrtcAndMasterOffPreservesLayout(PsbTextureFormat target)
    {
        string path = Path.Combine(_root, "alpha.psb");
        await File.WriteAllBytesAsync(path, PsbFixture.Create((32, 16)));
        var processor = new PsbProcessor();
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5, dxt5Layout: PsbDxt5Layout.Swizzled);
        byte[] original = await File.ReadAllBytesAsync(path);
        Assert.False((await processor.ProcessWithFormatAsync(path, 1, 1, null)).Changed);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        await processor.ProcessWithFormatAsync(path, .5, .5, null);
        Assert.Equal("DXT5_SWIZZLED", Assert.Single((await PsbTextureScanner.InspectFileAsync(path)).Atlases).Format);
        await processor.ProcessWithFormatAsync(path, 1, 1, target, dxt5Layout: PsbDxt5Layout.Swizzled);
        var info = await PsbTextureScanner.InspectFileAsync(path);
        Assert.Equal(PsbTextureFormats.TypeName(target), Assert.Single(info.Atlases).Format);
        byte[] pixels = PvrTextureCodec.Decode(PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path), "atlas0"), info.Width, info.Height,
            PsbTextureFormats.NativeFormat(target));
        Assert.InRange(pixels[0], (byte)160, (byte)200);
    }

    [Fact]
    public async Task InvalidLayoutsAndDimensionsFailWithoutChangingFile()
    {
        Assert.Throws<InvalidDataException>(() => PsbDxt5Storage.Swizzle(new byte[16], 0, 4));
        Assert.Throws<InvalidDataException>(() => PsbDxt5Storage.Swizzle(new byte[16], 4097, 4));
        Assert.Throws<InvalidDataException>(() => PsbDxt5Storage.Unswizzle(new byte[16], 16, 16));
        Assert.Throws<InvalidDataException>(() => PsbDxt5Storage.Swizzle(new byte[16], 16, 16));
        string path = Path.Combine(_root, "invalid.psb"); byte[] original = PsbFixture.Create((32, 16));
        await File.WriteAllBytesAsync(path, original);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new PsbProcessor().ProcessWithFormatAsync(path, 1, 1,
            PsbTextureFormat.Dxt5, dxt5Layout: (PsbDxt5Layout)999));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions(_root, _root + "-out", Dxt5Layout: (PsbDxt5Layout)999).Validate());
        Assert.Equal(PsbDxt5Layout.Swizzled, new ConversionOptions(_root, _root + "-out").Dxt5Layout);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
