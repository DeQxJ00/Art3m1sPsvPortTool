using System.Collections.ObjectModel;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel
{
    private bool _nativeTextures;
    private bool _ignoreBackgroundAlpha;
    public bool NativeTextures { get => _nativeTextures; set { if (Set(ref _nativeTextures, value)) SaveSettings(); } }
    public bool IgnoreBackgroundAlpha { get => _ignoreBackgroundAlpha; set { if (Set(ref _ignoreBackgroundAlpha, value)) RefreshTextures(); } }
    public ObservableCollection<TextureCategoryViewModel> TextureCategories { get; } = [];
    public IReadOnlyList<NativeFormatInfo> TextureFormats => NativeTextureFormats.All.Where(f => f.PvrCode >= 0).ToArray();
    private bool English => Language.StartsWith("en", StringComparison.Ordinal);
    public string NativeTexturesLabel => English ? "Convert images to PSV textures (disabled by default)" : "图片压制为 PSV 专用纹理（默认关闭）";
    public string NativeTexturesHelp => English ? "If the game runs smoothly with this option unchecked, there is no need to enable it. Original PNG images can be kept. If loading stutters, try converting BG images to PSV textures first. Only adjust other categories if you understand image formats; otherwise, leave everything except BG unchanged.\nFor compatible Art3m1sPSV builds. Uncheck if these resources will also be used on other platforms or original engines. Only BG is selected by default; grayscale is kept."
        : "如果不勾选此项，游戏运行也不卡顿，就无需开启，继续使用原来的 PNG 图片即可。遇到图片加载卡顿时，可以优先尝试将 BG 背景图片转换为 PSV 专用纹理。其他分类建议在了解图片格式后再调整；不熟悉的话，请保持 BG 以外的分类不变。\nPSV 专用纹理：用于支持此功能的 Art3m1sPSV。要同时给其他平台或原版引擎用的话不要勾选。默认仅勾选 BG；灰度图片默认保留。";
    public string ScanTexturesLabel => English ? "Scan image categories" : "扫描图片分类";
    public string IgnoreBackgroundAlphaLabel => English ? "Ignore BG alpha when converting (discards transparency; can break transition overlays stored in BG)" : "转换 BG 时忽略透明度（会丢弃 Alpha；BG 中的过渡叠加图可能显示错误）";
    public string TextureFormatsLabel => English ? "GXM formats: bits/pixel, channels and usage" : "GXM 原生压缩格式：位/像素、通道和用途";
    public string TextureHelp => English ? "Scan all .pfs / .pfs.xxx archives and loose images. Unchecked images still follow the resize setting. Unsuitable selections are kept and reported. Size estimates include block alignment and container headers; retained images use original size estimates. GPU allocation overhead is extra. Compressed textures are lossy and may be larger than PNG. No mipmaps. PVRTC1 requires power-of-two dimensions; ETC1 currently has Vita3K compatibility issues. PNG offsets/crop metadata are kept as PNG.\nManual-only AUTO conversion excluding offset metadata applies only to the selected category: keep metadata and grayscale, then choose BC1 for opaque color images and BC3 for alpha regardless of folder name. It is never selected by default. Numbered patches in the same PFS family override smaller suffixes; matching resource paths convert together, or stay in the original format if any version cannot safely share the output extension."
        : "扫描全部 .pfs / .pfs.xxx 和散装图片。未勾选的图片仍按原缩放设置处理。不适合的选择会保留原格式并记录原因。大小估计包含块对齐与容器头；保留图片按原文件估计，实际缩放后会不同。显存分配还有额外开销。压缩有损，文件不一定比 PNG 小；不生成 mipmap。PVRTC1 要求二次幂尺寸；ETC1 目前有 Vita3K 兼容问题。带偏移/裁剪信息的 PNG 保留原格式。\n手动模式“除带偏移信息外的 AUTO 转换”仅作用于所选分类：保留含 metadata 及灰度图片，其余彩色图片不受目录名限制，不透明用 BC1、透明用 BC3，不会默认选中。同组 PFS 按数字后缀由大到小覆盖；同路径资源一起转换，若部分版本无法安全使用相同输出后缀则整组保留。";
    private void RefreshTextures()
    {
        double ratio = TryRatio(out var r) ? r : 1;
        foreach (var category in TextureCategories) category.Refresh(ratio, IgnoreBackgroundAlpha, English);
    }
    public async Task ScanTexturesAsync()
    {
        if (IsBusy) return;
        if (!Directory.Exists(InputDirectory)) { Status = L("InvalidPaths"); return; }
        _conversionCancellation = new(); IsBusy = true; Progress = 0; Status = L("Scanning");
        // Capture all options before dispatch, and prevent stale results after input changes.
        string input = InputDirectory; var encoding = (PfsNameEncoding)SelectedEncoding;
        var token = _conversionCancellation.Token;
        try
        {
            var reporter = new Progress<ConversionProgress>(p => { Progress = p.Percent; Status = $"{L("Scanning")} {p.Archive} / {p.Entry}"; });
            var images = await Task.Run(() => new TextureScanner().ScanAsync(input, encoding, reporter, token), token);
            if (input != InputDirectory) { Status = L("Scanning") + " — input changed"; return; }
            var previous = TextureCategories.ToDictionary(x => x.Key, x => x.Rule);
            TextureCategories.Clear();
            double ratio = TryRatio(out var r) ? r : 1;
            foreach (var group in images.GroupBy(x => x.GroupKey).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var item = new TextureCategoryViewModel(group.ToArray(), ratio, IgnoreBackgroundAlpha, English);
                if (previous.TryGetValue(group.Key, out var rule)) { item.Enabled = rule.Enabled; item.SelectedFormat = (int)rule.Format; }
                TextureCategories.Add(item);
            }
            Progress = 100;
            Status = English ? $"Scanned {images.Count} images / {TextureCategories.Count} categories" : $"已扫描 {images.Count} 张图片 / {TextureCategories.Count} 类";
        }
        catch (OperationCanceledException) { Status = L("Cancel"); }
        catch (Exception e) { Status = L("Failed") + ": " + e.Message; }
        finally { _conversionCancellation.Dispose(); _conversionCancellation = null; IsBusy = false; }
    }
}
