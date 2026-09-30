using System.ComponentModel;
using System.Runtime.CompilerServices;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed class TextureCategoryViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private int _selectedFormat = 1;
    private double _ratio;
    private bool _ignoreAlpha;
    private bool _english;
    private IReadOnlyList<string>? _choices;
    private string? _summary;
    private IReadOnlyList<string>? _fileRows;
    private bool _detailsExpanded;
    public TextureCategoryViewModel(IReadOnlyList<TextureImageInfo> images, double ratio, bool ignoreAlpha, bool english)
    {
        Images = images; Key = images[0].GroupKey;
        _enabled = images.Any(NativeTextureFormats.DefaultEnabled);
        _ratio = ratio; _ignoreAlpha = ignoreAlpha; _english = english;
        if (images.All(x => NativeTextureFormats.Recommend(x, ignoreAlpha) == NativeTextureFormat.Preserve)) _selectedFormat = 0;
    }
    public IReadOnlyList<TextureImageInfo> Images { get; }
    public string Key { get; }
    public string Label => Images[0].Category + (Images[0].IsGray ? (_english ? " (grayscale)" : "（灰度）") : "");
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; _summary = null; Changed(); Changed(nameof(Summary)); } }
    public int SelectedFormat
    {
        get => _selectedFormat;
        set { if (value < 0 || value >= NativeTextureFormats.All.Count || value == _selectedFormat) return; _selectedFormat = value; _summary = null; _fileRows = null; Changed(); Changed(nameof(Summary)); Changed(nameof(FileRows)); }
    }
    public NativeTextureRule Rule => new(Key, Enabled, NativeTextureFormats.All[SelectedFormat].Format);
    public IReadOnlyList<string> Choices => _choices ??= NativeTextureFormats.All.Select(f => Choice(f)).ToArray();
    public bool DetailsExpanded
    {
        get => _detailsExpanded;
        set { if (_detailsExpanded == value) return; _detailsExpanded = value; Changed(); Changed(nameof(FileRows)); }
    }
    private string Choice(NativeFormatInfo f)
    {
        var reasons = Images.Select(x => NativeTextureFormats.Unsuitable(x, f.Format, _ratio, _ignoreAlpha)).Where(x => x != null).ToArray();
        string name = f.Name;
        if (NativeTextureFormats.IsAutomatic(f.Format))
        {
            var formats = Images.GroupBy(image =>
            {
                var recommended = NativeTextureFormats.Resolve(image, f.Format, _ignoreAlpha);
                return NativeTextureFormats.Unsuitable(image, f.Format, _ratio, _ignoreAlpha) == null
                    ? recommended : NativeTextureFormat.Preserve;
            }).OrderBy(group => group.Key).ToArray();
            string prefix = f.Format == NativeTextureFormat.AutoWithoutMetadata
                ? (_english ? "AUTO conversion excluding offset metadata (manual only) → " : "除带偏移信息外的 AUTO 转换（仅手动） → ")
                : (_english ? "AUTO → " : "AUTO 自动 → ");
            name = prefix + string.Join(" + ", formats.Select(group =>
            {
                string label = group.Key == NativeTextureFormat.Preserve
                    ? (_english ? "Keep original" : "保留原格式") : NativeTextureFormats.Info(group.Key).Name;
                return formats.Length == 1 ? label : $"{label} × {group.Count()}";
            }));
        }
        string status = reasons.Length > 0
            ? (_english ? $" — unsuitable {reasons.Length}/{Images.Count}: " : $" — 不适合 {reasons.Length}/{Images.Count} 张：") + string.Join("；", reasons.Distinct())
            : Images.All(x => NativeTextureFormats.Recommend(x, _ignoreAlpha) == f.Format) || f.Format == NativeTextureFormat.Auto
                ? (_english ? " — recommended" : " — 推荐") : (_english ? " — optional" : " — 可选");
        return name + status;
    }
    public string Summary
    {
        get
        {
            if (_summary != null) return _summary;
            long original = Images.Sum(x => x.SourceBytes), target = 0;
            int converted = 0;
            foreach (var image in Images)
            {
                var f = NativeTextureFormats.Resolve(image, Rule.Format, _ignoreAlpha);
                if (!Enabled || f == NativeTextureFormat.Preserve || NativeTextureFormats.Unsuitable(image, Rule.Format, _ratio, _ignoreAlpha) != null)
                { target += image.SourceBytes; continue; }
                converted++;
                target += NativeTextureFormats.PayloadBytes(f, Math.Max(1, (int)(image.Width * _ratio)), Math.Max(1, (int)(image.Height * _ratio)))
                    + (NativeTextureFormats.Info(f).Extension == ".dds" ? 128 : 52);
            }
            string alpha = $"{Images.Count(x => x.HasAlpha)}";
            return _summary = _english ? $"{Images.Count} images · alpha {alpha} · convert {converted} · {original / 1048576d:F1} MiB → ~{target / 1048576d:F1} MiB"
                : $"{Images.Count} 张 · 透明 {alpha} 张 · 可转换 {converted} 张 · {original / 1048576d:F1} MiB → 约 {target / 1048576d:F1} MiB";
        }
    }
    // ListBox virtualizes layout; collapsed details do not format any file rows.
    public IReadOnlyList<string> FileRows => !DetailsExpanded ? Array.Empty<string>() : _fileRows ??= Images.Select(i =>
    {
        string? reason = i.HasMetadata ? "含偏移/裁剪等附加信息，保留原格式 / Embedded metadata; keep original"
            : NativeTextureFormats.Unsuitable(i, Rule.Format, _ratio, _ignoreAlpha);
        return $"{i.Archive ?? "loose"} / {i.Path} · {i.Width}×{i.Height}" +
            (i.OverlayWinner != null ? $" · 覆盖来源 / Overlay winner: {i.OverlayWinner}" : "") +
            (i.HasSmoothAlpha ? " · 半透明 / smooth alpha" : i.HasAlpha ? " · 透明 / alpha" : " · 不透明 / opaque") +
            (i.Error != null ? " · " + i.Error : reason != null ? " · " + reason : "");
    }).ToArray();
    public string DetailsLabel => _english ? "Files / reasons" : "文件与不适合原因";
    public void Refresh(double ratio, bool ignoreAlpha, bool english)
    {
        _ratio = ratio; _ignoreAlpha = ignoreAlpha; _english = english;
        _choices = null; _summary = null; _fileRows = null;
        foreach (string n in new[] { nameof(Label), nameof(Choices), nameof(Summary), nameof(FileRows), nameof(DetailsLabel) }) Changed(n);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
