using System.ComponentModel;
using System.Runtime.CompilerServices;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed class TextureCategoryViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private int _selectedFormat = NativeTextureFormats.IndexOf(ConversionDefaults.ImageFormat);
    private double _ratio;
    private bool _ignoreAlpha;
    private bool _english;
    private bool _exportPngCoordinates;
    private bool _manualEnabled;
    private bool _manualFormat;
    private IReadOnlyList<string>? _choices;
    private IReadOnlyList<TextureFormatChoice>? _formatItems;
    private string? _summary;
    private IReadOnlyList<string>? _fileRows;
    private bool _detailsExpanded;
    public TextureCategoryViewModel(IReadOnlyList<TextureImageInfo> images, double ratio, bool ignoreAlpha, bool english,
        bool exportPngCoordinates = false)
    {
        Images = images; Key = images[0].GroupKey;
        _ratio = ratio; _ignoreAlpha = ignoreAlpha; _english = english;
        _exportPngCoordinates = exportPngCoordinates;
        UpdateRecommendation();
    }
    public IReadOnlyList<TextureImageInfo> Images { get; }
    public string Key { get; }
    public string Label => Images[0].Category + (Images[0].IsGray ? (_english ? " (grayscale)" : "（灰度）") : "");
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _manualEnabled = true; _enabled = value; _summary = null; Changed(); Changed(nameof(Summary)); } }
    public int SelectedFormat
    {
        get => _selectedFormat;
        set { if (value < 0 || value >= NativeTextureFormats.All.Count || value == _selectedFormat) return; _manualFormat = true; _selectedFormat = value; _summary = null; _fileRows = null; Changed(); Changed(nameof(Summary)); Changed(nameof(FileRows)); }
    }
    public NativeTextureRule Rule => new(Key, Enabled, NativeTextureFormats.All[SelectedFormat].Format);
    internal void RestoreSelection(TextureCategoryViewModel previous)
    {
        _enabled = previous._enabled;
        _selectedFormat = previous._selectedFormat;
        _manualEnabled = previous._manualEnabled;
        _manualFormat = previous._manualFormat;
    }
    internal static TextureCategoryViewModel[] FromScan(IReadOnlyList<TextureImageInfo> images, double ratio,
        bool ignoreAlpha, bool english, IReadOnlyDictionary<string, NativeTextureRule>? previous = null, bool exportPngCoordinates = false) =>
        images.GroupBy(x => x.GroupKey).OrderBy(x => x.Key, StringComparer.Ordinal).Select(group =>
        {
            var category = new TextureCategoryViewModel(group.ToArray(), ratio, ignoreAlpha, english, exportPngCoordinates);
            if (previous != null && previous.TryGetValue(group.Key, out var rule))
            { category.Enabled = rule.Enabled; category.SelectedFormat = NativeTextureFormats.IndexOf(rule.Format); }
            return category;
        }).ToArray();
    public IReadOnlyList<string> Choices => _choices ??= NativeTextureFormats.All.Select(f => Choice(f)).ToArray();
    // Keep item identities stable while translated labels and suitability
    // reasons change, so the bound ComboBox does not lose its selection.
    public IReadOnlyList<TextureFormatChoice> FormatItems => _formatItems ??= Choices.Select(label => new TextureFormatChoice(label)).ToArray();
    public bool DetailsExpanded
    {
        get => _detailsExpanded;
        set { if (_detailsExpanded == value) return; _detailsExpanded = value; Changed(); Changed(nameof(FileRows)); }
    }
    private string Choice(NativeFormatInfo f)
    {
        var reasons = Images.Select(x => Unsuitable(x, f.Format)).Where(x => x != null).ToArray();
        string name = f.Name;
        if (NativeTextureFormats.IsAutomatic(f.Format))
        {
            var formats = Images.GroupBy(image =>
            {
                var recommended = Resolve(image, f.Format);
                return Unsuitable(image, f.Format) == null
                    ? recommended : NativeTextureFormat.Preserve;
            }).OrderBy(group => group.Key).ToArray();
            string layout = f.Format is NativeTextureFormat.AutoLinear or NativeTextureFormat.AutoWithoutMetadataLinear ? "Linear" : "Swizzled";
            string prefix = NativeTextureFormats.IsManualAutomatic(f.Format)
                ? (_english ? "AUTO conversion excluding offset metadata (manual only) → " : "除带偏移信息外的 AUTO 转换（仅手动） → ")
                : (_english ? "AUTO → " : "AUTO 自动 → ");
            name = prefix.Replace(" → ", $" · {layout} → ") + string.Join(" + ", formats.Select(group =>
            {
                string label = group.Key == NativeTextureFormat.Preserve
                    ? (_english ? "Keep original" : "保留原格式") : NativeTextureFormats.Info(group.Key).Name;
                return formats.Length == 1 ? label : $"{label} × {group.Count()}";
            }));
        }
        string status = reasons.Length > 0
            ? (_english ? $" — unsuitable {reasons.Length}/{Images.Count}: " : $" — 不适合 {reasons.Length}/{Images.Count} 张：") + string.Join("；", reasons.Distinct())
            : Images.All(x => Recommend(x) == f.Format) || f.Format == NativeTextureFormat.Auto
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
                var f = Resolve(image, Rule.Format);
                if (!Enabled || f == NativeTextureFormat.Preserve || Unsuitable(image, Rule.Format) != null)
                { target += image.SourceBytes; continue; }
                converted++;
                target += NativeTextureFormats.PayloadBytes(f, Math.Max(1, (int)(image.Width * _ratio)), Math.Max(1, (int)(image.Height * _ratio)))
                    + (NativeTextureFormats.Info(f).Extension == ".dds" ? 128 : 52);
                if (NativeTextureFormats.ExportsPngCoordinates(image, f, _exportPngCoordinates))
                    target += image.EstimateCoordinateCsvBytes(_ratio);
            }
            string alpha = $"{Images.Count(x => x.HasAlpha)}";
            return _summary = _english ? $"{Images.Count} images · alpha {alpha} · convert {converted} · {original / 1048576d:F1} MiB → ~{target / 1048576d:F1} MiB"
                : $"{Images.Count} 张 · 透明 {alpha} 张 · 可转换 {converted} 张 · {original / 1048576d:F1} MiB → 约 {target / 1048576d:F1} MiB";
        }
    }
    // ListBox virtualizes layout; collapsed details do not format any file rows.
    public IReadOnlyList<string> FileRows => !DetailsExpanded ? Array.Empty<string>() : _fileRows ??= Images.Select(i =>
    {
        string? reason = Unsuitable(i, Rule.Format);
        if (i.HasMetadata && reason == null)
            reason = Enabled && NativeTextureFormats.ExportsPngCoordinates(i, Resolve(i, Rule.Format), _exportPngCoordinates)
                ? (_english ? "Export coordinate CSV" : "导出坐标 CSV")
                : (_english ? "Embedded metadata; keep original" : "含偏移/裁剪等附加信息，保留原格式");
        return $"{i.Archive ?? "loose"} / {i.Path} · {i.Width}×{i.Height}" +
            (i.OverlayWinner != null ? $" · 覆盖来源 / Overlay winner: {i.OverlayWinner}" : "") +
            (i.HasSmoothAlpha ? " · 半透明 / smooth alpha" : i.HasAlpha ? " · 透明 / alpha" : " · 不透明 / opaque") +
            (i.Error != null ? " · " + i.Error : reason != null ? " · " + reason : "");
    }).ToArray();
    public string DetailsLabel => _english ? "Files / reasons" : "文件与不适合原因";
    public void Refresh(double ratio, bool ignoreAlpha, bool english, bool exportPngCoordinates = false)
    {
        bool updateRecommendation = _exportPngCoordinates != exportPngCoordinates
            || exportPngCoordinates && _ratio != ratio;
        _ratio = ratio; _ignoreAlpha = ignoreAlpha; _english = english;
        _exportPngCoordinates = exportPngCoordinates;
        if (updateRecommendation) UpdateRecommendation();
        _choices = null; _summary = null; _fileRows = null;
        if (_formatItems != null)
            for (int i = 0; i < _formatItems.Count; i++) _formatItems[i].Update(Choices[i]);
        foreach (string n in new[] { nameof(Label), nameof(Choices), nameof(Summary), nameof(FileRows), nameof(DetailsLabel), nameof(SelectedFormat), nameof(Enabled) }) Changed(n);
    }
    private void UpdateRecommendation()
    {
        if (!_manualEnabled)
            _enabled = Images.Any(image => Recommend(image) != NativeTextureFormat.Preserve
                && Unsuitable(image, NativeTextureFormat.Auto) == null);
        if (!_manualFormat)
            _selectedFormat = NativeTextureFormats.IndexOf(Images.Any(image => Recommend(image) != NativeTextureFormat.Preserve)
                ? ConversionDefaults.ImageFormat : NativeTextureFormat.Preserve);
    }
    private NativeTextureFormat Recommend(TextureImageInfo image) =>
        NativeTextureFormats.Recommend(image, _ignoreAlpha, _exportPngCoordinates);
    private NativeTextureFormat Resolve(TextureImageInfo image, NativeTextureFormat format) =>
        NativeTextureFormats.Resolve(image, format, _ignoreAlpha, _exportPngCoordinates);
    private string? Unsuitable(TextureImageInfo image, NativeTextureFormat format) =>
        NativeTextureFormats.Unsuitable(image, format, _ratio, _ignoreAlpha, _exportPngCoordinates);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class TextureFormatChoice(string label) : INotifyPropertyChanged
{
    public string Label { get; private set; } = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Update(string value)
    {
        if (Label == value) return;
        Label = value; PropertyChanged?.Invoke(this, new(nameof(Label)));
    }
}
