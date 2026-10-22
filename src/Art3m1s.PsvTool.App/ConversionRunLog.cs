using System.Diagnostics;
using System.Text;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

internal sealed class ConversionRunLog : IProgress<ConversionProgress>, IDisposable
{
    private readonly object _gate = new();
    private readonly TextWriter _output, _error;
    private readonly bool _verbose, _persist;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private StreamWriter? _main, _issues;
    private DateTime _lastConsole;
    private int _warnings, _errors;
    private bool _completed, _disposed;
    public string Game { get; }
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string LogPath { get; }
    public string ErrorLogPath { get; }
    public string LastStage { get; private set; } = "setup";
    public bool HasWriteFailure { get; private set; }

    public ConversionRunLog(string input, string output, string? logPath = null, bool persist = true,
        bool verbose = false, TextWriter? standardOutput = null, TextWriter? standardError = null)
    {
        _output = standardOutput ?? Console.Out; _error = standardError ?? Console.Error;
        _verbose = verbose; _persist = persist;
        Game = GameName(input);
        string file = $"{Game}-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-{RunId}.log";
        LogPath = Path.GetFullPath(logPath ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "logs", Game, file));
        ErrorLogPath = Path.Combine(Path.GetDirectoryName(LogPath)!, Path.GetFileNameWithoutExtension(LogPath) + ".errors.log");
        ValidatePaths(input, output, LogPath, ErrorLogPath);
        if (persist) _main = Open(LogPath);
        Write(ConversionLogLevel.Info, "RUN_START", $"version={typeof(ConversionRunLog).Assembly.GetName().Version}; input={input}; output={output}; log={LogPath}; errors={ErrorLogPath}; dry-run={!persist}");
    }

    public static string GameName(string? input)
    {
        try
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(input ?? ""));
            if (string.IsNullOrWhiteSpace(name)) return "command-line";
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        }
        catch (ArgumentException) { return "command-line"; }
    }

    public static ConversionRunLog Fallback(string? input, TextWriter? output = null, TextWriter? error = null, string? directory = null)
    {
        string root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "art3m1s_psv_port_tool", "logs");
        string source = string.IsNullOrWhiteSpace(input) ? "command-line" : input;
        try { _ = Path.GetFullPath(source); }
        catch (ArgumentException) { source = "command-line"; }
        return new(source, Path.Combine(root, "fallback-output"),
            Path.Combine(root, GameName(input), $"{GameName(input)}-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log"),
            standardOutput: output, standardError: error);
    }

    public void Info(string code, string message) => Write(ConversionLogLevel.Info, code, message, LastStage);

    public void Report(ConversionProgress value)
    {
        lock (_gate)
        {
            LastStage = value.Stage;
            bool console = value.Diagnostic != null || _verbose || value.Percent >= 100 ||
                DateTime.UtcNow - _lastConsole >= TimeSpan.FromSeconds(5);
            if (console) _lastConsole = DateTime.UtcNow;
            Write(value.Diagnostic?.Level ?? ConversionLogLevel.Info, value.Diagnostic?.Code ?? "PROGRESS",
                value.Diagnostic?.Message ?? FormattableString.Invariant($"{value.Percent:F1}% {value.Entry}"),
                value.Stage, value.Archive, value.ResourcePath ?? value.Entry, console);
        }
    }

    public void Exception(Exception exception, string? stage = null)
    {
        ConversionItemException? item = exception as ConversionItemException;
        // The complete chain and FFmpeg stderr are persisted even without --verbose.
        Write(ConversionLogLevel.Error, "CONVERSION_FAILED", exception.ToString(), item?.Stage ?? stage ?? LastStage,
            item?.Archive, item?.Entry, console: false);
        _error.WriteLine($"ERROR game={Quote(Game)}; stage={Quote(item?.Stage ?? stage ?? LastStage)}; archive={Quote(item?.Archive ?? "<loose>")}; file={Quote(item?.Entry ?? "<none>")}; {exception.Message}; errors={ErrorLogPath}");
    }

    public void Write(ConversionLogLevel level, string code, string message, string stage = "setup",
        string? archive = null, string? entry = null, bool console = true)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (level == ConversionLogLevel.Warning) _warnings++;
            if (level == ConversionLogLevel.Error) _errors++;
            string prefix = $"{DateTimeOffset.Now:O} [{level.ToString().ToUpperInvariant()}] game={Quote(Game)} run={RunId} code={code} stage={Quote(stage)} archive={Quote(archive ?? "<loose>")} file={Quote(entry ?? "<none>")} ";
            foreach (string detail in message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                string line = prefix + detail;
                try
                {
                    _main?.WriteLine(line);
                    if (_persist && level != ConversionLogLevel.Info)
                    {
                        _issues ??= Open(ErrorLogPath);
                        _issues.WriteLine(line);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    HasWriteFailure = true;
                    _error.WriteLine($"LOG_WRITE_FAILED game={Quote(Game)} path={LogPath}: {ex.Message}");
                }
                if (console) (level == ConversionLogLevel.Error ? _error : _output).WriteLine(line);
            }
        }
    }

    public void Complete(string outcome, int exitCode)
    {
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            Info("RUN_END", FormattableString.Invariant($"outcome={outcome}; exit-code={exitCode}; elapsed-seconds={_elapsed.Elapsed.TotalSeconds:F3}; warning-events={_warnings}; error-events={_errors}; log-write-failed={HasWriteFailure}"));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!_completed) Complete("aborted", 1);
            _disposed = true;
            foreach (var writer in new[] { _main, _issues })
                try { writer?.Dispose(); }
                catch (IOException ex) { HasWriteFailure = true; _error.WriteLine("LOG_CLOSE_FAILED: " + ex.Message); }
        }
    }

    private static StreamWriter Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    }
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
    private static void ValidatePaths(string input, string output, params string[] logs)
    {
        foreach (string path in logs)
        {
            foreach (string root in new[] { input, output })
            {
                string relative = Path.GetRelativePath(Path.GetFullPath(root), path);
                if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException("Logs must be outside input/output directories: " + path);
            }
            for (string? ancestor = path; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Log paths must not contain symbolic links/junctions: " + ancestor);
        }
    }
}

internal sealed class ConversionProgressRelay(IProgress<ConversionProgress> log, IProgress<ConversionProgress> ui) : IProgress<ConversionProgress>
{
    public void Report(ConversionProgress value) { log.Report(value); ui.Report(value); }
}
