using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psb-formats-" + Guid.NewGuid().ToString("N"));
    public PsbFormatTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Linear)]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_4, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, PsbDxt5Layout.Swizzled)]
    public async Task FormatsConvertLooseAndArchivedMultiAtlasPsbWithoutChangingInput(PsbTextureFormat format, PsbDxt5Layout layout)
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        byte[] original = PsbFixture.Create((64, 32), (32, 16));
        string source = Path.Combine(input, "hero.psb"); await File.WriteAllBytesAsync(source, original);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("image/hero.psb"), "image/hero.psb", 0,
            (uint)original.Length, source)]), Path.Combine(input, "root.pfs.010"));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: AssetCategories.Animation, PsbOutputFormat: format, Dxt5Layout: layout));
        var archive = await codec.ExtractAsync(Path.Combine(output, "root.pfs.010"), Path.Combine(_root, "unpacked"));
        foreach (string path in new[] { Path.Combine(output, "hero.psb"), Assert.Single(archive.Entries).ExtractedPath })
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            var inspection = await PsbTextureScanner.InspectFileAsync(path);
            Assert.Equal(new[] { (32, 16), (16, 8) }, inspection.Atlases.Select(x => (x.Width, x.Height)));
            Assert.All(inspection.Atlases, atlas => Assert.Equal(PsbTextureFormats.TypeName(format, layout), atlas.Format));
            Assert.Equal("RGBA8", PsbProcessor.InspectStringMetadata(bytes, "metadata", "formatDescription"));
            foreach (var atlas in inspection.Atlases)
            {
                byte[] payload = PsbProcessor.InspectTextureResource(bytes, atlas.Source);
                if (format == PsbTextureFormat.Dxt5 && layout == PsbDxt5Layout.Swizzled)
                {
                    Assert.Equal(PsbDxt5Storage.SwizzledBytes(atlas.Width, atlas.Height), payload.Length);
                    payload = PsbDxt5Storage.Unswizzle(payload, atlas.Width, atlas.Height);
                }
                Assert.Equal(NativeTextureFormats.PayloadBytes(PsbTextureFormats.NativeFormat(format), atlas.Width, atlas.Height), payload.Length);
                byte[] decoded = format == PsbTextureFormat.Dxt5 ? PsbProcessor.DecodeDxt5ForTest(payload, atlas.Width, atlas.Height)
                    : PvrTextureCodec.Decode(payload, atlas.Width, atlas.Height, PsbTextureFormats.NativeFormat(format));
                Assert.InRange(decoded[0], (byte)160, (byte)200);
                Assert.InRange(decoded[1], (byte)70, (byte)110);
                Assert.InRange(decoded[2], (byte)20, (byte)60);
                Assert.InRange(decoded[3], (byte)245, byte.MaxValue);
            }
            Assert.Equal(640, PsbProcessor.InspectNumericMetadata(bytes, "screenSize", "width"));
            Assert.Equal(8, PsbProcessor.InspectNumericMetadata(bytes, "source", "atlas0", "icon", "piece0", "originX"));
            Assert.Equal(50, PsbProcessor.InspectNumericMetadata(bytes, "object", "hero", "motion", "idle", "layer", "0", "frameList", "0", "content", "coord", "0"));
            uint a = 1, b = 0;
            foreach (byte value in bytes.AsSpan(8, 32)) { a = (a + value) % 65521; b = (b + a) % 65521; }
            Assert.Equal((b << 16) | a, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));
            await new PsbProcessor().InspectAsync(path);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData(PsbTextureFormat.Pvrtc2_4, 17, 11)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, 17, 11)]
    [InlineData(PsbTextureFormat.Pvrtc2_4, 1, 1)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, 1, 1)]
    public async Task SmallAndNonBlockAlignedAtlasesUseRoundedStorageButKeepLogicalDimensions(PsbTextureFormat format, int width, int height)
    {
        string path = Path.Combine(_root, "odd.psb");
        await File.WriteAllBytesAsync(path, PsbFixture.Create((width, height)));
        var processor = new PsbProcessor();
        Assert.True((await processor.ProcessWithFormatAsync(path, 1, 1, format)).Changed);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        var info = await PsbTextureScanner.InspectFileAsync(path);
        Assert.Equal(width, info.Width); Assert.Equal(height, info.Height);
        byte[] payload = PsbProcessor.InspectTextureResource(bytes, "atlas0");
        Assert.Equal(NativeTextureFormats.PayloadBytes(PsbTextureFormats.NativeFormat(format), width, height), payload.Length);
        Assert.Equal(width * height * 4, PvrTextureCodec.Decode(payload, width, height, PsbTextureFormats.NativeFormat(format)).Length);
        Assert.False((await processor.ProcessWithFormatAsync(path, 1, 1, format)).Changed);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(PsbTextureFormat.Pvrtc2_4)]
    [InlineData(PsbTextureFormat.Pvrtc2_2)]
    public async Task AlphaAndColorSurviveDxt5ToPvrtcAndBack(PsbTextureFormat format)
    {
        string path = Path.Combine(_root, "alpha.psb");
        await File.WriteAllBytesAsync(path, PsbFixture.CreateWithPixels((x, _) => (180, 90, 40, x < 32 ? (byte)0 : (byte)128), (64, 32)));
        var processor = new PsbProcessor();
        await processor.ProcessAsync(path, 1, true);
        await processor.ProcessWithFormatAsync(path, 1, 1, format);
        byte[] payload = PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path), "atlas0");
        byte[] decoded = PvrTextureCodec.Decode(payload, 64, 32, PsbTextureFormats.NativeFormat(format));
        Assert.InRange(decoded[(16 * 64 + 8) * 4 + 3], (byte)0, (byte)20);
        Assert.InRange(decoded[(16 * 64 + 48) * 4 + 3], (byte)110, (byte)145);
        await processor.ProcessWithFormatAsync(path, .5, .5, PsbTextureFormat.Dxt5);
        Assert.Equal("DXT5", Assert.Single((await PsbTextureScanner.InspectFileAsync(path)).Atlases).Format);
        Assert.Equal(512, PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path), "atlas0").Length);
    }

    [Fact]
    public async Task DisabledFormatConversionPreservesSourceFormatAndDefaultIsBc3()
    {
        Assert.Equal(PsbTextureFormat.Dxt5, new ConversionOptions(_root, _root + "-output").PsbOutputFormat);
        string path = Path.Combine(_root, "keep.psb"); byte[] original = PsbFixture.Create((64, 32));
        await File.WriteAllBytesAsync(path, original);
        var processor = new PsbProcessor();
        Assert.False((await processor.ProcessWithFormatAsync(path, 1, 1, null)).Changed);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        await processor.ProcessWithFormatAsync(path, .5, .5, null);
        Assert.Equal("RGBA8", Assert.Single((await PsbTextureScanner.InspectFileAsync(path)).Atlases).Format);
        byte[] preserved = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessWithFormatAsync(path, 1, 1, (PsbTextureFormat)999));
        Assert.Equal(preserved, await File.ReadAllBytesAsync(path));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions(_root, _root + "-output", PsbOutputFormat: (PsbTextureFormat)999).Validate());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CompactTablesRebuildAcrossPsbVersionsAndGrowingResourceLengths(int version)
    {
        string path = Path.Combine(_root, "compact.psb");
        await File.WriteAllBytesAsync(path, PsbFixture.CreateWithOptions((ushort)version, true, (_, _) => (180, 90, 40, 128), (32, 16), (16, 8)));
        var processor = new PsbProcessor();
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Pvrtc2_2);
        await processor.ProcessWithFormatAsync(path, 1, 1, PsbTextureFormat.Dxt5);
        var inspection = await processor.InspectAsync(path);
        Assert.Equal((ushort)version, inspection.Version); Assert.Equal(2, inspection.ResourceCount);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal("RGBA8", PsbProcessor.InspectStringMetadata(bytes, "metadata", "formatDescription"));
        Assert.Equal(512, PsbProcessor.InspectTextureResource(bytes, "atlas0").Length);
        Assert.Equal(128, PsbProcessor.InspectTextureResource(bytes, "atlas1").Length);
        Assert.All((await PsbTextureScanner.InspectFileAsync(path)).Atlases, atlas => Assert.Equal("DXT5", atlas.Format));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
