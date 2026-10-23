using System.Buffers.Binary;
using System.Text;

namespace Art3m1s.PsvTool.Core;

// This layer only validates/fragments the container. ImageSharp owns pixel decoding.
// Chunk payloads reference the input buffer; scanning does not copy compressed pixels.
internal sealed record PngChunk(string Type, ReadOnlyMemory<byte> Data, int Offset);

internal sealed record PngDocument(uint Width, uint Height, int BitDepth, int ColorType,
    IReadOnlyList<PngChunk> Chunks, ReadOnlyMemory<byte> ImageBytes, ReadOnlyMemory<byte> Trailer)
{
    internal static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    public ReadOnlyMemory<byte> Ihdr => Chunks[0].Data;
    public bool IsAnimated => Chunks.Any(static chunk => chunk.Type is "acTL" or "fcTL" or "fdAT");
    public bool HasProtectedMetadata => !Trailer.IsEmpty || Chunks.Any(static chunk =>
        chunk.Type is not ("IHDR" or "IDAT" or "IEND" or "PLTE" or "tRNS" or "gAMA" or "sRGB" or
            "cHRM" or "iCCP" or "pHYs" or "tIME" or "bKGD" or "sBIT"));

    public static bool HasSignature(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Signature);

    public static PngDocument Parse(byte[] bytes)
    {
        if (!HasSignature(bytes)) throw Invalid("signature", 0, "Not a PNG file.");
        List<PngChunk> chunks = [];
        int cursor = Signature.Length, paletteEntries = 0;
        uint width = 0, height = 0;
        int bits = 0, color = 0;
        bool hasData = false, dataEnded = false, hasTransparency = false;
        while (cursor < bytes.Length)
        {
            int offset = cursor;
            int remaining = bytes.Length - offset;
            string type = remaining >= 8 ? Encoding.ASCII.GetString(bytes, offset + 4, 4) : "header";
            if (remaining < 12) throw Invalid(type, offset, "Truncated PNG chunk header or CRC.");
            ReadOnlySpan<byte> typeBytes = bytes.AsSpan(offset + 4, 4);
            if (!IsChunkName(typeBytes)) throw Invalid("type", offset, "Invalid PNG chunk name.");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            // Compare before casting/adding: even 0x7fffffff and 0xffffffff cannot overflow.
            if (length > (uint)(remaining - 12)) throw Invalid(type, offset, "Truncated PNG chunk payload.");
            int count = (int)length;
            ReadOnlyMemory<byte> data = bytes.AsMemory(offset + 8, count);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + count, 4));
            if (PngCrc32.Compute(typeBytes, data.Span) != crc) throw Invalid(type, offset, "PNG CRC mismatch.");
            cursor = offset + count + 12;
            if (chunks.Count == 0 && type != "IHDR") throw Invalid(type, offset, "IHDR must be first.");
            switch (type)
            {
                case "IHDR":
                    if (chunks.Count != 0) throw Invalid(type, offset, "Duplicate IHDR.");
                    if (count != 13) throw Invalid(type, offset, "IHDR must contain 13 bytes.");
                    width = BinaryPrimitives.ReadUInt32BigEndian(data.Span);
                    height = BinaryPrimitives.ReadUInt32BigEndian(data.Span[4..]);
                    bits = data.Span[8]; color = data.Span[9];
                    bool validDepth = color switch
                    {
                        0 => bits is 1 or 2 or 4 or 8 or 16,
                        2 or 4 or 6 => bits is 8 or 16,
                        3 => bits is 1 or 2 or 4 or 8,
                        _ => false
                    };
                    if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
                        throw Invalid(type, offset, "Invalid PNG dimensions.");
                    if (!validDepth) throw Invalid(type, offset, "Invalid PNG color type/bit depth combination.");
                    if (data.Span[10] != 0 || data.Span[11] != 0 || data.Span[12] > 1)
                        throw Invalid(type, offset, "Invalid PNG compression, filter or interlace method.");
                    break;
                case "PLTE":
                    if (paletteEntries != 0 || hasData || hasTransparency || color is 0 or 4)
                        throw Invalid(type, offset, "Duplicate, misplaced or forbidden PLTE.");
                    if (count is 0 or > 768 || count % 3 != 0 || (color == 3 && count / 3 > 1 << bits))
                        throw Invalid(type, offset, "Invalid PNG palette size.");
                    paletteEntries = count / 3;
                    break;
                case "tRNS":
                    if (hasTransparency || hasData || color is 4 or 6 ||
                        (color == 3 && (paletteEntries == 0 || count > paletteEntries)) ||
                        (color == 0 && count != 2) || (color == 2 && count != 6))
                        throw Invalid(type, offset, "Invalid PNG transparency length, type or order.");
                    hasTransparency = true;
                    break;
                case "IDAT":
                    if (dataEnded) throw Invalid(type, offset, "IDAT chunks must be consecutive.");
                    if (color == 3 && paletteEntries == 0) throw Invalid(type, offset, "Indexed PNG is missing PLTE before IDAT.");
                    hasData = true;
                    break;
                case "IEND":
                    if (count != 0) throw Invalid(type, offset, "IEND must be empty.");
                    if (!hasData) throw Invalid(type, offset, "PNG is missing IDAT.");
                    chunks.Add(new(type, data, offset));
                    // Bytes after IEND belong to the application, not the PNG chunk stream.
                    return new(width, height, bits, color, chunks, bytes.AsMemory(0, cursor), bytes.AsMemory(cursor));
                default:
                    if ((typeBytes[0] & 32) == 0) throw Invalid(type, offset, "Unsupported critical PNG chunk.");
                    break;
            }
            if (hasData && type != "IDAT") dataEnded = true;
            chunks.Add(new(type, data, offset));
        }
        throw Invalid("IEND", cursor, "PNG is missing IEND.");
    }

    private static bool IsChunkName(ReadOnlySpan<byte> name)
    {
        foreach (byte value in name)
            if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')) return false;
        return true;
    }

    private static InvalidDataException Invalid(string type, int offset, string reason) =>
        new($"PNG chunk {type} at byte {offset}: {reason}");
}

internal static class PngCrc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in first) crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (byte value in second) crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
