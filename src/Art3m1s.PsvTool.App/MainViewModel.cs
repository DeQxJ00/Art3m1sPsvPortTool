using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Art3m1s.PsvTool.Core;
using Avalonia;
using Avalonia.Styling;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _localizer;
    private readonly IProjectScanner _scanner;
    private readonly IPsbTextureScanner _psbScanner;
    private readonly IConversionService _converter;
    private readonly ISettingsStore _settings;
    private CancellationTokenSource? _conversionCancellation;
    private string _inputDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _ratio = ConversionDefaults.Ratio.ToString(CultureInfo.InvariantCulture);
    private int? _width;
    private int? _height;
    private IReadOnlyList<IniResolution> _resolutions = [];
    private int _selectedResolutionIndex = -1;
    private int _archiveCount;
    private bool _isBusy;
    private bool _overwriteArmed;
    private double _progress;
    private string _status = string.Empty;
    private string _log = string.Empty;
    private bool _text = ConversionDefaults.Categories.HasFlag(AssetCategories.Text);
    private bool _images = ConversionDefaults.Categories.HasFlag(AssetCategories.Images);
    private bool _animation = ConversionDefaults.Categories.HasFlag(AssetCategories.Animation);
    private bool _video = ConversionDefaults.Categories.HasFlag(AssetCategories.Video);
    private bool _ignorePfsVideos = ConversionDefaults.IgnorePfsVideos;
    private bool _convertEmotePsbTexturesToDxt5 = ConversionDefaults.ConvertEmotePsbTextures;
    private bool _subsetFonts = ConversionDefaults.SubsetFonts;
    private int _fontProfile = (int)ConversionDefaults.FontProfile;
    private int _selectedParallel = ConversionDefaults.MaxParallelism;
    private int _selectedEncoding = (int)ConversionDefaults.NameEncoding;

    public MainViewModel(ILocalizer? localizer = null, IProjectScanner? scanner = null, IConversionService? converter = null, ISettingsStore? settings = null,
        IPsbTextureScanner? psbScanner = null)
    {
        _localizer = localizer ?? new Localizer();
        _scanner = scanner ?? new ProjectScanner();
        _psbScanner = psbScanner ?? new PsbTextureScanner();
        _converter = converter ?? new ConversionService();
        _settings = settings ?? new LocalSettingsStore();
        AppSettings saved = _settings.Load();
        IsDark = saved.IsDark;
        _convertEmotePsbTexturesToDxt5 = saved.ConvertEmotePsbTexturesToDxt5;
        _selectedPsbFormat = Enum.IsDefined(saved.PsbOutputFormat) ? (int)saved.PsbOutputFormat : (int)ConversionDefaults.PsbOutputFormat;
        _selectedPsbDxt5Layout = Enum.IsDefined(saved.Dxt5Layout) ? (int)saved.Dxt5Layout : (int)ConversionDefaults.Dxt5Layout;
        _nativeTextures = saved.NativeTextures;
        // Independent texture scaling is a per-run opt-in, never restored from settings.
        _independentPsbTextureScaling = ConversionDefaults.IndependentPsbScaling;
        _psbRenderCompensation = saved.PsbRenderCompensation;
        _localizer.SetLanguage(saved.Language);
        InitializePsb(saved.PsbRatios);
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        _localizer.LanguageChanged += (_, _) => RaiseAllLocalized();
        _status = L("Ready");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string InputDirectory
    {
        get => _inputDirectory;
        set
        {
            if (Set(ref _inputDirectory, value))
            {
                TextureCategories.Clear();
                PsbCategories.Clear();
                ClearPsbSizeEstimate();
                _resolutions = []; _selectedResolutionIndex = -1; _width = _height = null; _archiveCount = 0;
                OnPropertyChanged(nameof(ResolutionChoices)); OnPropertyChanged(nameof(HasResolutionChoices));
                OnPropertyChanged(nameof(SelectedResolutionIndex)); OnPropertyChanged(nameof(OriginalResolution));
                OnPropertyChanged(nameof(TargetResolution)); OnPropertyChanged(nameof(ScanSummary));
            }
            _overwriteArmed = false;
        }
    }
    public string OutputDirectory { get => _outputDirectory; set { Set(ref _outputDirectory, value); _overwriteArmed = false; } }
    public string Ratio { get => _ratio; set { if (Set(ref _ratio, value)) { OnPropertyChanged(nameof(TargetResolution)); RefreshTextures(); RefreshPsbRendering(); RefreshPsbSizeEstimate(); } } }
    public bool ProcessText { get => _text; set => Set(ref _text, value); }
    public bool ProcessImages { get => _images; set => Set(ref _images, value); }
    public bool ProcessAnimation { get => _animation; set { if (Set(ref _animation, value)) { RefreshPsbRendering(); RefreshPsbSizeEstimate(); } } }
    public bool ProcessVideo { get => _video; set => Set(ref _video, value); }
    public bool IgnorePfsVideos { get => _ignorePfsVideos; set => Set(ref _ignorePfsVideos, value); }
    public bool ConvertEmotePsbTexturesToDxt5
    {
        get => _convertEmotePsbTexturesToDxt5;
        set { if (Set(ref _convertEmotePsbTexturesToDxt5, value)) { OnPropertyChanged(nameof(CanSelectPsbDxt5Layout)); RefreshPsbSizeEstimate(); SaveSettings(); } }
    }
    public bool SubsetFonts { get => _subsetFonts; set => Set(ref _subsetFonts, value); }
    public int FontProfile { get => _fontProfile; set => Set(ref _fontProfile, value); }
    public int SelectedParallel { get => _selectedParallel; set => Set(ref _selectedParallel, value); }
    public int SelectedEncoding { get => _selectedEncoding; set { if (Set(ref _selectedEncoding, value)) ClearPsbSizeEstimate(); } }
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); OnPropertyChanged(nameof(CanStart)); } }
    public bool CanStart => !IsBusy;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public string? LogFilePath { get; private set; }
    public string? ErrorLogFilePath { get; private set; }
    public string OriginalResolution => _width is > 0 && _height is > 0 ? $"{_width} × {_height}" : L("Unknown");
    public string ResolutionListLabel => L("ResolutionList");
    public IReadOnlyList<string> ResolutionChoices => _resolutions.Select(item =>
        $"{item.Source} · [{(string.IsNullOrEmpty(item.Section) ? L("IniPreamble") : item.Section)}] · {item.Width} × {item.Height}").ToArray();
    public bool HasResolutionChoices => _resolutions.Count > 0;
    public int SelectedResolutionIndex
    {
        get => _selectedResolutionIndex;
        set
        {
            if (!Set(ref _selectedResolutionIndex, value) || value < 0 || value >= _resolutions.Count) return;
            IniResolution selected = _resolutions[value];
            _width = selected.Width; _height = selected.Height;
            OnPropertyChanged(nameof(OriginalResolution)); OnPropertyChanged(nameof(TargetResolution));
        }
    }
    public string TargetResolution => _width is > 0 && _height is > 0 && TryRatio(out double ratio)
        ? $"{Math.Max(1, (int)(_width.Value * ratio))} × {Math.Max(1, (int)(_height.Value * ratio))}" : L("Unknown");
    public string ScanSummary => _archiveCount == 0 ? L("ScanEmpty") : $"{_archiveCount} {L("Archives")}";
    public string Language => _localizer.Language;
    public bool IsDark { get; private set; } = true;

    public string Title => L("Title"); public string Subtitle => L("Subtitle"); public string ProjectLabel => L("Project");
    public string AssetRightsBackupWarning => L("AssetRightsBackupWarning");
    public string InputLabel => L("Input"); public string OutputLabel => L("Output"); public string BrowseLabel => L("Browse");
    public string ScanLabel => L("Scan"); public string RatioLabel => L("Ratio"); public string RatioHelp => L("RatioHelp");
    public string AutoScanResolutionLabel => L("AutoScanResolution");
    public string OriginalLabel => L("Original"); public string TargetLabel => L("Target"); public string TypesLabel => L("Types");
    public string TextLabel => L("Text"); public string ImagesLabel => L("Images"); public string AnimationLabel => L("Animation");
    public string VideoLabel => L("Video"); public string ModeLabel => L("Mode"); public string ModeHelp => L("ModeHelp");
    public string FontSubsetLabel => L("FontSubset"); public string FontSubsetHelp => L("FontSubsetHelp");
    public IReadOnlyList<string> FontProfiles => [L("Simplified"), L("Japanese"), L("Traditional")];
    public IReadOnlyList<string> ParallelChoices => [L("Auto"), .. Enumerable.Range(1, Math.Max(2, Environment.ProcessorCount)).Select(value => value.ToString(CultureInfo.InvariantCulture))];
    public IReadOnlyList<string> EncodingChoices => [$"{L("Auto")} · UTF-8 / Shift_JIS", "UTF-8", "Shift_JIS"];
    public string AdvancedLabel => L("Advanced"); public string ParallelLabel => L("Parallel"); public string AutoLabel => L("Auto");
    public string EncodingLabel => L("Encoding"); public string LogLabel => L("Log"); public string StartLabel => L("Start");
    public string IgnorePfsVideosLabel => L("IgnorePfsVideos");
    public string ConvertEmotePsbTexturesToDxt5Label => L("ConvertEmotePsbTexturesToDxt5");
    public string ConvertEmotePsbTexturesToDxt5Help => L("ConvertEmotePsbTexturesToDxt5Help");
    public string CancelLabel => L("Cancel"); public string AboutLabel => L("About"); public string AboutBody => L("AboutBody"); public string ProjectRepositoryLabel => L("ProjectRepository"); public string ThemeLabel => L("Theme"); public string ThemeValue => L(IsDark ? "Dark" : "Light");

    public void SetRatio(double ratio) => Ratio = ratio.ToString("0.###", CultureInfo.InvariantCulture);
    public void SetLanguage(string language)
    {
        _localizer.SetLanguage(language);
        SaveSettings();
    }
    public void ToggleTheme()
    {
        IsDark = !IsDark;
        if (Application.Current is not null) Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        OnPropertyChanged(nameof(IsDark)); OnPropertyChanged(nameof(ThemeValue));
        SaveSettings();
    }

    public Task ScanAsync() => ScanCoreAsync(false);
    public Task AutoScanResolutionAsync() => ScanCoreAsync(true);

    private async Task ScanCoreAsync(bool resolutionOnly)
    {
        if (IsBusy) return;
        if (!Directory.Exists(InputDirectory)) { Status = L("InvalidPaths"); return; }
        string input = InputDirectory;
        IsBusy = true; Status = L("Scanning");
        try
        {
            ScanResult result = await _scanner.ScanAsync(input);
            if (input != InputDirectory) { Status = L("ScanInputChanged"); return; }
            _archiveCount = result.Archives.Count;
            var automatic = AutomaticResolution.FromScan(result);
            _resolutions = automatic.Choices;
            int preferred = automatic.SelectedIndex;
            _width = automatic.Width;
            _height = automatic.Height;
            _selectedResolutionIndex = -1;
            OnPropertyChanged(nameof(ResolutionChoices)); OnPropertyChanged(nameof(HasResolutionChoices));
            SelectedResolutionIndex = preferred;
            OnPropertyChanged(nameof(SelectedResolutionIndex));
            if (resolutionOnly && _width is > 0 && _height is > 0)
                Ratio = AutomaticResolution.FitRatio(_width.Value, _height.Value).ToString(CultureInfo.InvariantCulture);
            Status = resolutionOnly
                ? _width is > 0 && _height is > 0
                    ? string.Format(CultureInfo.CurrentUICulture, L("ResolutionDetected"), OriginalResolution)
                    : L("ResolutionNotFound")
                : _archiveCount == 0 ? L("NoPfs") : ScanSummary;
            Log = string.Join(Environment.NewLine, result.Archives.Select(item => $"{item.FileName} · pf{item.Version} · {item.Length:N0} B"));
            OnPropertyChanged(nameof(ScanSummary)); OnPropertyChanged(nameof(OriginalResolution)); OnPropertyChanged(nameof(TargetResolution));
        }
        catch (Exception ex) { Status = ex.Message; RecordFailure(ex, "scan"); }
        finally { IsBusy = false; }
    }

    public async Task StartAsync()
    {
        if (!Directory.Exists(InputDirectory) || string.IsNullOrWhiteSpace(OutputDirectory) || !TryRatio(out double ratio))
        { Status = L("InvalidPaths"); RecordFailure(new ArgumentException(Status), "validation"); return; }
        if (ProcessAnimation && IndependentPsbTextureScaling && PsbCategories.Any(x => !x.TryRatio(out _)))
        { Status = L("PsbInvalidRatio"); return; }
        if (Directory.Exists(OutputDirectory) && !_overwriteArmed)
        { _overwriteArmed = true; Status = L("Overwrite"); return; }

        AssetCategories categories = AssetCategories.None;
        if (ProcessText) categories |= AssetCategories.Text; if (ProcessImages) categories |= AssetCategories.Images;
        if (ProcessAnimation) categories |= AssetCategories.Animation; if (ProcessVideo) categories |= AssetCategories.Video;
        _conversionCancellation = new CancellationTokenSource(); IsBusy = true; Progress = 0; Log = string.Empty;
        string? activeArchive = null;
        string? activeEntry = null;
        ConversionRunLog? runLog = null;
        string outcome = "failed";
        int exitCode = 1;
        try
        {
            runLog = new ConversionRunLog(InputDirectory, OutputDirectory, standardOutput: TextWriter.Null, standardError: TextWriter.Null);
            ShowLogPaths(runLog);
            runLog.Info("OPTIONS", $"ratio={ratio.ToString(CultureInfo.InvariantCulture)}; categories={categories}; native-textures={NativeTextures}; subset-fonts={SubsetFonts}; font-profile={(FontSubsetProfile)FontProfile}; psb-format={(PsbTextureFormat)SelectedPsbFormat}; layout={(PsbDxt5Layout)SelectedPsbDxt5Layout}");
            Progress<ConversionProgress> reporter = new(value =>
            {
                activeArchive = value.Archive;
                activeEntry = value.Entry;
                Progress = value.Percent; Status = LocalizeStage(value.Stage);
                if (!string.IsNullOrWhiteSpace(value.Entry)) Log += value.Entry + Environment.NewLine;
                if (value.Diagnostic is { } diagnostic) Log += $"[{diagnostic.Level}] {diagnostic.Code}: {diagnostic.Message}" + Environment.NewLine;
            });
            var options = new ConversionOptions(InputDirectory, OutputDirectory, ratio, categories, SelectedParallel, (PfsNameEncoding)SelectedEncoding, _overwriteArmed, SubsetFonts, (FontSubsetProfile)FontProfile, IgnorePfsVideos, ConvertEmotePsbTexturesToDxt5, new NativeTextureOptions(NativeTextures, IgnoreBackgroundAlpha, TextureCategories.Select(x => x.Rule).ToArray()), new PsbTextureOptions(Enabled: ConversionDefaults.IndependentPsbScaling, CompensateRendering: ConversionDefaults.PsbRenderCompensation), (PsbTextureFormat)SelectedPsbFormat, (PsbDxt5Layout)SelectedPsbDxt5Layout,
                TargetWidth: _width is > 0 ? Math.Max(1, (int)(_width.Value * ratio)) : null,
                TargetHeight: _height is > 0 ? Math.Max(1, (int)(_height.Value * ratio)) : null);
            var token = _conversionCancellation.Token;
            var loggingReporter = new ConversionProgressRelay(runLog, reporter);
            await Task.Run(() => _converter.ConvertAsync(options, loggingReporter, token), token);
            Status = L("Finished");
            outcome = "success"; exitCode = 0;
            if (runLog.HasWriteFailure) { Status = L("LogWriteFailed"); Log += L("LogWriteFailed") + Environment.NewLine; outcome = "completed-with-log-errors"; exitCode = 2; }
        }
        catch (OperationCanceledException) { Status = L("Cancel"); outcome = "cancelled"; exitCode = 130; }
        catch (ConversionItemException ex)
        {
            Status = $"{L("Failed")}: {ex.Message}";
            RecordFailure(ex, ex.Stage ?? runLog?.LastStage ?? "resource", runLog);
        }
        catch (Exception ex)
        {
            Status = $"{L("Failed")}: {ex.Message}";
            RecordFailure(new ConversionItemException(activeArchive, activeEntry, ex, runLog?.LastStage), runLog?.LastStage ?? "setup", runLog);
        }
        finally { runLog?.Complete(outcome, exitCode); runLog?.Dispose(); _conversionCancellation.Dispose(); _conversionCancellation = null; IsBusy = false; _overwriteArmed = false; }
    }

    public void Cancel() => _conversionCancellation?.Cancel();
    public void ConfigureScreenshotDemo()
    {
        InputDirectory = OperatingSystem.IsWindows() ? @"D:\Games\ArtemisDemo" : "/games/ArtemisDemo";
        OutputDirectory = OperatingSystem.IsWindows() ? @"D:\Games\ArtemisDemo-PSV" : "/games/ArtemisDemo-PSV";
        _width = 1920; _height = 1080; _archiveCount = 5; Ratio = "0.5"; Progress = 68;
        _resolutions = [new("root.pfs/system.ini", "VITA", 960, 540),
            new("root.pfs/system.ini", "WINDOWS", 1920, 1080)];
        _selectedResolutionIndex = 1;
        OnPropertyChanged(nameof(ResolutionChoices)); OnPropertyChanged(nameof(HasResolutionChoices));
        OnPropertyChanged(nameof(SelectedResolutionIndex));
        SubsetFonts = true; FontProfile = 0;
        Status = string.Format(CultureInfo.CurrentUICulture, L("DemoProgress"), "root.pfs.010", 68);
        Log = "root.pfs\nroot.pfs.000\nroot.pfs.001\nroot.pfs.010\nroot.pfs.011";
        OnPropertyChanged(nameof(ScanSummary)); OnPropertyChanged(nameof(OriginalResolution)); OnPropertyChanged(nameof(TargetResolution));
    }
    private bool TryRatio(out double ratio) => double.TryParse(Ratio, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio) && ratio is > 0 and <= 1;
    private string L(string key) => _localizer[key];
    private string LocalizeStage(string stage) => stage switch
    {
        "copy" => L("StageCopy"),
        "extract" => L("StageExtract"),
        "pack" => L("StagePack"),
        "loose" => L("StageLoose"),
        "resource" or "image" or "text" or "font" or "psb" or "video" or "ini" => L("StageResource"),
        "texture-scan" or "psb-scan" => L("Scanning"),
        "complete" => L("StageComplete"),
        _ => stage
    };
    private void ShowLogPaths(ConversionRunLog log)
    {
        LogFilePath = log.LogPath; ErrorLogFilePath = log.ErrorLogPath;
        Log += $"{L("LogFilePath")}: {LogFilePath}" + Environment.NewLine + $"{L("ErrorLogFilePath")}: {ErrorLogFilePath}" + Environment.NewLine;
    }
    private void RecordFailure(Exception exception, string stage, ConversionRunLog? log = null)
    {
        try
        {
            if (log != null) log.Exception(exception, stage);
            else
            {
                using var fallback = ConversionRunLog.Fallback(InputDirectory, TextWriter.Null, TextWriter.Null);
                ShowLogPaths(fallback);
                fallback.Exception(exception, stage); fallback.Complete("failed", 1);
            }
        }
        catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException or ArgumentException)
        { Log += L("LogWriteFailed") + ": " + loggingError.Message + Environment.NewLine; }
        var item = exception as ConversionItemException;
        AppendFailure(item?.Archive, item?.Entry, exception.Message, stage);
        Log += exception.ToString() + Environment.NewLine;
    }
    private void AppendFailure(string? archive, string? entry, string reason, string? stage = null)
    {
        List<string> details = [$"--- {L("FailureDetails")} ---"];
        if (stage != null) details.Add($"{L("ErrorStage")}: {stage}");
        if (!string.IsNullOrWhiteSpace(archive)) details.Add($"{L("ErrorArchive")}: {archive}");
        if (!string.IsNullOrWhiteSpace(entry)) details.Add($"{L("ErrorFile")}: {entry}");
        details.Add($"{L("ErrorReason")}: {reason}");
        if (!string.IsNullOrEmpty(Log) && !Log.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            Log += Environment.NewLine;
        Log += string.Join(Environment.NewLine, details) + Environment.NewLine;
    }
    private void SaveSettings()
    {
        try { _settings.Save(new AppSettings(Language, IsDark, ConvertEmotePsbTexturesToDxt5, NativeTextures, PsbRules, PsbRenderCompensation, (PsbTextureFormat)SelectedPsbFormat, (PsbDxt5Layout)SelectedPsbDxt5Layout)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void RaiseAllLocalized()
    {
        RefreshTextures();
        RefreshPsb();
        foreach (string name in new[] { nameof(NativeTexturesLabel), nameof(NativeTexturesHelp), nameof(ScanTexturesLabel), nameof(IgnoreBackgroundAlphaLabel), nameof(TextureHelp), nameof(TextureFormatsLabel) }) OnPropertyChanged(name);
        foreach (string property in new[] { nameof(Title), nameof(Subtitle), nameof(ProjectLabel), nameof(AssetRightsBackupWarning), nameof(InputLabel), nameof(OutputLabel), nameof(BrowseLabel), nameof(ScanLabel), nameof(AutoScanResolutionLabel), nameof(ResolutionListLabel), nameof(ResolutionChoices), nameof(RatioLabel), nameof(RatioHelp), nameof(OriginalLabel), nameof(TargetLabel), nameof(TypesLabel), nameof(TextLabel), nameof(ImagesLabel), nameof(AnimationLabel), nameof(VideoLabel), nameof(FontSubsetLabel), nameof(FontSubsetHelp), nameof(FontProfiles), nameof(ParallelChoices), nameof(ModeLabel), nameof(ModeHelp), nameof(AdvancedLabel), nameof(ParallelLabel), nameof(AutoLabel), nameof(EncodingLabel), nameof(IgnorePfsVideosLabel), nameof(ConvertEmotePsbTexturesToDxt5Label), nameof(ConvertEmotePsbTexturesToDxt5Help), nameof(LogLabel), nameof(StartLabel), nameof(CancelLabel), nameof(AboutLabel), nameof(AboutBody), nameof(ProjectRepositoryLabel), nameof(ThemeLabel), nameof(ThemeValue), nameof(ScanSummary), nameof(OriginalResolution), nameof(TargetResolution), nameof(Language) }) OnPropertyChanged(property);
    }
}
