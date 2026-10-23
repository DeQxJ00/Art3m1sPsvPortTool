using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PngTrailerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-png-trailer-" + Guid.NewGuid().ToString("N"));
    public PngTrailerTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { Directory.Delete(_root, true); }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(2, 8)]
    [InlineData(6, 8)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    public async Task ScanStopsAtIendAndResizeRetainsOpaqueTrailer(int colorType, int bits)
    {
        byte[] png = MakePng(colorType, bits);
        byte[] trailer = Enumerable.Range(0, 184).Select(i => (byte)(i * 37 + 28)).ToArray();
        byte[] source = [.. png, .. trailer];
        var info = TextureScanner.InspectBytes(source, "image/bg/sample.png");
        Assert.True(info.HasMetadata);
        Assert.Equal(16, info.Width); Assert.Equal(8, info.Height);
        Assert.Equal(NativeTextureFormat.Preserve, NativeTextureFormats.Recommend(info, false));
        string path = Path.Combine(_root, "sample.png");
        await File.WriteAllBytesAsync(path, source);
        await new PngProcessor().ResizeAsync(path, .5);
        byte[] output = await File.ReadAllBytesAsync(path);
        Assert.True(output.AsSpan().EndsWith(trailer));
        Assert.Equal(bits, output[24]); Assert.Equal(colorType, output[25]);
        if (colorType == 3)
        {
            Assert.Equal(Chunk(png, "PLTE"), Chunk(output, "PLTE"));
            Assert.Equal(Chunk(png, "tRNS"), Chunk(output, "tRNS"));
        }
        using Image image = Image.Load(output);
        Assert.Equal(8, image.Width); Assert.Equal(4, image.Height);
        Assert.True(TextureScanner.InspectBytes(output, "image/bg/sample.png").HasMetadata);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void IendStopsBeforeTrailingChunkLengthOverflow(int value)
    {
        byte[] data = [.. MakePng(6, 8), .. Enumerable.Repeat((byte)value, 184)];
        var info = TextureScanner.InspectBytes(data, "image/bg/sample.png");
        Assert.True(info.HasMetadata);
        Assert.Equal(16, info.Width); Assert.Equal(8, info.Height);
    }

    [Fact]
    public void TruncatedChunkBeforeIendStillFails()
    {
        byte[] png = MakePng(3, 2);
        int pos = Find(png, "IDAT");
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(pos), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => TextureScanner.InspectBytes(png, "image/bg/sample.png"));
    }

    private static byte[] MakePng(int colorType, int bits)
    {
        if (colorType != 3)
        {
            using MemoryStream stream = new();
            using var image = new Image<Rgba32>(16, 8, new Rgba32(160, 80, 40, 180));
            image.Save(stream, new PngEncoder { ColorType = (PngColorType)colorType, BitDepth = PngBitDepth.Bit8 });
            return stream.ToArray();
        }
        using MemoryStream result = new();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 16); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 8);
        header[8] = (byte)bits; header[9] = 3;
        Write(result, "IHDR", header); Write(result, "PLTE", [255, 0, 0, 0, 255, 0]); Write(result, "tRNS", [255, 80]);
        int rowSize = (16 * bits + 7) / 8;
        byte[] raw = new byte[8 * (rowSize + 1)];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 16; x++)
            raw[y * (rowSize + 1) + 1 + x * bits / 8] |= (byte)(((x + y) % 2) << (8 - bits - x * bits % 8));
        using MemoryStream compressed = new();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write(raw);
        Write(result, "IDAT", compressed.ToArray()); Write(result, "IEND", []);
        return result.ToArray();
    }

    private static int Find(byte[] png, string type)
    {
        for (int pos = 8; pos <= png.Length - 12; pos += checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos))) + 12)
            if (Encoding.ASCII.GetString(png, pos + 4, 4) == type) return pos;
        throw new InvalidDataException("Missing test chunk.");
    }

    private static byte[] Chunk(byte[] png, string type)
    {
        int pos = Find(png, type);
        return png.AsSpan(pos + 8, (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos))).ToArray();
    }

    private static void Write(Stream stream, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); stream.Write(number); stream.Write(name); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in name.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
}
