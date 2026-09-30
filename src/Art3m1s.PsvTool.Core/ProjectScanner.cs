using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public interface IProjectScanner
{
    Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default);
}

public sealed partial class ProjectScanner : IProjectScanner
{
    public async Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException(inputDirectory);

        List<PfsFileInfo> archives = [];
        foreach (string file in Directory.EnumerateFiles(inputDirectory, "*", SearchOption.AllDirectories)
                     .Where(IsPfsName)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = File.OpenRead(file);
            byte[] header = new byte[3];
            if (await stream.ReadAsync(header, cancellationToken) != header.Length ||
                header[0] != (byte)'p' || header[1] != (byte)'f' || header[2] is not ((byte)'2' or (byte)'6' or (byte)'8'))
                continue;
            archives.Add(new PfsFileInfo(file, Path.GetRelativePath(inputDirectory, file).Replace('\\', '/'), (char)header[2], stream.Length));
        }

        string systemIni = Path.Combine(inputDirectory, "system.ini");
        bool hasSystemIni = File.Exists(systemIni);
        List<IniResolution> resolutions = [];
        IniResolution? preferred = null;
        if (hasSystemIni)
        {
            IReadOnlyList<IniResolution> loose = ReadResolutions(
                await File.ReadAllBytesAsync(systemIni, cancellationToken), "system.ini");
            resolutions.AddRange(loose);
            preferred = PickPreferred(loose);
        }
        PfsCodec codec = new();
        foreach (PfsFileInfo archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? contents = await codec.ReadSmallEntryAsync(archive.Path, "system.ini", cancellationToken);
            if (contents is null) continue;
            hasSystemIni = true;
            IReadOnlyList<IniResolution> found = ReadResolutions(contents, $"{archive.FileName}/system.ini");
            resolutions.AddRange(found);
            preferred ??= PickPreferred(found);
        }

        return new ScanResult(archives, hasSystemIni, preferred?.Width, preferred?.Height, resolutions);
    }

    private static bool IsPfsName(string path) => PfsNameRegex().IsMatch(Path.GetFileName(path));

    private static IniResolution? PickPreferred(IReadOnlyList<IniResolution> resolutions) =>
        resolutions.FirstOrDefault(item => item.Section.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
        ?? resolutions.FirstOrDefault();

    private static IReadOnlyList<IniResolution> ReadResolutions(byte[] bytes, string source)
    {
        string text = TextEncoding.Detect(bytes).Encoding.GetString(bytes).TrimStart('\uFEFF');
        List<IniResolution> resolutions = [];
        string section = string.Empty;
        int? width = null, height = null;
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line)
        {
            Match heading = SectionRegex().Match(line);
            if (heading.Success)
            {
                section = heading.Groups[1].Value.Trim();
                width = height = null;
                continue;
            }
            Match widthMatch = WidthRegex().Match(line);
            if (widthMatch.Success)
                width = int.TryParse(widthMatch.Groups[1].Value, out int widthValue) && widthValue > 0 ? widthValue : null;
            Match heightMatch = HeightRegex().Match(line);
            if (heightMatch.Success)
                height = int.TryParse(heightMatch.Groups[1].Value, out int heightValue) && heightValue > 0 ? heightValue : null;
            if (width is > 0 && height is > 0)
            {
                resolutions.Add(new IniResolution(source, section, width.Value, height.Value));
                width = height = null;
            }
        }
        return resolutions;
    }

    [GeneratedRegex(@"^.+\.pfs(?:\.[^.]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PfsNameRegex();

    [GeneratedRegex(@"(?im)^[ \t]*\[([^\]\r\n]+)\][ \t]*(?:[;#].*)?$")]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"(?im)^[ \t]*WIDTH[ \t]*=[ \t]*(\d+)(?=[ \t]*(?:[;#]|$))")]
    private static partial Regex WidthRegex();

    [GeneratedRegex(@"(?im)^[ \t]*HEIGHT[ \t]*=[ \t]*(\d+)(?=[ \t]*(?:[;#]|$))")]
    private static partial Regex HeightRegex();
}

internal static class TextEncoding
{
    static TextEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static (Encoding Encoding, bool HasBom) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true, true), true);
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return (new UTF8Encoding(false, true), false);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), false);
        }
    }
}
