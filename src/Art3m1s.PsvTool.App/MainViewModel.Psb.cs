using System.Collections.ObjectModel;
using System.Globalization;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel
{
    private readonly Dictionary<(int Width, int Height), double> _savedPsbRatios = [];
    public ObservableCollection<PsbCategoryViewModel> PsbCategories { get; } = [];
    public bool HasPsbCategories => PsbCategories.Count > 0;
    public string PsbEmptyHelp => L("PsbEmptyHelp");
    public string PsbTitle => L("PsbTitle");
    public string ScanPsbLabel => L("PsbScan");
    public string PsbHelp => L("PsbHelp");
    public string PsbDxt5Title => L("PsbDxt5Title");
    public string PsbDxt5Help => L("PsbDxt5Help");
    public string IndependentPsbTextureScalingLabel => L("IndependentPsbTextureScaling");
    public string PsbRenderCompensationLabel => L("PsbRenderCompensation");
    public string PsbRenderCompensationHelp => L("PsbRenderCompensationHelp");
    private bool _independentPsbTextureScaling;
    private bool _psbRenderCompensation = true;
    public bool IndependentPsbTextureScaling
    {
        get => _independentPsbTextureScaling;
        set { if (Set(ref _independentPsbTextureScaling, value)) RefreshPsbRendering(); }
    }
    public bool PsbRenderCompensation
    {
        get => _psbRenderCompensation;
        set { if (Set(ref _psbRenderCompensation, value)) { RefreshPsbRendering(); SaveSettings(); } }
    }

    private void RefreshPsbRendering()
    {
        foreach (var category in PsbCategories)
            category.UpdateRendering(TryRatio(out double ratio) ? ratio : double.NaN,
                IndependentPsbTextureScaling && PsbRenderCompensation && ProcessAnimation);
    }

    private void InitializePsb(IReadOnlyList<PsbResolutionRule>? saved)
    {
        PsbCategories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPsbCategories));
        foreach (var rule in (saved ?? []).Where(x => x is not null && x.Width > 0 && x.Height > 0 && double.IsFinite(x.Ratio) && x.Ratio is > 0 and <= 1))
            _savedPsbRatios[(rule.Width, rule.Height)] = rule.Ratio;
    }

    private PsbCategoryViewModel AddPsbCategory(int width, int height)
    {
        double ratio = _savedPsbRatios.GetValueOrDefault((width, height), 0.5);
        var category = new PsbCategoryViewModel(width, height, ratio, _localizer);
        category.UpdateRendering(TryRatio(out double geometryRatio) ? geometryRatio : double.NaN,
            IndependentPsbTextureScaling && PsbRenderCompensation && ProcessAnimation);
        category.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PsbCategoryViewModel.Ratio) && category.TryRatio(out double updatedRatio))
            {
                _savedPsbRatios[(width, height)] = updatedRatio;
                SaveSettings();
            }
        };
        int index = 0;
        while (index < PsbCategories.Count &&
            ((long)PsbCategories[index].Width * PsbCategories[index].Height > (long)width * height ||
             (long)PsbCategories[index].Width * PsbCategories[index].Height == (long)width * height && PsbCategories[index].Width > width)) index++;
        PsbCategories.Insert(index, category);
        return category;
    }

    private PsbResolutionRule[] PsbRules
    {
        get
        {
            var rules = new Dictionary<(int Width, int Height), double>(_savedPsbRatios);
            foreach (var category in PsbCategories)
                if (category.TryRatio(out double ratio)) rules[(category.Width, category.Height)] = ratio;
            return rules.Select(x => new PsbResolutionRule(x.Key.Width, x.Key.Height, x.Value)).ToArray();
        }
    }

    private void RefreshPsb()
    {
        foreach (var category in PsbCategories) category.Refresh();
        OnPropertyChanged(nameof(PsbTitle)); OnPropertyChanged(nameof(ScanPsbLabel)); OnPropertyChanged(nameof(PsbHelp));
        OnPropertyChanged(nameof(PsbDxt5Title)); OnPropertyChanged(nameof(PsbDxt5Help));
        OnPropertyChanged(nameof(PsbEmptyHelp));
        OnPropertyChanged(nameof(IndependentPsbTextureScalingLabel));
        OnPropertyChanged(nameof(PsbRenderCompensationLabel)); OnPropertyChanged(nameof(PsbRenderCompensationHelp));
    }

    public async Task ScanPsbAsync()
    {
        if (IsBusy) return;
        if (!Directory.Exists(InputDirectory)) { Status = L("InvalidPaths"); return; }
        string input = InputDirectory; var encoding = (PfsNameEncoding)SelectedEncoding;
        _conversionCancellation = new(); var token = _conversionCancellation.Token;
        IsBusy = true; Progress = 0; Status = L("Scanning");
        try
        {
            var reporter = new Progress<ConversionProgress>(p =>
            { Progress = p.Percent; Status = $"{L("Scanning")} {p.Archive} / {p.Entry}"; });
            var files = await Task.Run(() => _psbScanner.ScanAsync(input, encoding, reporter, token), token);
            if (input != InputDirectory) { Status = L("PsbInputChanged"); return; }
            PsbCategories.Clear();
            foreach (var group in files.Where(x => x.Width > 0 && x.Height > 0).GroupBy(x => (x.Width, x.Height)))
            {
                var category = AddPsbCategory(group.Key.Width, group.Key.Height);
                category.SetFiles(group.ToArray());
            }
            int grouped = files.Count(x => x.Width > 0 && x.Height > 0);
            Status = string.Format(CultureInfo.CurrentUICulture, L("PsbScanSummary"), files.Count,
                PsbCategories.Count(x => x.FileCount > 0), files.Count - grouped);
            Log = string.Join(Environment.NewLine, files.Select(x =>
                $"{(x.Archive is null ? x.Path : x.Archive + " / " + x.Path)} · " +
                (x.Error is not null ? x.Error : x.Width == 0 ? L("PsbNoAtlas") :
                    string.Join("; ", x.Inspection!.Atlases.Select(a => $"{a.Source}: {a.Width} × {a.Height} {a.Format}"))))) + Environment.NewLine;
            Progress = 100; SaveSettings();
        }
        catch (OperationCanceledException) { Status = L("Cancel"); }
        catch (Exception error) { Status = L("Failed") + ": " + error.Message; }
        finally { _conversionCancellation.Dispose(); _conversionCancellation = null; IsBusy = false; }
    }
}
