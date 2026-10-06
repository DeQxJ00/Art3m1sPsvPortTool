using System.Buffers.Binary;

namespace Art3m1s.PsvTool.Core;

public sealed record PsbAtlasInfo(string Source, int Width, int Height, string Format);

public sealed record PsbTextureInspection(bool IsEmoteMotion, IReadOnlyList<PsbAtlasInfo> Atlases)
{
    public PsbAtlasInfo? LargestAtlas => Atlases.OrderByDescending(x => (long)x.Width * x.Height)
        .ThenByDescending(x => x.Width).ThenByDescending(x => x.Height).FirstOrDefault();
    public int Width => LargestAtlas?.Width ?? 0;
    public int Height => LargestAtlas?.Height ?? 0;
}

public sealed record PsbTextureFileInfo(string? Archive, string Path, long Bytes,
    PsbTextureInspection? Inspection, string? Error = null)
{
    public int Width => Inspection?.Width ?? 0;
    public int Height => Inspection?.Height ?? 0;
}

public sealed record PsbResolutionRule(int Width, int Height, double Ratio = 0.5);

public sealed record PsbTextureOptions(IReadOnlyList<PsbResolutionRule>? Rules = null, bool Enabled = false,
    bool CompensateRendering = true)
{
    public double RatioFor(int width, int height) => Rules?.FirstOrDefault(x => x.Width == width && x.Height == height)?.Ratio ?? 0.5;

    public void Validate()
    {
        if (Rules is null) return;
        if (Rules.Any(x => x.Width <= 0 || x.Height <= 0 || !double.IsFinite(x.Ratio) || x.Ratio is <= 0 or > 1) ||
            Rules.Select(x => (x.Width, x.Height)).Distinct().Count() != Rules.Count)
            throw new ArgumentException("Invalid or duplicate PSB resolution rules.");
    }
}

public interface IPsbTextureScanner
{
    Task<IReadOnlyList<PsbTextureFileInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default);
}

/// <summary>Reads PSB metadata without decoding its texture pixels or extracting entire PFS archives.</summary>
public sealed class PsbTextureScanner : IPsbTextureScanner
{
    public static async Task<PsbTextureInspection> InspectFileAsync(string path, CancellationToken token = default)
    {
        await using FileStream stream = File.OpenRead(path);
        return await InspectEntryAsync(stream, 0, stream.Length, null, token);
    }

    private static async Task<PsbTextureInspection> InspectEntryAsync(FileStream stream, long offset, long length,
        byte[]? xorKey, CancellationToken token)
    {
        const int maximumMetadataBytes = 512 * 1024 * 1024;
        if (length < 40) throw new InvalidDataException("Truncated PSB header.");
        byte[] prefix = new byte[(int)Math.Min(56, length)];
        await ReadAsync(prefix);
        int metadataLength = PsbProcessor.TextureMetadataLength(prefix, length);
        if (metadataLength > maximumMetadataBytes) throw new InvalidDataException("PSB metadata exceeds 512 MiB scan limit.");
        byte[] data = new byte[metadataLength];
        await ReadAsync(data);
        return PsbProcessor.InspectTextureMetadata(data);

        async Task ReadAsync(byte[] buffer)
        {
            stream.Position = offset;
            await stream.ReadExactlyAsync(buffer, token);
            if (xorKey is not null)
                for (int i = 0; i < buffer.Length; i++) buffer[i] ^= xorKey[i % xorKey.Length];
        }
    }

