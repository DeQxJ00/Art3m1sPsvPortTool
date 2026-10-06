using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed class PsbCategoryViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _localizer;
    private string _ratio;
    private IReadOnlyList<PsbTextureFileInfo> _files = [];
    private double _geometryRatio = .5;
    private bool _compensationEnabled;
    public PsbCategoryViewModel(int width, int height, double ratio, ILocalizer localizer)
    {
        Width = width; Height = height; _ratio = ratio.ToString(CultureInfo.InvariantCulture);
        _localizer = localizer;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Width { get; }
    public int Height { get; }
    public string Label => $"{Width} × {Height}";
    public string Ratio
    {
        get => _ratio;
        set
        {
            if (_ratio == value) return;
            _ratio = value; Changed(); Changed(nameof(Preview)); Changed(nameof(Validation)); Changed(nameof(HasValidationError)); Changed(nameof(SelectedPreset)); Changed(nameof(MemoryComparison)); Changed(nameof(RenderCompensation));
        }
    }
    public IReadOnlyList<string> Presets { get; } = ["1", "0.75", "0.5", "0.375", "0.25"];
    public int SelectedPreset
    {
        get => Presets.ToList().IndexOf(Ratio);
        set { if (value >= 0 && value < Presets.Count) Ratio = Presets[value]; }
    }
    public bool TryRatio(out double ratio) => double.TryParse(Ratio, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio)
        && double.IsFinite(ratio) && ratio is > 0 and <= 1;
    public string Preview => TryRatio(out double ratio)
        ? $"→ {Math.Max(1, (int)(Width * ratio))} × {Math.Max(1, (int)(Height * ratio))}" : "—";
    public string Validation => TryRatio(out _) ? string.Empty : _localizer["PsbInvalidRatio"];
    public string MemoryComparison
    {
        get
        {
            if (!TryRatio(out double ratio)) return "—";
            var estimate = PsbTextureMemory.Estimate(Math.Max(1, (int)(Width * ratio)), Math.Max(1, (int)(Height * ratio)));
            if (estimate is null) return _localizer["PsbMemoryUnsupported"];
            static string MiB(long bytes) => (bytes / 1048576d).ToString("0.###", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.CurrentUICulture, _localizer["PsbMemoryComparison"],
                MiB(estimate.RgbaAllocationBytes), MiB(estimate.Dxt5AllocationBytes), MiB(estimate.Dxt5DataBytes));
        }
    }
    public bool HasValidationError => !TryRatio(out _);
    public string RenderCompensation => !_compensationEnabled ? _localizer["PsbCompensationOff"] :
        TryRatio(out double ratio) && double.IsFinite(_geometryRatio)
            ? string.Format(CultureInfo.CurrentUICulture, _localizer["PsbCompensationPreview"],
                (_geometryRatio / ratio).ToString("0.###", CultureInfo.InvariantCulture),
                _geometryRatio.ToString("0.###", CultureInfo.InvariantCulture), Ratio) : "—";
    public void UpdateRendering(double geometryRatio, bool enabled)
    {
        _geometryRatio = geometryRatio; _compensationEnabled = enabled; Changed(nameof(RenderCompensation));
    }
    public int FileCount => _files.Count;
    public bool HasFiles => FileCount > 0;
    public string Count => string.Format(CultureInfo.CurrentUICulture, _localizer["PsbCount"], _files.Count,
        _files.Sum(x => x.Inspection?.Atlases.Count ?? 0));
    public string DetailsLabel => _localizer["PsbDetails"];
    public IReadOnlyList<string> FileRows => _files.Select(x =>
        $"{(x.Archive is null ? x.Path : x.Archive + " / " + x.Path)} · " +
        string.Join("; ", x.Inspection!.Atlases.Select(a => $"{a.Source}: {a.Width} × {a.Height} {a.Format}"))).ToArray();
    public void SetFiles(IReadOnlyList<PsbTextureFileInfo> files) { _files = files; Refresh(); }
    public void Refresh()
    {
        Changed(nameof(Count)); Changed(nameof(FileCount)); Changed(nameof(HasFiles)); Changed(nameof(DetailsLabel)); Changed(nameof(Validation)); Changed(nameof(FileRows)); Changed(nameof(MemoryComparison)); Changed(nameof(RenderCompensation));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
