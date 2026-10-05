namespace Art3m1s.PsvTool.Core;

public sealed record PsbTextureMemoryEstimate(long RgbaDataBytes, long Dxt5DataBytes,
    long RgbaAllocationBytes, long Dxt5AllocationBytes);

/// <summary>
/// Single-atlas estimates for the current Art3m1sPSV direct GXM upload path, not total game memory.
/// BC3 uses 16-byte 4x4 blocks and power-of-two storage; RGBA uses an 8-pixel stride.
/// Both allocations are rounded to 256 KiB by host-direct/src/gpu.cpp.
/// </summary>
public static class PsbTextureMemory
{
    public static PsbTextureMemoryEstimate? Estimate(int width, int height)
    {
        // The current native BC3 path accepts dimensions up to 4096, inclusive.
        if (width is < 1 or > 4096 || height is < 1 or > 4096) return null;
        long rgba = (long)width * height * 4;
        long dxt5 = ((width + 3L) / 4) * ((height + 3L) / 4) * 16;
        long rgbaStorage = (long)((width + 7) & ~7) * height * 4;
        long dxt5Storage = (long)BlockExtent(width) * BlockExtent(height);
        return new(rgba, dxt5, Allocation(rgbaStorage), Allocation(dxt5Storage));
    }

    private static int BlockExtent(int value)
    {
        int extent = 4;
        while (extent < value) extent *= 2;
        return extent;
    }

    private static long Allocation(long bytes) => (bytes + 0x3ffff) & ~0x3ffffL;
}
