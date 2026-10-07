using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Art3m1s.PsvTool.Core;

public sealed record TextureConversionResult(string OutputPath, bool Converted, string Message, long Before, long After);

public sealed class NativeTextureProcessor
{
    // PVRTexLib may internally use worker threads; serialize entry into the native
    // encoder to bound memory usage and avoid oversubscription with image workers.
    private static readonly SemaphoreSlim EncoderGate = new(1);

    public async Task<TextureConversionResult> ConvertAsync(string path, TextureImageInfo source,
        NativeTextureOptions options, double ratio, CancellationToken token = default)
    {
        long before = new FileInfo(path).Length;
        NativeTextureRule? rule = options.Rules?.FirstOrDefault(r => r.Category == source.GroupKey);
        bool enabled = options.Enabled && (rule?.Enabled ?? NativeTextureFormats.DefaultEnabled(source));
        NativeTextureFormat format = rule?.Format ?? NativeTextureFormat.Auto;
        if (source.Error != null) return new(path, false, "无法解析，原样保留 / Cannot inspect, kept: " + source.Error, before, before);
        if (!enabled) return new(path, false, "未勾选 / Not selected", before, before);
        string? reason = NativeTextureFormats.Unsuitable(source, format, ratio, options.IgnoreBackgroundAlpha);
        if (reason != null) return new(path, false, "保留 / Kept: " + reason, before, before);
        format = NativeTextureFormats.Resolve(source, format, options.IgnoreBackgroundAlpha);
        if (format == NativeTextureFormat.Preserve) return new(path, false, "保留原格式 / Kept original", before, before);
        NativeFormatInfo spec = NativeTextureFormats.Info(format);
        string output = Path.ChangeExtension(path, spec.Extension);
        if (File.Exists(output)) return new(path, false, "保留：目标文件已存在 / Kept: target exists", before, before);
        // Keep temporary output beside its destination for an atomic rename.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + spec.Extension;
        await EncoderGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(path, token);
            if (source.IsBackground && options.IgnoreBackgroundAlpha)
                image.ProcessPixelRows(rows =>
                {
                    for (int y = 0; y < rows.Height; y++)
                        foreach (ref var pixel in rows.GetRowSpan(y)) pixel.A = 255;
                });
            byte[] rgba = new byte[checked(image.Width * image.Height * 4)]; image.CopyPixelDataTo(rgba);
            Encode(rgba, image.Width, image.Height, spec, temporary);
            token.ThrowIfCancellationRequested();
            long expected = NativeTextureFormats.PayloadBytes(format, image.Width, image.Height);
            if (new FileInfo(temporary).Length < expected) throw new InvalidDataException("Truncated native texture output.");
            File.Move(temporary, output);
            File.Delete(path);
            return new(output, true, spec.Name, before, new FileInfo(output).Length);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            EncoderGate.Release();
        }
    }
    private static byte[] ContainerHeader(NativeFormatInfo spec, int w, int h, int length)
    {
        bool dds = spec.Extension == ".dds";
        byte[] header = new byte[dds ? 128 : 52];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), value);
        if (dds)
        {
            "DDS "u8.CopyTo(header); U(4, 124); U(8, 0x81007); U(12, (uint)h); U(16, (uint)w);
            U(20, (uint)length); U(28, 1); U(76, 32); U(80, 4); U(108, 0x1000);
            string code = spec.Format switch
            {
                NativeTextureFormat.Bc1 => "DXT1",
                NativeTextureFormat.Bc2 => "DXT3",
                NativeTextureFormat.Bc3 => "DXT5",
                NativeTextureFormat.Bc4 => "ATI1",
                NativeTextureFormat.Bc4Signed => "BC4S",
                NativeTextureFormat.Bc5 => "ATI2",
                NativeTextureFormat.Bc5Signed => "BC5S",
                _ => throw new InvalidOperationException()
            };
            System.Text.Encoding.ASCII.GetBytes(code).CopyTo(header, 84);
        }
        else
        {
            "PVR\x03"u8.CopyTo(header); U(8, (uint)spec.PvrCode); U(24, (uint)h); U(28, (uint)w);
            U(32, 1); U(36, 1); U(40, 1); U(44, 1);
        }
        return header;
    }
    private static void Encode(byte[] rgba, int width, int height, NativeFormatInfo spec, string output)
    {
        byte[] payload = PvrTextureCodec.Encode(rgba, width, height, spec);
        // Managed I/O keeps CJK filenames safe on Windows.
        using var file = File.Create(output);
        file.Write(ContainerHeader(spec, width, height, payload.Length)); file.Write(payload);
    }
}
