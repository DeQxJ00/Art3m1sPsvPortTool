using System.Text.Json;
using System.Text.Json.Serialization;

namespace Art3m1s.PsvTool.Core;

// One uniform compensation multiplier per PSB; no atlas or icon dimension lists.
// FNV-1a is only an identity guard against stale/overlaid resources, not security verification.
public sealed record PsbRenderModel(string Path, string? Archive, string Fingerprint,
    double TextureRatio, double RenderScale);
public sealed record PsbRenderManifest(int Version, double GeometryRatio, IReadOnlyList<PsbRenderModel> Models)
{
    public const int CurrentVersion = 2;
    public const string FileName = "art3m1s_psb_render.json";

    public static string FingerprintBytes(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in bytes) hash = unchecked((hash ^ value) * 1099511628211);
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal async Task WriteAsync(string directory, CancellationToken cancellationToken)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(this, PsbRenderJsonContext.Default.PsbRenderManifest);
        if (data.Length > 8 * 1024 * 1024 || Models.Count > 10000)
            throw new InvalidDataException("PSB render configuration exceeds the engine's supported limits.");
        await File.WriteAllBytesAsync(System.IO.Path.Combine(directory, FileName), data, cancellationToken);
    }

    internal static async Task<string> FingerprintFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[65536];
        ulong hash = 14695981039346656037;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            for (int index = 0; index < read; index++) hash = unchecked((hash ^ buffer[index]) * 1099511628211);
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PsbRenderManifest))]
public sealed partial class PsbRenderJsonContext : JsonSerializerContext;
