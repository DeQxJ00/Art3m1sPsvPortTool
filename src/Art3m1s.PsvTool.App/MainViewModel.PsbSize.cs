using System.Globalization;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel
{
    private IReadOnlyList<PsbTextureFileInfo>? _psbSizeFiles;
    private PsbTextureSizeEstimate? _psbSizeEstimate;
    public string CalculatePsbSizeLabel => L("PsbCalculateSize");
    public string PsbSizeHelp => L("PsbSizeHelp");
    public string PsbSizeDetailsLabel => L("PsbSizeDetails");
    public bool HasPsbSizeEstimate => _psbSizeFiles is not null;
    public string PsbSizeSummary => _psbSizeEstimate is { } estimate
        ? string.Format(CultureInfo.InvariantCulture, L("PsbSizeSummary"), estimate.EstimatedFileCount, estimate.FileCount,
            estimate.Textures.Count, FormatPsbBytes(estimate.SourceBytes), FormatPsbBytes(estimate.TargetBytes), estimate.Issues.Count,
            ProcessAnimation ? Ratio : "1", ConvertEmotePsbTexturesToDxt5
                ? PsbTextureFormats.TypeName((PsbTextureFormat)SelectedPsbFormat, (PsbDxt5Layout)SelectedPsbDxt5Layout) : L("PsbSizeKeepFormat"))
        : HasPsbSizeEstimate ? L("PsbInvalidRatio") : string.Empty;
    public string PsbSizeDetails => _psbSizeEstimate is not { } estimate ? string.Empty :
        string.Join(Environment.NewLine, estimate.Textures
            .GroupBy(x => (x.Width, x.Height, x.TargetWidth, x.TargetHeight, x.SourceFormat, x.TargetFormat))
            .OrderByDescending(x => (long)x.Key.Width * x.Key.Height)
            .ThenByDescending(x => x.Key.Width).ThenBy(x => x.Key.SourceFormat, StringComparer.Ordinal)
            .ThenBy(x => x.Key.TargetFormat, StringComparer.Ordinal)
            .Select(g => $"{g.Key.Width} × {g.Key.Height} {g.Key.SourceFormat} → {g.Key.TargetWidth} × {g.Key.TargetHeight} {g.Key.TargetFormat}" +
                $" · ×{g.Count()} · {FormatPsbBytes(g.Sum(x => x.SourceBytes))} → {FormatPsbBytes(g.Sum(x => x.TargetBytes))}")
            .Concat(estimate.Issues.Select(FormatPsbSizeIssue)));

    private string FormatPsbSizeIssue(PsbSizeIssue issue) =>
        $"{(issue.Archive is null ? issue.Path : issue.Archive + " / " + issue.Path)} · {L("PsbSize" + issue.Kind)}" +
        (string.IsNullOrEmpty(issue.Detail) ? string.Empty : ": " + issue.Detail);
    private static string FormatPsbBytes(long bytes) =>
        $"{(bytes / 1048576d).ToString("0.###", CultureInfo.InvariantCulture)} MiB ({bytes.ToString("N0", CultureInfo.InvariantCulture)} B)";

    private void ClearPsbSizeEstimate()
    {
        _psbSizeFiles = null; RefreshPsbSizeEstimate();
    }
    private void RefreshPsbSizeEstimate()
    {
        _psbSizeEstimate = _psbSizeFiles is not null && (!ProcessAnimation || TryRatio(out _))
            ? PsbTextureSizeEstimator.Estimate(_psbSizeFiles, ProcessAnimation && TryRatio(out double ratio) ? ratio : 1,
                ConvertEmotePsbTexturesToDxt5 ? (PsbTextureFormat)SelectedPsbFormat : null, (PsbDxt5Layout)SelectedPsbDxt5Layout)
            : null;
        RaisePsbSizeLabels();
    }
    private void RaisePsbSizeLabels()
    {
        OnPropertyChanged(nameof(CalculatePsbSizeLabel)); OnPropertyChanged(nameof(PsbSizeHelp));
        OnPropertyChanged(nameof(PsbSizeDetailsLabel)); OnPropertyChanged(nameof(HasPsbSizeEstimate));
        OnPropertyChanged(nameof(PsbSizeSummary)); OnPropertyChanged(nameof(PsbSizeDetails));
    }

    public async Task CalculatePsbSizeAsync()
    {
        if (IsBusy) return;
        if (!Directory.Exists(InputDirectory)) { Status = L("InvalidPaths"); return; }
        if (ProcessAnimation && !TryRatio(out _)) { Status = L("PsbInvalidRatio"); return; }
        string input = InputDirectory; var encoding = (PfsNameEncoding)SelectedEncoding;
        using var cancellation = new CancellationTokenSource();
        _conversionCancellation = cancellation; var token = cancellation.Token;
        IsBusy = true; Progress = 0; Status = L("Scanning"); ClearPsbSizeEstimate();
        try
        {
            var reporter = new Progress<ConversionProgress>(p =>
            {
                if (IsBusy && ReferenceEquals(_conversionCancellation, cancellation))
                { Progress = p.Percent; Status = $"{L("Scanning")} {p.Archive} / {p.Entry}"; }
            });
            var files = await Task.Run(() => _psbScanner.ScanAsync(input, encoding, reporter, token), token);
            token.ThrowIfCancellationRequested();
            if (input != InputDirectory || encoding != (PfsNameEncoding)SelectedEncoding)
            { Status = L("PsbInputChanged"); return; }
            _psbSizeFiles = files; RefreshPsbSizeEstimate();
            Status = L("PsbSizeFinished"); Progress = 100;
            if (_psbSizeEstimate is { Issues.Count: > 0 } estimate)
                Log += string.Join(Environment.NewLine, estimate.Issues.Select(FormatPsbSizeIssue)) + Environment.NewLine;
        }
        catch (OperationCanceledException) { Status = L("Cancel"); }
        catch (Exception error) { Status = L("Failed") + ": " + error.Message; }
        finally { _conversionCancellation = null; IsBusy = false; }
    }
}
