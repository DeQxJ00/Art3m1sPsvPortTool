namespace Art3m1s.PsvTool.Core;

public enum NativeTextureFormat
{
    Preserve, Auto, Bc1, Bc2, Bc3, Bc4, Bc4Signed, Bc5, Bc5Signed,
    PvrtcRgb2, PvrtcRgba2, PvrtcRgb4, PvrtcRgba4, Pvrtc2_2, Pvrtc2_4, Etc1,
    AutoWithoutMetadata
}

public sealed record NativeTextureRule(string Category, bool Enabled, NativeTextureFormat Format = NativeTextureFormat.Auto);
public sealed record NativeTextureOptions(bool Enabled = true, bool IgnoreBackgroundAlpha = false,
    IReadOnlyList<NativeTextureRule>? Rules = null);
public sealed record NativeFormatInfo(NativeTextureFormat Format, string Name, int BitsPerPixel,
    int PvrCode, string Channels, string Description, string Extension = ".pvr")
{
    public bool IsPvrtc1 => PvrCode is >= 0 and <= 3;
    public bool HasAlpha => Format is NativeTextureFormat.Bc1 or NativeTextureFormat.Bc2 or NativeTextureFormat.Bc3
        or NativeTextureFormat.PvrtcRgba2 or NativeTextureFormat.PvrtcRgba4 or NativeTextureFormat.Pvrtc2_2 or NativeTextureFormat.Pvrtc2_4;
}

