namespace Art3m1s.PsvTool.Core;

public enum PsbDxt5Layout
{
    Swizzled,
    Linear
}

internal static class PsbDxt5Storage
{
    public static int LinearBytes(int width, int height) => GxmBlockTextureStorage.LinearBytes(width, height, 16);
    public static int SwizzledBytes(int width, int height) => GxmBlockTextureStorage.SwizzledBytes(width, height, 16);
    public static byte[] Swizzle(byte[] linear, int width, int height) => GxmBlockTextureStorage.Swizzle(linear, width, height, 16);
    public static byte[] Unswizzle(byte[] swizzled, int width, int height) => GxmBlockTextureStorage.Unswizzle(swizzled, width, height, 16);
}
