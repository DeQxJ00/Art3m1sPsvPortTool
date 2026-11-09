using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using System.Runtime.InteropServices;
namespace Art3m1s.PsvTool.Core;
internal static class Bc7TextureCodec
{
    public static byte[] Decode(byte[] payload, int width, int height)
    {
        if (payload.Length != PsbTextureSizeEstimator.PayloadBytes("BC7", width, height))
            throw new InvalidDataException("BC7 payload length does not match its dimensions.");
        var decoder = new BcDecoder();
        decoder.Options.IsParallel = false;
        return MemoryMarshal.AsBytes(decoder.DecodeRaw(payload, width, height, CompressionFormat.Bc7).AsSpan()).ToArray();
    }
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        if (rgba.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid RGBA pixel length.");
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.Options.IsParallel = false;
        encoder.OutputOptions.GenerateMipMaps = false;
        return encoder.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32)[0];
    }
}
