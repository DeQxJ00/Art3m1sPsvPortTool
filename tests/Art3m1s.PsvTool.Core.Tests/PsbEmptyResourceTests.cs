using System.Buffers.Binary;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbEmptyResourceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmptyExtraResourcePointersCanAliasRegularTables(bool sharedEmptyTable, bool convertFormat)
    {
        string directory = Path.Combine(Path.GetTempPath(), "psb-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] original = WithEmptyExtraTables(sharedEmptyTable);
            string path = Path.Combine(directory, "sample09.psb");
            await File.WriteAllBytesAsync(path, original);
            var processor = new PsbProcessor();
            Assert.Equal(0, (await processor.InspectAsync(path)).ExtraResourceCount);
            var result = await processor.ProcessWithFormatAsync(path, .5, .5,
                convertFormat ? PsbTextureFormat.Dxt5 : null, dxt5Layout: PsbDxt5Layout.Swizzled);
            Assert.True(result.Changed);
            var inspection = await processor.InspectAsync(path);
            Assert.Equal(0, inspection.ExtraResourceCount);
            Assert.Equal(2, inspection.ResourceCount);
            var atlases = (await PsbTextureScanner.InspectFileAsync(path)).Atlases;
            Assert.Equal(new[] { (16, 8), (8, 4) }, atlases.Select(a => (a.Width, a.Height)));
            Assert.All(atlases, a => Assert.Equal(convertFormat ? "DXT5_SWIZZLED" : "RGBA8", a.Format));
            byte[] rebuilt = await File.ReadAllBytesAsync(path);
            Assert.Equal(Read(rebuilt, 24), Read(rebuilt, 52));
            Assert.Equal("RGBA8", PsbProcessor.InspectStringMetadata(rebuilt, "metadata", "formatDescription"));
            Assert.Equal(640, PsbProcessor.InspectNumericMetadata(rebuilt, "screenSize", "width"));
            foreach (var atlas in atlases)
            {
                byte[] pixels = PsbProcessor.InspectTextureResource(rebuilt, atlas.Source);
                if (convertFormat)
                    pixels = PsbProcessor.DecodeDxt5ForTest(PsbDxt5Storage.Unswizzle(pixels, atlas.Width, atlas.Height), atlas.Width, atlas.Height);
                Assert.Equal(atlas.Width * atlas.Height * 4, pixels.Length);
                Assert.InRange(pixels[0], (byte)(convertFormat ? 160 : 40), (byte)(convertFormat ? 200 : 40));
                Assert.Equal(255, pixels[3]);
            }
            // Repeated conversions must retain the empty-table aliases too.
            Assert.True((await processor.ProcessWithFormatAsync(path, .5, .5,
                convertFormat ? PsbTextureFormat.Dxt5 : null, dxt5Layout: PsbDxt5Layout.Swizzled)).Changed);
            Assert.Equal(0, (await processor.InspectAsync(path)).ExtraResourceCount);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] WithEmptyExtraTables(bool sharedEmptyTable)
    {
        byte[] source = PsbFixture.CreateWithOptions(4, true, (_, _) => (180, 90, 40, 255), (32, 16), (16, 8));
        int start = checked((int)Read(source, 24));
        // Empty arrays are three bytes; their data pointer need not own any bytes.
        byte[] tables = sharedEmptyTable ? [0x0d, 0, 0x0c] : [0x0d, 0, 0x0c, 0x0d, 0, 0x0c];
        byte[] result = new byte[source.Length + tables.Length];
        source.AsSpan(0, start).CopyTo(result);
        tables.CopyTo(result, start);
        source.AsSpan(start).CopyTo(result.AsSpan(start + tables.Length));
        foreach (int field in new[] { 24, 28, 32 }) Write(result, field, Read(source, field) + (uint)tables.Length);
        Write(result, 44, (uint)start);
        Write(result, 48, (uint)(sharedEmptyTable ? start : start + 3));
        Write(result, 52, Read(result, 24));
        uint a = 1, b = 0;
        foreach (byte value in result.AsSpan(8, 32).ToArray().Concat(result.AsSpan(44, 12).ToArray()))
        { a = (a + value) % 65521; b = (b + a) % 65521; }
        Write(result, 40, (b << 16) | a);
        return result;
    }

    private static uint Read(byte[] bytes, int field) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(field));
    private static void Write(byte[] bytes, int field, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), value);
}
