namespace Art3m1s.PsvTool.Core;

// Shared PSB/DDS GXM layout. BC1 uses 8 bytes per 4x4 block, BC3 uses 16.
internal static class GxmBlockTextureStorage
{
    public static int LinearBytes(int width, int height, int blockBytes)
    {
        Validate(width, height, blockBytes);
        return checked(((width + 3) / 4) * ((height + 3) / 4) * blockBytes);
    }

    public static int SwizzledBytes(int width, int height, int blockBytes)
    {
        Validate(width, height, blockBytes);
        return checked((Extent(width) / 4) * (Extent(height) / 4) * blockBytes);
    }

    public static byte[] Swizzle(byte[] linear, int width, int height, int blockBytes)
    {
        if (linear.Length != LinearBytes(width, height, blockBytes)) throw new InvalidDataException("Invalid linear BC payload length.");
        byte[] swizzled = new byte[SwizzledBytes(width, height, blockBytes)];
        CopyBlocks(linear, swizzled, width, height, blockBytes, toSwizzled: true);
        return swizzled;
    }

    public static byte[] Unswizzle(byte[] swizzled, int width, int height, int blockBytes)
    {
        if (swizzled.Length != SwizzledBytes(width, height, blockBytes)) throw new InvalidDataException("Invalid swizzled BC payload length.");
        byte[] linear = new byte[LinearBytes(width, height, blockBytes)];
        CopyBlocks(swizzled, linear, width, height, blockBytes, toSwizzled: false);
        return linear;
    }

    private static void CopyBlocks(byte[] source, byte[] destination, int width, int height, int blockBytes, bool toSwizzled)
    {
        int sourceBlocksX = (width + 3) / 4, sourceBlocksY = (height + 3) / 4;
        int storageBlocksX = Extent(width) / 4, storageBlocksY = Extent(height) / 4;
        for (int y = 0; y < sourceBlocksY; y++)
            for (int x = 0; x < sourceBlocksX; x++)
            {
                int linearOffset = (y * sourceBlocksX + x) * blockBytes;
                int swizzledOffset = BlockIndex(x, y, storageBlocksX, storageBlocksY) * blockBytes;
                source.AsSpan(toSwizzled ? linearOffset : swizzledOffset, blockBytes)
                    .CopyTo(destination.AsSpan(toSwizzled ? swizzledOffset : linearOffset, blockBytes));
            }
    }

    // Y occupies the low Morton bit. Once the shorter power-of-two
    // block dimension ends, continue only the longer dimension.
    private static int BlockIndex(int x, int y, int blocksX, int blocksY)
    {
        int index = 0, shift = 0;
        for (int bit = 1; bit < blocksX || bit < blocksY; bit <<= 1)
        {
            if (bit < blocksY) { index |= ((y & bit) != 0 ? 1 : 0) << shift; shift++; }
            if (bit < blocksX) { index |= ((x & bit) != 0 ? 1 : 0) << shift; shift++; }
        }
        return index;
    }

    private static int Extent(int value)
    {
        int extent = 4;
        while (extent < value) extent <<= 1;
        return extent;
    }

    private static void Validate(int width, int height, int blockBytes)
    {
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096)
            throw new InvalidDataException("GXM swizzled BC dimensions must be between 1 and 4096.");
        if (blockBytes is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(blockBytes));
    }
}
