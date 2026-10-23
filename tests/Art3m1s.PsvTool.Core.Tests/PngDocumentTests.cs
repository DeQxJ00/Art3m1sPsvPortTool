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

    [Fact]
    public void BadIccProfilesAreRemovedOnlyFromDecoderInputAndKeepTrailerAndSourceOffsets()
    {
        var parts = Parts();
        parts.Insert(1, ("iCCP", new byte[] { 1, 2, 3 }));
        parts.Insert(2, ("tEXt", Encoding.Latin1.GetBytes("note\0synthetic")));
        parts.Insert(3, ("iCCP", new byte[] { 4, 5 }));
        byte[] bytes = CorruptCrc(Encode(parts), "iCCP");
        byte[] input = [.. bytes, 0x91, 0x92, 0x93];
        byte[] original = input.ToArray();
        var document = PngDocument.Parse(input);
        Assert.Equal(2, document.IgnoredIccProfileOffsets.Count);
        Assert.DoesNotContain(document.Chunks, c => c.Type == "iCCP");
        Assert.Equal(Encode(parts.Where(p => p.Type != "iCCP").ToList()), document.ImageBytes.ToArray());
        Assert.Equal(new byte[] { 0x91, 0x92, 0x93 }, document.Trailer.ToArray());
        foreach (var chunk in document.Chunks)
        {
            Assert.True(MemoryMarshal.TryGetArray(chunk.Data, out var segment));
            Assert.Same(input, segment.Array);
            Assert.Equal(chunk.Offset + 8, segment.Offset);
        }
        Assert.Equal(original, input);
        using var decoded = Image.Load(document.ImageBytes.Span);
        Assert.Equal(8, decoded.Width);
        Assert.Empty(PngDocument.Parse(document.ImageBytes.ToArray()).IgnoredIccProfileOffsets);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 16)]
    [InlineData(2, 8)]
    [InlineData(6, 8)]
    [InlineData(4, 8)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    public async Task BrokenIccProfileCanBeScannedAndResizedWithoutChangingStorageFormat(int color, int depth)
    {
        using MemoryStream stream = new();
        using (var image = new Image<Rgba32>(16, 8, new Rgba32(45, 90, 135, 170)))
            image.Save(stream, new PngEncoder { ColorType = (PngColorType)color, BitDepth = (PngBitDepth)depth });
        var before = PngDocument.Parse(stream.ToArray());
        var parts = before.Chunks.Select(c => (c.Type, c.Data.ToArray())).ToList();
        parts.Insert(1, ("iCCP", new byte[] { 0x7f, 0, 0, 0xff }));
        byte[] input = [.. CorruptCrc(Encode(parts), "iCCP"), 0x91, 0x92];
        string path = Path.Combine(_root, "sample.png");
        await File.WriteAllBytesAsync(path, input);
        var scanned = TextureScanner.InspectBytes(input, "image/rule/sample.png");
        Assert.Null(scanned.Error);
        Assert.Equal(16, scanned.Width);
        Assert.Single(scanned.IgnoredIccProfileOffsets);
        Assert.Equal(input, await File.ReadAllBytesAsync(path));
        List<ConversionProgress> events = [];
        var images = await new TextureScanner().ScanAsync(_root, progress: new IccProgress(events));
        Assert.Null(Assert.Single(images).Error);
        var diagnostic = Assert.Single(events, e => e.Diagnostic?.Code == "PNG_ICCP_CRC_IGNORED");
        Assert.Equal("sample.png", diagnostic.Entry);
        Assert.Equal(ConversionLogLevel.Warning, diagnostic.Diagnostic!.Level);
        await new PngProcessor().ResizeAsync(path, .6);
        var after = PngDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(9u, after.Width); Assert.Equal(4u, after.Height);
        Assert.Equal(color, after.ColorType); Assert.Equal(depth, after.BitDepth);
        Assert.Empty(after.IgnoredIccProfileOffsets);
        Assert.DoesNotContain(after.Chunks, c => c.Type == "iCCP");
        Assert.Equal(new byte[] { 0x91, 0x92 }, after.Trailer.ToArray());
        foreach (var chunk in before.Chunks.Where(c => c.Type is "PLTE" or "tRNS"))
            Assert.Equal(chunk.Data.ToArray(), after.Chunks.Single(c => c.Type == chunk.Type).Data.ToArray());
        using var decoded = Image.Load(after.ImageBytes.Span);
        Assert.Equal(9, decoded.Width);
    }

    [Theory]
    [InlineData("IHDR")]
    [InlineData("IDAT")]
    [InlineData("IEND")]
    [InlineData("PLTE")]
    [InlineData("tRNS")]
    [InlineData("tEXt")]
    [InlineData("gAMA")]
    public void BadCrcInOtherChunksStillFails(string type)
    {
        var parts = Parts();
        if (parts.All(p => p.Type != type)) parts.Insert(1, (type, new byte[] { 0, 0, 0, 0, 0, 0 }));
        var error = Assert.Throws<InvalidDataException>(() => PngDocument.Parse(CorruptCrc(Encode(parts), type)));
        Assert.Contains($"PNG chunk {type} ", error.Message);
        Assert.Contains("PNG CRC mismatch", error.Message);
    }

    [Fact]
    public void ValidIccCrcIsPreservedAndBadIccCannotHideStructuralErrors()
    {
        var parts = Parts();
        parts.Insert(1, ("iCCP", new byte[] { 1, 0, 0, 0 }));
        byte[] valid = Encode(parts);
        var document = PngDocument.Parse(valid);
        Assert.Equal(valid, document.ImageBytes.ToArray());
        Assert.Empty(document.IgnoredIccProfileOffsets);
        Assert.Equal(parts[1].Data, document.Chunks[1].Data.ToArray());
        parts.Insert(0, ("iCCP", new byte[] { 1 }));
        Assert.Contains("IHDR must be first", Assert.Throws<InvalidDataException>(() =>
            PngDocument.Parse(CorruptCrc(Encode(parts), "iCCP"))).Message);
        parts = Parts();
        parts.Insert(2, ("iCCP", new byte[] { 1 }));
        parts.Insert(3, ("IDAT", []));
        Assert.Contains("IDAT chunks must be consecutive", Assert.Throws<InvalidDataException>(() =>
            PngDocument.Parse(CorruptCrc(Encode(parts), "iCCP"))).Message);
        byte[] truncated = CorruptCrc(valid, "iCCP")[..44];
        Assert.Contains("Truncated PNG chunk", Assert.Throws<InvalidDataException>(() => PngDocument.Parse(truncated)).Message);
    }

    [Fact]
    public async Task OptionalLocalIccFixturesScanAndResizeWithoutChangingOriginals()
    {
        string? directory = Environment.GetEnvironmentVariable("ART3M1S_LOCAL_ICCP_PNG_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        string[] fixtures = Directory.GetFiles(directory, "*.png");
        Assert.NotEmpty(fixtures);
        foreach (string fixture in fixtures)
        {
            byte[] original = await File.ReadAllBytesAsync(fixture);
            var before = PngDocument.Parse(original);
            Assert.NotEmpty(before.IgnoredIccProfileOffsets);
            Assert.Null(TextureScanner.InspectBytes(original, "image/rule/sample.png").Error);
            string copy = Path.Combine(_root, Path.GetFileName(fixture));
            await File.WriteAllBytesAsync(copy, original);
            await new PngProcessor().ResizeAsync(copy, .6);
            var after = PngDocument.Parse(await File.ReadAllBytesAsync(copy));
            Assert.Equal(Math.Max(1u, (uint)(before.Width * .6)), after.Width);
            Assert.Equal(Math.Max(1u, (uint)(before.Height * .6)), after.Height);
            Assert.Equal(before.ColorType, after.ColorType); Assert.Equal(before.BitDepth, after.BitDepth);
            Assert.Empty(after.IgnoredIccProfileOffsets);
            var validProfiles = before.Chunks.Where(c => c.Type == "iCCP").ToArray();
            var remainingProfiles = after.Chunks.Where(c => c.Type == "iCCP").ToArray();
            Assert.Equal(validProfiles.Length, remainingProfiles.Length);
            foreach (var (originalProfile, remainingProfile) in validProfiles.Zip(remainingProfiles))
                Assert.Equal(originalProfile.Data.ToArray(), remainingProfile.Data.ToArray());
            foreach (var chunk in before.Chunks.Where(c => c.Type is "PLTE" or "tRNS"))
                Assert.Equal(chunk.Data.ToArray(), after.Chunks.Single(c => c.Type == chunk.Type).Data.ToArray());
            Assert.Equal(original, await File.ReadAllBytesAsync(fixture));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionRecoversArchivedAndLoosePngWithOrWithoutTextureScanning(bool nativeTextures)
    {
        var parts = Parts();
        parts.Insert(1, ("iCCP", new byte[] { 0x7f, 0, 0, 0xff }));
        byte[] original = CorruptCrc(Encode(parts), "iCCP");
        string payload = Path.Combine(_root, "payload.png");
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        await File.WriteAllBytesAsync(payload, original);
        await File.WriteAllBytesAsync(Path.Combine(input, "loose.png"), original);
        var codec = new PfsCodec();
        string archive = Path.Combine(input, "root.pfs");
        const string entry = "image/rule/sample.png";
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes(entry), entry, 0, (uint)original.Length, payload)]), archive);
        byte[] originalArchive = await File.ReadAllBytesAsync(archive);
        List<ConversionProgress> events = [];
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .6, Categories: AssetCategories.Images,
            NativeTextures: new(Enabled: nativeTextures)), new IccProgress(events));
        var warnings = events.Where(e => e.Diagnostic?.Code == "PNG_ICCP_CRC_IGNORED").ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, e => Assert.Equal(ConversionLogLevel.Warning, e.Diagnostic!.Level));
        Assert.Contains(warnings, e => e.Archive == "root.pfs" && e.Entry!.Replace('\\', '/') == entry);
        Assert.Contains(warnings, e => e.Archive is null && e.Entry == "loose.png");
        Assert.DoesNotContain(events, e => e.Diagnostic?.Level == ConversionLogLevel.Error);
        var rebuilt = await codec.ExtractAsync(Path.Combine(output, "root.pfs"), Path.Combine(_root, "verify"));
        foreach (string path in new[] { Assert.Single(rebuilt.Entries).ExtractedPath, Path.Combine(output, "loose.png") })
        {
            var document = PngDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.Equal(4u, document.Width); Assert.Equal(2u, document.Height);
            Assert.DoesNotContain(document.Chunks, c => c.Type == "iCCP");
        }
        Assert.Equal(originalArchive, await File.ReadAllBytesAsync(archive));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(input, "loose.png")));
    }

    private sealed class IccProgress(List<ConversionProgress> events) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) { lock (events) events.Add(value); }
    }

    private static byte[] CorruptCrc(byte[] bytes, string type)
    {
        byte[] result = bytes.ToArray();
        for (int offset = 8; offset < result.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(offset)));
            if (Encoding.ASCII.GetString(result, offset + 4, 4) == type) result[offset + 8 + length] ^= 1;
            offset += length + 12;
        }
        return result;
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
