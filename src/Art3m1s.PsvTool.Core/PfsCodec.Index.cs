using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Art3m1s.PsvTool.Core;

public sealed record PfsIndexEntry(string Path, uint Offset, uint Size);
public sealed record PfsIndex(IReadOnlyList<PfsIndexEntry> Entries, byte[]? XorKey);

public sealed partial class PfsCodec
{
    public async Task<PfsIndex> ReadIndexAsync(string path, PfsNameEncoding encoding = PfsNameEncoding.Auto,
        CancellationToken token = default)
    {
        await using FileStream input = File.OpenRead(path);
        byte[] header = new byte[11];
        await input.ReadExactlyAsync(header, token);
        if (header[0] != 'p' || header[1] != 'f' || header[2] is not ((byte)'2' or (byte)'6' or (byte)'8'))
            throw new InvalidDataException($"Unsupported PFS header: {path}");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(7));
        if (size < 4 || size > 64 * 1024 * 1024 || 7L + size > input.Length || count > 10_000_000)
            throw new InvalidDataException("Invalid PFS index bounds.");
        byte[] index = new byte[size]; header.AsSpan(7, 4).CopyTo(index);
        await input.ReadExactlyAsync(index.AsMemory(4), token);
        List<PfsIndexEntry> entries = [];
        int cursor = 4;
        for (uint i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            EnsureAvailable(index, cursor, 4);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(cursor)); cursor += 4;
            if (length == 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid PFS name length.");
            EnsureAvailable(index, cursor, checked((int)length + 12));
            string name = NormalizeArchivePath(DecodeName(index.AsSpan(cursor, (int)length).ToArray(), encoding));
            cursor += (int)length + 4;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(cursor));
            uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(cursor + 4)); cursor += 8;
            if ((ulong)offset + bytes > (ulong)input.Length) throw new InvalidDataException("PFS entry out of bounds.");
            entries.Add(new(name, offset, bytes));
        }
        return new(entries, header[2] == '8' ? SHA1.HashData(index) : null);
    }

    public static async Task<byte[]> ReadIndexedEntryAsync(FileStream input, PfsIndexEntry entry, byte[]? key,
        CancellationToken token = default)
    {
        if (entry.Size > 64 * 1024 * 1024) throw new InvalidDataException("Image exceeds 64 MiB scan limit.");
        byte[] data = new byte[entry.Size]; input.Position = entry.Offset;
        await input.ReadExactlyAsync(data, token);
        if (key != null) for (int i = 0; i < data.Length; i++) data[i] ^= key[i % key.Length];
        return data;
    }
}
