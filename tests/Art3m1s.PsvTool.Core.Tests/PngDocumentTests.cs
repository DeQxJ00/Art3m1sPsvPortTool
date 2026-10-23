using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PngDocumentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-png-document-" + Guid.NewGuid().ToString("N"));
    public PngDocumentTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { Directory.Delete(_root, true); }

    [Fact]
    public void ContainerReferencesSourceAndSeparatesTrailerWithoutParsingIt()
    {
        byte[] png = Encode(Parts());
        byte[] trailer = [0xff, 0xff, 0xff, 0xff, (byte)'I', (byte)'D', (byte)'A', (byte)'T', 1];
        byte[] input = [.. png, .. trailer];
        var document = PngDocument.Parse(input);
        Assert.Equal(png, document.ImageBytes.ToArray());
        Assert.Equal(trailer, document.Trailer.ToArray());
        Assert.True(document.HasProtectedMetadata);
        foreach (var chunk in document.Chunks)
        {
            Assert.True(MemoryMarshal.TryGetArray(chunk.Data, out var segment));
            Assert.Same(input, segment.Array);
            Assert.Equal(chunk.Offset + 8, segment.Offset);
        }
    }

    public static TheoryData<string> MalformedCases => new()
    {
        "crc", "truncated-crc", "length-uint-max", "length-int-max", "missing-iend", "nonempty-iend",
        "missing-idat", "ihdr-not-first", "duplicate-ihdr", "short-ihdr", "zero-width", "zero-height",
        "dimension-overflow", "invalid-depth", "invalid-color", "invalid-compression", "invalid-filter",
        "invalid-interlace", "unknown-critical", "invalid-name", "idat-interrupted", "missing-palette",
        "duplicate-palette", "late-palette", "gray-palette", "oversized-palette", "empty-palette",
        "fractional-palette", "transparency-with-alpha", "long-transparency", "late-transparency",
        "duplicate-transparency", "transparency-before-palette", "short-rgb-transparency"
    };

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public async Task ScanAndResizeSharePreciseErrorsWithoutChangingSource(string scenario)
    {
        byte[] bytes = Malformed(scenario);
        string path = Path.Combine(_root, "sample.png");
        await File.WriteAllBytesAsync(path, bytes);
        var parsed = Assert.Throws<InvalidDataException>(() => PngDocument.Parse(bytes));
        var scanned = Assert.Throws<InvalidDataException>(() => TextureScanner.InspectBytes(bytes, "image/bg/sample.png"));
        var resized = await Assert.ThrowsAsync<InvalidDataException>(() => new PngProcessor().ResizeAsync(path, .5));
        Assert.Equal(parsed.Message, scanned.Message);
        Assert.Equal(parsed.Message, resized.Message);
        Assert.Contains("PNG chunk ", parsed.Message);
        Assert.Contains(" at byte ", parsed.Message);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 4)]
    [InlineData(0, 8)]
    [InlineData(0, 16)]
    [InlineData(2, 8)]
    [InlineData(2, 16)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(4, 16)]
    [InlineData(6, 8)]
    [InlineData(6, 16)]
    public async Task LegalColorDepthsAndAdam7RetainStorageFormat(int color, int depth)
    {
        string path = Path.Combine(_root, "sample.png");
        using (var image = new Image<Rgba64>(16, 8, new Rgba64(10000, 20000, 30000, 40000)))
            image.Save(path, new PngEncoder
            {
                ColorType = (PngColorType)color,
                BitDepth = (PngBitDepth)depth,
                InterlaceMethod = PngInterlaceMode.Adam7
            });
        byte[] original = await File.ReadAllBytesAsync(path);
        var before = PngDocument.Parse(original);
        Assert.Equal(depth, before.BitDepth); Assert.Equal(color, before.ColorType);
        Assert.Equal(1, before.Ihdr.Span[12]);
        Assert.Equal(16, TextureScanner.InspectBytes(original, "image/bg/sample.png").Width);
        await new PngProcessor().ResizeAsync(path, .5);
        byte[] result = await File.ReadAllBytesAsync(path);
        var after = PngDocument.Parse(result);
        Assert.Equal(before.BitDepth, after.BitDepth); Assert.Equal(before.ColorType, after.ColorType);
        Assert.Equal(8u, after.Width); Assert.Equal(4u, after.Height);
        foreach (var chunk in before.Chunks.Where(c => c.Type is "PLTE" or "tRNS"))
            Assert.Equal(chunk.Data.ToArray(), after.Chunks.Single(c => c.Type == chunk.Type).Data.ToArray());
        using var decoded = Image.Load(result);
        Assert.Equal(8, decoded.Width); Assert.Equal(4, decoded.Height);
    }

    [Fact]
    public async Task UnknownAncillaryChunksAndConsecutiveIdatsRemainSupported()
    {
        var parts = Parts();
        byte[] compressed = parts[1].Data;
        parts[1] = ("IDAT", compressed[..(compressed.Length / 2)]);
        parts.Insert(2, ("IDAT", compressed[(compressed.Length / 2)..]));
        parts.Insert(2, ("IDAT", []));
        parts.Insert(1, ("vpAg", new byte[] { 7, 4, 1, 9 }));
        parts.Insert(parts.Count - 1, ("tEXt", Encoding.Latin1.GetBytes("offset\0" + "12,24")));
        string path = Path.Combine(_root, "sample.png");
        byte[] original = Encode(parts);
        await File.WriteAllBytesAsync(path, original);
        Assert.True(TextureScanner.InspectBytes(original, "image/bg/sample.png").HasMetadata);
        await new PngProcessor().ResizeAsync(path, .5);
        var document = PngDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(new byte[] { 7, 4, 1, 9 }, document.Chunks.Single(c => c.Type == "vpAg").Data.ToArray());
        Assert.Equal("offset\0" + "6,12", Encoding.Latin1.GetString(document.Chunks.Single(c => c.Type == "tEXt").Data.Span));
        Assert.Equal(4u, document.Width); Assert.Equal(2u, document.Height);
    }

    [Fact]
    public async Task EmptyIndexedTransparencyIsRetainedWithoutAddingAlphaEntries()
    {
        using MemoryStream encoded = new();
        using (var image = new Image<Rgb24>(8, 4, new Rgb24(40, 80, 120)))
            image.Save(encoded, new PngEncoder { ColorType = PngColorType.Palette, BitDepth = PngBitDepth.Bit1 });
        var document = PngDocument.Parse(encoded.ToArray());
        var parts = document.Chunks.Where(c => c.Type != "tRNS").Select(c => (c.Type, Data: c.Data.ToArray())).ToList();
        parts.Insert(parts.FindIndex(c => c.Type == "IDAT"), ("tRNS", []));
        string path = Path.Combine(_root, "sample.png");
        await File.WriteAllBytesAsync(path, Encode(parts));
        Assert.Equal(8, TextureScanner.InspectBytes(await File.ReadAllBytesAsync(path), "image/bg/sample.png").Width);
        await new PngProcessor().ResizeAsync(path, .5);
        var resized = PngDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Empty(resized.Chunks.Single(c => c.Type == "tRNS").Data.ToArray());
        Assert.Equal(3, resized.ColorType); Assert.Equal(1, resized.BitDepth);
    }

    [Fact]
    public async Task AnimatedPngCannotBeSilentlyRewrittenWithStaleFrameData()
    {
        var parts = Parts();
        parts.Insert(1, ("acTL", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 }));
        byte[] original = Encode(parts);
        string path = Path.Combine(_root, "sample.png");
        await File.WriteAllBytesAsync(path, original);
        Assert.True(PngDocument.Parse(original).IsAnimated);
        await Assert.ThrowsAsync<NotSupportedException>(() => new PngProcessor().ResizeAsync(path, .5));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    private static byte[] Malformed(string scenario)
    {
        var parts = Parts();
        byte[] header = parts[0].Data;
        switch (scenario)
        {
            case "missing-iend": parts.RemoveAt(parts.Count - 1); break;
            case "nonempty-iend": parts[^1] = ("IEND", [1]); break;
            case "missing-idat": parts.RemoveAt(1); break;
            case "ihdr-not-first": parts.Insert(0, ("tEXt", [0])); break;
            case "duplicate-ihdr": parts.Insert(1, ("IHDR", header)); break;
            case "short-ihdr": parts[0] = ("IHDR", header[..12]); break;
            case "zero-width": header.AsSpan(0, 4).Clear(); break;
            case "zero-height": header.AsSpan(4, 4).Clear(); break;
            case "dimension-overflow": BinaryPrimitives.WriteUInt32BigEndian(header, uint.MaxValue); break;
            case "invalid-depth": header[8] = 4; break;
            case "invalid-color": header[9] = 5; break;
            case "invalid-compression": header[10] = 1; break;
            case "invalid-filter": header[11] = 1; break;
            case "invalid-interlace": header[12] = 2; break;
            case "unknown-critical": parts.Insert(1, ("ABCD", [])); break;
            case "invalid-name": parts.Insert(1, ("a1Cd", [])); break;
            case "idat-interrupted": parts.Insert(2, ("tEXt", [0])); parts.Insert(3, ("IDAT", [])); break;
            case "missing-palette": header[8] = 1; header[9] = 3; break;
            case "duplicate-palette": parts.Insert(1, ("PLTE", [0, 0, 0])); parts.Insert(2, ("PLTE", [1, 1, 1])); break;
            case "late-palette": parts.Insert(2, ("PLTE", [0, 0, 0])); break;
            case "gray-palette": header[9] = 0; parts.Insert(1, ("PLTE", [0, 0, 0])); break;
            case "empty-palette": parts.Insert(1, ("PLTE", [])); break;
            case "fractional-palette": parts.Insert(1, ("PLTE", [1, 2])); break;
            case "oversized-palette": header[8] = 1; header[9] = 3; parts.Insert(1, ("PLTE", new byte[9])); break;
            case "transparency-with-alpha": header[9] = 6; parts.Insert(1, ("tRNS", new byte[6])); break;
            case "long-transparency": header[8] = 1; header[9] = 3; parts.Insert(1, ("PLTE", [0, 0, 0])); parts.Insert(2, ("tRNS", [0, 255])); break;
            case "late-transparency": parts.Insert(2, ("tRNS", new byte[6])); break;
            case "duplicate-transparency": parts.Insert(1, ("tRNS", new byte[6])); parts.Insert(2, ("tRNS", new byte[6])); break;
            case "transparency-before-palette": header[8] = 1; header[9] = 3; parts.Insert(1, ("tRNS", [0])); parts.Insert(2, ("PLTE", [0, 0, 0])); break;
            case "short-rgb-transparency": parts.Insert(1, ("tRNS", [0])); break;
        }
        byte[] encoded = Encode(parts);
        const int idatOffset = 33;
        if (scenario == "crc") encoded[^13] ^= 1;
        if (scenario == "truncated-crc") return encoded[..^2];
        if (scenario is "length-uint-max" or "length-int-max")
            BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(idatOffset), scenario == "length-uint-max" ? uint.MaxValue : int.MaxValue);
        return encoded;
    }

    private static List<(string Type, byte[] Data)> Parts()
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 8); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 4);
        header[8] = 8; header[9] = 2;
        using MemoryStream stream = new();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, true)) zlib.Write(new byte[4 * 25]);
        return [("IHDR", header), ("IDAT", stream.ToArray()), ("IEND", [])];
    }

    private static byte[] Encode(List<(string Type, byte[] Data)> parts)
    {
        using MemoryStream stream = new();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> value = stackalloc byte[4];
        foreach (var (type, data) in parts)
        {
            byte[] name = Encoding.ASCII.GetBytes(type);
            BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
            stream.Write(value); stream.Write(name); stream.Write(data);
            // Bitwise reference implementation independent of the production CRC table.
            uint crc = uint.MaxValue;
            foreach (byte item in name.Concat(data))
            {
                crc ^= item;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
            }
            BinaryPrimitives.WriteUInt32BigEndian(value, ~crc); stream.Write(value);
        }
        return stream.ToArray();
    }
}
