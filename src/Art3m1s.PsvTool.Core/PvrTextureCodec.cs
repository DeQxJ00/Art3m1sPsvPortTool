using PVRTexLib;

namespace Art3m1s.PsvTool.Core;

internal static class PvrTextureCodec
{
    // Shared by PNG and PSB jobs: native codec calls must not overlap.
    private static readonly object Gate = new();
    private static ulong RgbaFormat => PVRDefine.PVRTGENPIXELID4('r', 'g', 'b', 'a', 8, 8, 8, 8);

    public static byte[] Encode(byte[] rgba, int width, int height, NativeFormatInfo spec)
    {
        if (rgba.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid RGBA pixel length.");
        lock (Gate)
        {
            return Transcode(rgba, width, height, RgbaFormat, (ulong)spec.PvrCode,
                spec.Format is NativeTextureFormat.Bc4Signed or NativeTextureFormat.Bc5Signed
                    ? PVRTexLibVariableType.SignedByteNorm : PVRTexLibVariableType.UnsignedByteNorm,
                checked((int)NativeTextureFormats.PayloadBytes(spec.Format, width, height)));
        }
    }

    public static byte[] Decode(byte[] payload, int width, int height, NativeTextureFormat format)
    {
        var spec = NativeTextureFormats.Info(format);
        if (payload.Length != NativeTextureFormats.PayloadBytes(format, width, height))
            throw new InvalidDataException("Compressed PSB texture length does not match its dimensions.");
        lock (Gate)
            return Transcode(payload, width, height, (ulong)spec.PvrCode, RgbaFormat,
                PVRTexLibVariableType.UnsignedByteNorm, checked(width * height * 4));
    }

    private static unsafe byte[] Transcode(byte[] pixels, int width, int height, ulong sourceFormat,
        ulong targetFormat, PVRTexLibVariableType targetType, int expectedLength)
    {
        fixed (byte* data = pixels)
        {
            using var header = new PVRTextureHeader(sourceFormat, (uint)width, (uint)height, 1, 1, 1, 1,
                PVRTexLibColourSpace.Linear, PVRTexLibVariableType.UnsignedByteNorm, false);
            using var texture = new PVRTexture(header, data);
            if (!texture.Transcode(targetFormat, targetType, PVRTexLibColourSpace.Linear, PVRTexLibCompressorQuality.PVRTCHigh))
                throw new InvalidDataException("PVRTexLib texture transcode failed.");
            int length = checked((int)texture.GetTextureDataSize(0));
            if (length != expectedLength) throw new InvalidDataException("Encoder returned an unexpected block layout.");
            return new ReadOnlySpan<byte>(texture.GetTextureDataPointer(0), length).ToArray();
        }
    }
}