public static class NativeTextureFormats
{
    public static readonly IReadOnlyList<NativeFormatInfo> All = [
        new(NativeTextureFormat.Preserve, "保留原格式 / Keep original", 0, -1, "原通道 / Original", "灰度、文字、UI 推荐保留 / Recommended for grayscale, text and UI"),
        new(NativeTextureFormat.Auto, "自动推荐 / Automatic", 0, -1, "按图片选择 / Per image", "灰度/UI 保留；彩色不透明 BC1，透明 BC3 / Keep gray/UI; opaque BC1, alpha BC3"),
        new(NativeTextureFormat.Bc1, "BC1 / DXT1", 4, 7, "RGB + 1-bit A", "背景、CG；仅二值透明 / Backgrounds; binary alpha only", ".dds"),
        new(NativeTextureFormat.Bc2, "BC2 / DXT3", 8, 9, "RGB + 4-bit A", "透明层级较少 / Explicit alpha levels", ".dds"),
        new(NativeTextureFormat.Bc3, "BC3 / DXT5", 8, 11, "RGB + 插值 A / interpolated A", "立绘、渐变透明 / Sprites, smooth alpha", ".dds"),
        new(NativeTextureFormat.Bc4, "BC4 UNORM", 4, 12, "R", "单通道数据；灰度默认仍保留 / Single channel; keep grayscale by default", ".dds"),
        new(NativeTextureFormat.Bc4Signed, "BC4 SNORM", 4, 12, "R (signed)", "有符号数据，不推荐普通图片 / Signed data, not ordinary images", ".dds"),
        new(NativeTextureFormat.Bc5, "BC5 UNORM", 8, 13, "RG", "双通道数据，不保留蓝色与 Alpha / No blue or alpha", ".dds"),
        new(NativeTextureFormat.Bc5Signed, "BC5 SNORM", 8, 13, "RG (signed)", "有符号双通道数据 / Signed two-channel data", ".dds"),
        new(NativeTextureFormat.PvrtcRgb2, "PVRTC1 RGB 2bpp", 2, 0, "RGB", "二次幂尺寸；细节损失较大 / Power-of-two; lower detail"),
        new(NativeTextureFormat.PvrtcRgba2, "PVRTC1 RGBA 2bpp", 2, 1, "RGBA", "二次幂尺寸；细节损失较大 / Power-of-two; lower detail"),
        new(NativeTextureFormat.PvrtcRgb4, "PVRTC1 RGB 4bpp", 4, 2, "RGB", "二次幂尺寸 / Power-of-two dimensions"),
        new(NativeTextureFormat.PvrtcRgba4, "PVRTC1 RGBA 4bpp", 4, 3, "RGBA", "二次幂尺寸 / Power-of-two dimensions"),
        new(NativeTextureFormat.Pvrtc2_2, "PVRTC2 2bpp", 2, 4, "RGBA", "较小，但可能损失细节 / Small, lower detail"),
        new(NativeTextureFormat.Pvrtc2_4, "PVRTC2 4bpp", 4, 5, "RGBA", "透明彩色图片 / Color with alpha"),
        new(NativeTextureFormat.Etc1, "ETC1", 4, 6, "RGB", "无 Alpha；目前 Vita3K 不兼容 / No alpha; current Vita3K incompatible"),
        new(NativeTextureFormat.AutoWithoutMetadata, "除带偏移信息外的 AUTO 转换（仅手动） / AUTO conversion excluding offset metadata (manual only)", 0, -1,
            "按图片选择 / Per image", "所选分类跳过 metadata 和灰度；其余不透明 BC1、透明 BC3 / Selected category: keep metadata and gray; opaque BC1, alpha BC3")
    ];
    public static NativeFormatInfo Info(NativeTextureFormat f) => All.Single(x => x.Format == f);
    public static bool IsAutomatic(NativeTextureFormat format) =>
        format is NativeTextureFormat.Auto or NativeTextureFormat.AutoWithoutMetadata;
    public static NativeTextureFormat Resolve(TextureImageInfo image, NativeTextureFormat requested, bool ignoreAlpha) => requested switch
    {
        NativeTextureFormat.Auto => Recommend(image, ignoreAlpha),
        NativeTextureFormat.AutoWithoutMetadata => RecommendColor(image, ignoreAlpha),
        _ => requested
    };
    public static NativeTextureFormat Recommend(TextureImageInfo image, bool ignoreAlpha) =>
        IsConservative(image.Category) ? NativeTextureFormat.Preserve : RecommendColor(image, ignoreAlpha);
    private static NativeTextureFormat RecommendColor(TextureImageInfo image, bool ignoreAlpha) =>
        image.IsGray || image.IsNative || image.HasMetadata || image.Error != null
            ? NativeTextureFormat.Preserve
            : image.HasAlpha && !(ignoreAlpha && image.IsBackground) ? NativeTextureFormat.Bc3 : NativeTextureFormat.Bc1;
    private static bool IsConservative(string category) => !category.Split('/').Any(x =>
        x is "bg" or "fg" or "cg" or "event" or "ev" or "background" or "backgrounds" or "character" or "characters");
    public static bool DefaultEnabled(TextureImageInfo image) => image.IsBackground && !image.IsGray;
    // Hard blockers are kept unchanged even if explicitly selected. Lossy channel
    // choices remain selectable, but are never applied silently to incompatible data.
    public static string? Unsuitable(TextureImageInfo image, NativeTextureFormat format, double ratio, bool ignoreAlpha)
    {
        string? ownReason = UnsuitableSingle(image, format, ratio, ignoreAlpha);
        if (ownReason != null) return ownReason;
        var resolved = Resolve(image, format, ignoreAlpha);
        if (resolved == NativeTextureFormat.Preserve) return null;
        foreach (var peer in image.OverlayPeers)
        {
            var peerFormat = Resolve(peer, format, ignoreAlpha);
            if (peer.GroupKey != image.GroupKey || peerFormat == NativeTextureFormat.Preserve
                || UnsuitableSingle(peer, format, ratio, ignoreAlpha) != null
                || Info(peerFormat).Extension != Info(resolved).Extension)
                return "覆盖链需统一后缀，部分图片需保留原格式 / Keep overlay chain: some versions require the original format";
        }
        return null;
    }
    private static string? UnsuitableSingle(TextureImageInfo image, NativeTextureFormat format, double ratio, bool ignoreAlpha)
    {
        if (format == NativeTextureFormat.Preserve) return null;
        format = Resolve(image, format, ignoreAlpha);
        if (format == NativeTextureFormat.Preserve) return null;
        if (image.Error != null) return "无法解析图片 / Cannot inspect image";
        if (image.IsNative) return "已是原生纹理，避免重复有损压缩 / Already compressed";
        if (image.HasMetadata) return "含偏移/裁剪等附加信息 / Embedded image metadata";
        int w = Math.Max(1, (int)(image.Width * ratio)), h = Math.Max(1, (int)(image.Height * ratio));
        if (w > 4096 || h > 4096) return "输出尺寸超过 4096 / Output exceeds 4096";
        NativeFormatInfo f = Info(format);
        if (f.IsPvrtc1 && ((w & (w - 1)) != 0 || (h & (h - 1)) != 0)) return "输出尺寸不是二次幂 / Output not power-of-two";
        bool alpha = image.HasAlpha && !(image.IsBackground && ignoreAlpha);
        if (alpha && !f.HasAlpha) return "不能保留透明度 / Cannot preserve alpha";
        if (alpha && (image.HasSmoothAlpha || ratio != 1) && format == NativeTextureFormat.Bc1) return "含半透明像素 / Contains smooth alpha";
        if (format is NativeTextureFormat.Bc4Signed or NativeTextureFormat.Bc5Signed) return "普通图片不是有符号数据 / Image is not signed data";
        if (format == NativeTextureFormat.Bc4 && !image.IsGray) return "会丢失彩色通道 / Loses color channels";
        if (format == NativeTextureFormat.Bc5) return "普通图片需要完整 RGB / Full RGB required";
        if (image.ExistingPaths.Contains(Path.ChangeExtension(image.Path, f.Extension))) return "目标文件已存在 / Target name already exists";
        if (image.HasStemConflict) return image.ConflictReason ?? "同名不同后缀资源冲突 / Conflicting source names";
        return null;
    }
    public static long PayloadBytes(NativeTextureFormat format, int width, int height)
    {
        NativeFormatInfo f = Info(format);
        if (f.PvrCode < 0) return 0;
        int bw = f.BitsPerPixel == 2 ? 8 : 4;
        long x = (width + bw - 1) / bw, y = (height + 3) / 4;
        if (f.IsPvrtc1) { x = Math.Max(2, x); y = Math.Max(2, y); }
        return checked(x * y * (f.BitsPerPixel == 8 ? 16 : 8));
    }
}
