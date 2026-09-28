using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public interface ITextProcessor
{
    Task ProcessAsync(string path, double ratio, CancellationToken cancellationToken = default);
}

public interface IVitaIniProcessor
{
    Task EnsureVitaSectionAsync(string path, CancellationToken cancellationToken = default);
}

public sealed partial class ArtemisTextProcessor : ITextProcessor
{
    private static readonly string[] TblListKeys = ["game_scale", "game_wasmbar", "fontsize", "line_size", "line_window", "line_back", "line_scroll", "line_name01", "line_name02"];
    private static readonly string[] TblScalarKeys = ["x", "y", "w", "h", "r", "cx", "cy", "cw", "ch", "fx", "fy", "fw", "fh", "left", "top", "size", "width", "height", "spacetop", "spacemiddle", "spacebottom", "kerning", "rubysize"];
    private static readonly string[] TblClipKeys = ["clip", "clip_a", "clip_c", "clip_d"];
    private static readonly string[] IptKeys = ["x", "y", "w", "h", "ax", "ay"];
    private static readonly string[] AstKeys = ["mx", "my", "ax", "ay", "bx", "by", "x", "y", "x2", "y2"];
    private static readonly string[] LuaKeys = ["width", "height", "left", "top", "x", "y"];

    public async Task ProcessAsync(string path, double ratio, CancellationToken cancellationToken = default)
    {
        await ProcessWithDiagnosticsAsync(path, ratio, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ProcessWithDiagnosticsAsync(string path, double ratio, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio));
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        EncodedText encoded = EncodedText.Decode(bytes);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        List<int> preservedExpressions = [];
        string output = extension switch
        {
            ".ini" => ScaleIni(encoded.Text, ratio),
            ".tbl" or ".ipt" or ".ast" or ".lua" => ScaleScript(encoded.Text, ratio, extension, preservedExpressions),
            _ => encoded.Text
        };
        if (!ReferenceEquals(output, encoded.Text) && output != encoded.Text)
            await File.WriteAllBytesAsync(path, encoded.Encode(output), cancellationToken);
        return preservedExpressions.Distinct().Order().ToArray();
    }

    private static string ScaleIni(string text, double ratio)
    {
        StringBuilder result = new(text.Length);
        bool inVita = false;
        bool insertedSavePath = false;
        foreach (string line in SplitLines(text))
        {
            Match section = SectionRegex().Match(line);
            if (section.Success)
                inVita = section.Groups[1].Value.Equals("VITA", StringComparison.OrdinalIgnoreCase);
            string transformed = line;
            if (!inVita)
            {
                Match size = Regex.Match(transformed, @"^(WIDTH|HEIGHT)(\s*=\s*)(\d+)(?=\s*(?:;|$))", RegexOptions.CultureInvariant);
                if (size.Success)
                    transformed = size.Groups[1].Value + size.Groups[2].Value + ScaleInteger(size.Groups[3].Value, ratio) + transformed[(size.Groups[3].Index + size.Groups[3].Length)..];
                Match savePath = Regex.Match(transformed, @"^(;?)(SAVEPATH.*)", RegexOptions.CultureInvariant);
                if (savePath.Success)
                {
                    if (savePath.Groups[1].Value.Length != 0)
                    {
                        if (!insertedSavePath)
                        {
                            transformed = "SAVEPATH = savedataHD\r\n";
                            insertedSavePath = true;
                        }
                    }
                    else
                    {
                        transformed = ";" + transformed;
                    }
                }
            }
            result.Append(transformed);
        }
        return result.ToString();
    }

    private static string ScaleInteger(string value, double ratio) =>
        ((int)(int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) * ratio)).ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> SplitLines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            int end = i + 1;
            if (text[i] == '\r' && end < text.Length && text[end] == '\n') end++;
            yield return text[start..end];
            i = end - 1;
            start = end;
        }
        if (start < text.Length) yield return text[start..];
    }

    [GeneratedRegex(@"^\s*\[([^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();
}

public sealed partial class VitaIniProcessor : IVitaIniProcessor
{
    public async Task EnsureVitaSectionAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        EncodedText encoded = EncodedText.Decode(bytes);
        if (VitaSectionRegex().IsMatch(encoded.Text))
            return;

        string charset = FindCharset(WindowsSectionRegex().Match(encoded.Text).Groups[1].Value)
            ?? FindCharset(encoded.Text)
            ?? "Shift_JIS";
        string separator = encoded.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string prefix = encoded.Text.Length == 0 || encoded.Text.EndsWith('\n') || encoded.Text.EndsWith('\r') ? string.Empty : separator;
        string block = VitaTemplate.ReplaceLineEndings("\n").Replace("\n", separator, StringComparison.Ordinal).Replace("{CHARSET}", charset, StringComparison.Ordinal);
        await File.WriteAllBytesAsync(path, encoded.Encode(encoded.Text + prefix + block), cancellationToken);
    }

    private static string? FindCharset(string text)
    {
        Match match = CharsetRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private const string VitaTemplate = """
        [VITA]

        ; 文字コード
        CHARSET = {CHARSET}

        ; ステージの幅
        WIDTH = 960
        ; ステージの高さ
        HEIGHT = 540

        ; ステージと液晶パネルの縦横比が一致しない場合に
        ; はみ出した部分をカットしてフィットさせるか否か
        SIDECUT = 0

        ; 最初に読み込むスクリプト
        BOOT = system/first.iet

        ; フォントキャッシュ（本文とバックログのパフォーマンス要チェック）
        ;FONT_CACHE_SIZE = 50331648
        ;FONT_CACHE_SIZE = 33554432
        FONT_CACHE_SIZE = 25165824
        ;FONT_CACHE_SIZE = 16777216
        ;FONT_CACHE_SIZE = 14680064
        ;FONT_CACHE_SIZE = 67108864
        """;

    [GeneratedRegex(@"(?im)^\s*\[VITA\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex VitaSectionRegex();
    [GeneratedRegex(@"(?ims)^\s*\[WINDOWS\]\s*(.*?)(?=^\s*\[|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsSectionRegex();
    [GeneratedRegex(@"(?im)^\s*CHARSET\s*=\s*([^;\r\n]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CharsetRegex();
}

internal sealed record EncodedText(string Text, Encoding Encoding, byte[] Preamble)
{
    public static EncodedText Decode(byte[] bytes)
    {
        (Encoding encoding, bool hasBom) = TextEncoding.Detect(bytes);
        byte[] preamble = hasBom ? encoding.GetPreamble() : [];
        int offset = hasBom ? preamble.Length : 0;
        return new EncodedText(encoding.GetString(bytes, offset, bytes.Length - offset), encoding, preamble);
    }

    public byte[] Encode(string text)
    {
        byte[] body = Encoding.GetBytes(text);
        if (Preamble.Length == 0) return body;
        byte[] result = new byte[Preamble.Length + body.Length];
        Preamble.CopyTo(result, 0);
        body.CopyTo(result, Preamble.Length);
        return result;
    }
}