    public async Task<IReadOnlyList<PsbTextureFileInfo>> ScanAsync(string input,
        PfsNameEncoding encoding = PfsNameEncoding.Auto, IProgress<ConversionProgress>? progress = null,
        CancellationToken token = default)
    {
        var project = await new ProjectScanner().ScanAsync(input, token);
        var codec = new PfsCodec();
        List<(PfsFileInfo Archive, PfsIndex Index)> archives = [];
        foreach (var archive in project.Archives)
        {
            token.ThrowIfCancellationRequested();
            archives.Add((archive, await codec.ReadIndexAsync(archive.Path, encoding, token)));
        }
        string[] loose = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(x => Path.GetExtension(x).Equals(".psb", StringComparison.OrdinalIgnoreCase)).ToArray();
        int total = loose.Length + archives.Sum(x => x.Index.Entries.Count(e => IsPsb(e.Path))), done = 0;
        List<PsbTextureFileInfo> result = [];
        async Task Inspect(string? archive, string path, long size, Func<Task<PsbTextureInspection>> read)
        {
            token.ThrowIfCancellationRequested();
            try { result.Add(new(archive, path, size, await read())); }
            catch (Exception error) when (error is not OperationCanceledException)
            { result.Add(new(archive, path, size, null, error.Message)); }
            progress?.Report(new(100d * ++done / Math.Max(1, total), "psb-scan", archive, path));
        }
        foreach (var (archive, index) in archives)
        {
            await using var stream = File.OpenRead(archive.Path);
            foreach (var entry in index.Entries.Where(e => IsPsb(e.Path)))
                await Inspect(archive.FileName, entry.Path, entry.Size,
                    () => InspectEntryAsync(stream, entry.Offset, entry.Size, index.XorKey, token));
        }
        foreach (string path in loose)
            await Inspect(null, Path.GetRelativePath(input, path).Replace('\\', '/'), new FileInfo(path).Length,
                () => InspectFileAsync(path, token));
        return result;
    }

    private static bool IsPsb(string path) => Path.GetExtension(path).Equals(".psb", StringComparison.OrdinalIgnoreCase);
}

public sealed partial class PsbProcessor
{
    internal static int TextureMetadataLength(byte[] prefix, long length)
    {
        if (!prefix.AsSpan(0, 4).SequenceEqual("PSB\0"u8)) throw new InvalidDataException("Missing PSB signature.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(4));
        int headerLength = version switch { 1 or 2 => 40, 3 => 44, 4 => 56, _ => throw new InvalidDataException("Unsupported PSB version.") };
        if (prefix.Length < headerLength) throw new InvalidDataException("Truncated PSB header.");
        byte[] header = prefix.AsSpan(0, headerLength).ToArray();
        if ((BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)) & 1) != 0)
            ApplyCipher(header.AsSpan(8), InferHeaderKey(header, (uint)headerLength));
        uint resourceStart = ReadUInt32(header, 32);
        if (version >= 4 && ReadUInt32(header, 52) is > 0 and var extraStart)
            resourceStart = Math.Min(resourceStart, extraStart);
        int[] metadataFields = version >= 4 ? [12, 16, 20, 24, 28, 36, 44, 48] : [12, 16, 20, 24, 28, 36];
        bool prefixContainsMetadata = resourceStart >= headerLength && resourceStart <= length &&
            metadataFields.All(field => ReadUInt32(header, field) < resourceStart);
        return checked((int)(prefixContainsMetadata ? resourceStart : length));
    }

    internal static PsbTextureInspection InspectTextureMetadata(byte[] data)
    {
        MutableDocument document = MutableDocument.Parse(data);
        if (document.Root is not ObjectNode root) throw new InvalidDataException("PSB root is not an object.");
        bool motion = string.Equals(root.GetString("id"), "motion", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(root.GetString("type"), "motion", StringComparison.OrdinalIgnoreCase);
        if (!motion || root.GetObject("source") is not { } sources) return new(false, []);
        List<PsbAtlasInfo> atlases = [];
        foreach (var (name, node) in sources.Values)
        {
            if (node is not ObjectNode source || source.GetObject("texture") is not { } texture) continue;
            int width = checked((int)(texture.GetNumber("width")?.Value ?? 0));
            int height = checked((int)(texture.GetNumber("height")?.Value ?? 0));
            if (width <= 0 || height <= 0) throw new InvalidDataException($"Invalid PSB texture size: {name}.");
            atlases.Add(new(name, width, height, texture.GetString("type") ?? "unknown"));
        }
        return new(true, atlases);
    }
}
