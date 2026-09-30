using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Art3m1s.PsvTool.Core;

public sealed record TextureImageInfo(string? Archive, string Path, string Category, int Width, int Height,
    long SourceBytes, bool IsGray, bool HasAlpha, bool HasSmoothAlpha, bool HasMetadata,
    bool IsNative = false, string? Error = null)
{
    public bool IsBackground => Category.Split('/').Any(x => x is "bg" or "background" or "backgrounds");
    public string GroupKey => Category + (IsGray ? "#gray" : "");
    public IReadOnlySet<string> ExistingPaths { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool HasStemConflict { get; init; }
}

public sealed class TextureScanner
{
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".dds" or ".pvr";
    public static string CategoryFor(string path)
    {
        string[] p = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant().Split('/');
        if (p.Length == 1) return "(root)";
        int depth = p[0] is "image" or "images" or "graphics" ? Math.Min(2, p.Length - 1) : 1;
        return string.Join('/', p.Take(depth));
    }
    public async Task<IReadOnlyList<TextureImageInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
    {
        ScanResult project = await new ProjectScanner().ScanAsync(input, token);
        var codec = new PfsCodec();
        List<(PfsFileInfo Archive, PfsIndex Index)> indices = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> duplicates = new(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in project.Archives)
        {
            var index = await codec.ReadIndexAsync(archive.Path, encoding, token);
            indices.Add((archive, index));
            foreach (var e in index.Entries) if (!names.Add(e.Path)) duplicates.Add(e.Path);
        }
        string[] loose = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Where(IsImage).ToArray();
        foreach (string p in loose) { string relative = Path.GetRelativePath(input, p).Replace('\\', '/'); if (!names.Add(relative)) duplicates.Add(relative); }
        var stems = names.Where(IsImage).GroupBy(x => Path.ChangeExtension(x, null), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int total = indices.Sum(x => x.Index.Entries.Count(e => IsImage(e.Path))) + loose.Length, done = 0;
        List<TextureImageInfo> result = [];
        async Task Inspect(string? archive, string path, long size, Func<Task<byte[]>> read)
        {
            token.ThrowIfCancellationRequested();
            TextureImageInfo image;
            try { image = InspectBytes(await read(), path, archive); }
            catch (Exception e) when (e is not OperationCanceledException)
            { image = new(archive, path, CategoryFor(path), 0, 0, size, false, false, false, false, Error: e.Message); }
            result.Add(image with { ExistingPaths = names, HasStemConflict = stems.Contains(Path.ChangeExtension(path, null)) || duplicates.Contains(path) });
            progress?.Report(new(100d * ++done / Math.Max(1, total), "texture-scan", archive, path));
        }
        foreach (var (archive, index) in indices)
        {
            await using var stream = File.OpenRead(archive.Path);
            foreach (var entry in index.Entries.Where(e => IsImage(e.Path)))
                await Inspect(archive.FileName, entry.Path, entry.Size, () => PfsCodec.ReadIndexedEntryAsync(stream, entry, index.XorKey, token));
        }
        foreach (var path in loose)
            await Inspect(null, Path.GetRelativePath(input, path).Replace('\\', '/'), new FileInfo(path).Length, async () =>
            {
                if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Image exceeds 64 MiB scan limit.");
                return await File.ReadAllBytesAsync(path, token);
            });
        return result;
    }
    public static TextureImageInfo InspectBytes(byte[] bytes, string path, string? archive = null)
    {
        string category = CategoryFor(path);
        if (bytes.AsSpan().StartsWith("DDS "u8) || bytes.AsSpan().StartsWith("PVR\x03"u8))
        {
            bool dds = bytes.AsSpan().StartsWith("DDS "u8);
            int w = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dds ? 16 : 28)));
            int h = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dds ? 12 : 24)));
            return new(archive, path, category, w, h, bytes.Length, false, false, false, false, true);
        }
        ImageInfo header = Image.Identify(bytes);
        if ((long)header.Width * header.Height > 32 * 1024 * 1024) throw new InvalidDataException("Image exceeds 32 megapixel scan limit.");
        bool metadata = HasPngMetadata(bytes);
        using Image<Rgba32> image = Image.Load<Rgba32>(bytes);
        if (image.Frames.Count > 1) metadata = true;
        bool gray = true, alpha = false, smooth = false;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++) foreach (var p in rows.GetRowSpan(y))
            {
                // Color in fully transparent texels is immaterial to classification.
                if (p.A != 0 && (p.R != p.G || p.R != p.B)) gray = false;
                if (p.A != 255) alpha = true;
                if (p.A is > 0 and < 255) smooth = true;
            }
        });
        return new(archive, path, category, image.Width, image.Height, bytes.Length, gray, alpha, smooth, metadata);
    }
    private static bool HasPngMetadata(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        int pos = 8;
        while (pos + 12 <= data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos)));
            if (length > data.Length - pos - 12) throw new InvalidDataException("Truncated PNG chunk.");
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (type is "tEXt" or "zTXt" or "iTXt" or "oFFs" or "acTL" ||
                type is not ("IHDR" or "IDAT" or "IEND" or "PLTE" or "tRNS" or "gAMA" or "sRGB" or "cHRM" or "iCCP" or "pHYs" or "tIME" or "bKGD" or "sBIT")) return true;
            pos += length + 12;
        }
        return false;
    }
}
