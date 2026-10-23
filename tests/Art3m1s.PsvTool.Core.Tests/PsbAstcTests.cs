using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbAstcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psb-astc-" + Guid.NewGuid().ToString("N"));
    public PsbAstcTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("ASTC_8BPP", 4, 4)]
    [InlineData("astc_4x4", 4, 4)]
    [InlineData("ASTC_5X4", 5, 4)]
    [InlineData("ASTC_5X5", 5, 5)]
    [InlineData("ASTC_6X5", 6, 5)]
    [InlineData("ASTC_6X6", 6, 6)]
    [InlineData("ASTC_8X5", 8, 5)]
    [InlineData("ASTC_8X6", 8, 6)]
    [InlineData("ASTC_8X8", 8, 8)]
    [InlineData("ASTC_10X5", 10, 5)]
    [InlineData("ASTC_10X6", 10, 6)]
    [InlineData("ASTC_10X8", 10, 8)]
    [InlineData("ASTC_10X10", 10, 10)]
    [InlineData("ASTC_12X10", 12, 10)]
    [InlineData("ASTC_12X12", 12, 12)]
    public void ExplicitFootprintsDecodeIndependentConstantBlocks(string type, int blockWidth, int blockHeight)
    {
        var format = PsbAstcFormat.ParseType(type)!.Value;
        Assert.Equal(blockWidth, format.BlockWidth); Assert.Equal(blockHeight, format.BlockHeight);
        byte[] payload = ConstantBlocks(19, 13, blockWidth, blockHeight, false);
        Assert.Equal(payload.Length, PsbTextureSizeEstimator.PayloadBytes(type, 19, 13));
        byte[] rgba = PvrTextureCodec.DecodeAstc(payload, 19, 13, format);
        Assert.Equal(19 * 13 * 4, rgba.Length);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.InRange(rgba[i], (byte)179, (byte)181);
            Assert.InRange(rgba[i + 1], (byte)89, (byte)91);
            Assert.InRange(rgba[i + 2], (byte)39, (byte)41);
            Assert.InRange(rgba[i + 3], (byte)127, (byte)129);
        }
        Assert.Throws<InvalidDataException>(() => PvrTextureCodec.DecodeAstc(payload[..^1], 19, 13, format));
        Assert.Throws<InvalidDataException>(() => PvrTextureCodec.DecodeAstc([.. payload, 0], 19, 13, format));
    }

    [Theory]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Linear)]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_4, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, PsbDxt5Layout.Swizzled)]
    public async Task MixedAstcAndRgbaAtlasesConvertLooseAndPfsUsingGlobalRatio(PsbTextureFormat format, PsbDxt5Layout layout)
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        string source = Path.Combine(input, "sample.psb");
        byte[] original = Fixture(mixed: true);
        await File.WriteAllBytesAsync(source, original);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("image/fg/sample.psb"), "image/fg/sample.psb", 0,
            (uint)original.Length, source)]), Path.Combine(input, "root.pfs.010"));
        var scan = await new PsbTextureScanner().ScanAsync(input);
        var estimate = PsbTextureSizeEstimator.Estimate(scan, .5, format, layout);
        Assert.Empty(estimate.Issues); Assert.Equal(2, estimate.EstimatedFileCount);
        List<ConversionProgress> events = [];
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Animation,
            PsbOutputFormat: format, Dxt5Layout: layout), new Sink(events));
        Assert.DoesNotContain(events, e => e.Diagnostic?.Code == "PSB_PRESERVED_UNSUPPORTED");
        Assert.Equal(2, events.Count(e => e.Diagnostic?.Code == "PSB_ASTC_PROCESSED"));
        var extracted = await codec.ExtractAsync(Path.Combine(output, "root.pfs.010"), Path.Combine(_root, "unpacked"));
        long totalBytes = 0;
        foreach (string path in new[] { Path.Combine(output, "sample.psb"), Assert.Single(extracted.Entries).ExtractedPath })
        {
            byte[] result = await File.ReadAllBytesAsync(path);
            var atlases = (await PsbTextureScanner.InspectFileAsync(path)).Atlases;
            Assert.Equal(new[] { (32, 16), (16, 8) }, atlases.Select(a => (a.Width, a.Height)));
            foreach (var atlas in atlases)
            {
                Assert.Equal(PsbTextureFormats.TypeName(format, layout), atlas.Format);
                byte[] payload = PsbProcessor.InspectTextureResource(result, atlas.Source);
                totalBytes += payload.Length;
                Assert.Equal(PsbTextureSizeEstimator.PayloadBytes(atlas.Format, atlas.Width, atlas.Height), payload.Length);
                if (format == PsbTextureFormat.Dxt5 && layout == PsbDxt5Layout.Swizzled)
                    payload = PsbDxt5Storage.Unswizzle(payload, atlas.Width, atlas.Height);
                byte[] rgba = format == PsbTextureFormat.Dxt5 ? PsbProcessor.DecodeDxt5ForTest(payload, atlas.Width, atlas.Height)
                    : PvrTextureCodec.Decode(payload, atlas.Width, atlas.Height, PsbTextureFormats.NativeFormat(format));
                if (atlas.Source == "atlas0")
                {
                    int left = (8 * 32 + 4) * 4, right = (8 * 32 + 28) * 4;
                    Assert.InRange(rgba[left + 3], (byte)0, (byte)20);
                    Assert.InRange(rgba[right + 3], (byte)108, (byte)148);
                    Assert.InRange(rgba[right], (byte)155, (byte)205);
                }
            }
            Assert.Equal(640, PsbProcessor.InspectNumericMetadata(result, "screenSize", "width"));
            Assert.Equal(50, PsbProcessor.InspectNumericMetadata(result, "object", "hero", "motion", "idle", "layer", "0", "frameList", "0", "content", "coord", "0"));
            Assert.Equal(8, PsbProcessor.InspectNumericMetadata(result, "source", "atlas0", "icon", "piece0", "originX"));
            await new PsbProcessor().InspectAsync(path);
        }
        Assert.Equal(estimate.TargetBytes, totalBytes);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CompactVersionTablesAndNonBlockAlignedDimensionsRebuild(int version)
    {
        string path = Path.Combine(_root, "sample.psb");
        byte[] source = PsbFixture.CreateWithEncodedResources((ushort)version, true,
            (_, w, h) => ("ASTC_8BPP", ConstantBlocks(w, h, 4, 4, false)), (17, 11), (3, 7));
        await File.WriteAllBytesAsync(path, source);
        Assert.True((await new PsbProcessor().ProcessWithFormatAsync(path, .5, .5, PsbTextureFormat.Dxt5,
            dxt5Layout: PsbDxt5Layout.Swizzled)).Changed);
        Assert.Equal((ushort)version, (await new PsbProcessor().InspectAsync(path)).Version);
        var atlases = (await PsbTextureScanner.InspectFileAsync(path)).Atlases;
        Assert.Equal(new[] { (8, 5), (1, 3) }, atlases.Select(a => (a.Width, a.Height)));
        byte[] result = await File.ReadAllBytesAsync(path);
        foreach (var atlas in atlases)
            Assert.Equal(PsbDxt5Storage.SwizzledBytes(atlas.Width, atlas.Height),
                PsbProcessor.InspectTextureResource(result, atlas.Source).Length);
    }

    [Fact]
    public async Task FormatConversionOffStillResizesAstcAndKeepsItsType()
    {
        string path = Path.Combine(_root, "sample.psb");
        byte[] original = Fixture();
        await File.WriteAllBytesAsync(path, original);
        var processor = new PsbProcessor();
        Assert.False((await processor.ProcessWithFormatAsync(path, 1, 1, null)).Changed);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.True((await processor.ProcessWithFormatAsync(path, .5, .5, null)).Changed);
        var atlas = Assert.Single((await PsbTextureScanner.InspectFileAsync(path)).Atlases);
        Assert.Equal("ASTC_8BPP", atlas.Format); Assert.Equal(32, atlas.Width); Assert.Equal(16, atlas.Height);
        byte[] pixels = PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path), atlas.Source);
        Assert.Equal(512, pixels.Length);
        byte[] rgba = PvrTextureCodec.DecodeAstc(pixels, 32, 16, PsbAstcFormat.ParseType(atlas.Format)!.Value);
        Assert.InRange(rgba[(8 * 32 + 28) * 4 + 3], (byte)108, (byte)148);
        Assert.InRange(rgba[(8 * 32 + 4) * 4 + 3], (byte)0, (byte)20);
        Assert.Empty(PsbTextureSizeEstimator.Estimate([new(null, "sample.psb", original.Length, new(true, [atlas]))], .5, null).Issues);
    }

    [Fact]
    public async Task TruncatedAstcFailsWithoutPublishingOutputOrChangingInput()
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        string source = Path.Combine(input, "sample.psb");
        byte[] original = PsbFixture.CreateWithEncodedResources(3, false, (_, w, h) =>
            ("ASTC_8BPP", ConstantBlocks(w, h, 4, 4, false)[..^1]), (64, 32));
        await File.WriteAllBytesAsync(source, original);
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        var exception = await Assert.ThrowsAsync<ConversionItemException>(() => new ConversionService().ConvertAsync(
            new(input, output, Ratio: .5, Categories: AssetCategories.Animation)));
        Assert.Equal("sample.psb", exception.Entry); Assert.Equal("psb", exception.Stage);
        Assert.Contains("payload length", exception.ToString());
        Assert.Contains("2047", exception.ToString()); Assert.Contains("2048", exception.ToString());
        Assert.False(Directory.Exists(output)); Assert.False(File.Exists(output + ".conversion.lock"));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData("ASTC")]
    [InlineData("ASTC_4BPP")]
    [InlineData("ASTC_3X3X3")]
    public async Task UnknownOrAmbiguousAstcTypesAreNotGuessed(string type)
    {
        string path = Path.Combine(_root, "sample.psb");
        byte[] original = PsbFixture.CreateWithEncodedResources(3, false,
            (_, w, h) => (type, ConstantBlocks(w, h, 4, 4, false)), (64, 32));
        await File.WriteAllBytesAsync(path, original);
        Assert.Null(PsbAstcFormat.ParseType(type));
        var result = await new PsbProcessor().ProcessWithFormatAsync(path, .5, .5, PsbTextureFormat.Dxt5);
        Assert.False(result.Changed); Assert.Contains("unsupported", result.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private static byte[] Fixture(bool mixed = false) => PsbFixture.CreateWithEncodedResources(3, false,
        (index, w, h) => index == 0 ? ("ASTC_8BPP", ConstantBlocks(w, h, 4, 4, true))
            : ("RGBA8", Enumerable.Range(0, w * h).SelectMany(_ => new byte[] { 40, 90, 180, 128 }).ToArray()),
        mixed ? [(64, 32), (32, 16)] : [(64, 32)]);

    private static byte[] ConstantBlocks(int width, int height, int blockWidth, int blockHeight, bool splitAlpha)
    {
        int columns = (width + blockWidth - 1) / blockWidth, rows = (height + blockHeight - 1) / blockHeight;
        byte[] payload = new byte[columns * rows * 16];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                Span<byte> block = payload.AsSpan((y * columns + x) * 16, 16);
                // LDR void-extent block: independent constant-color ASTC fixture.
                block[..8].Fill(255); block[0] = 0xfc; block[1] = 0xfd;
                BinaryPrimitives.WriteUInt16LittleEndian(block[8..], 180 * 257);
                BinaryPrimitives.WriteUInt16LittleEndian(block[10..], 90 * 257);
                BinaryPrimitives.WriteUInt16LittleEndian(block[12..], 40 * 257);
                BinaryPrimitives.WriteUInt16LittleEndian(block[14..], (ushort)(splitAlpha && x < columns / 2 ? 0 : 128 * 257));
            }
        return payload;
    }

    private sealed class Sink(List<ConversionProgress> events) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) { lock (events) events.Add(value); }
    }
}
