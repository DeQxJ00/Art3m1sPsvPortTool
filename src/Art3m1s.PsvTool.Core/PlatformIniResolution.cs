using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

// Unify output canvases only. Script coordinate systems retain their own scaling rules.
internal static class PlatformIniResolution
{
    private static readonly HashSet<string> Platforms = new(StringComparer.OrdinalIgnoreCase)
        { "WINDOWS", "ANDROID", "IOS", "WASM", "SWITCH", "PS4", "VITA" };

    public static async Task ApplyAsync(string path, int width, int height, CancellationToken token)
    {
        EncodedText encoded = EncodedText.Decode(await File.ReadAllBytesAsync(path, token));
        string newline = encoded.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool platform = false, hasWidth = false, hasHeight = false;
        StringBuilder output = new();
        void CompleteSection()
        {
            if (!platform || hasWidth && hasHeight) return;
            if (output.Length > 0 && output[^1] != '\n' && output[^1] != '\r') output.Append(newline);
            if (!hasWidth) output.Append($"WIDTH = {width}{newline}");
            if (!hasHeight) output.Append($"HEIGHT = {height}{newline}");
        }
        foreach (Match part in Regex.Matches(encoded.Text, @"[^\r\n]*(?:\r\n|\r|\n|$)"))
        {
            if (part.Length == 0) continue;
            string line = part.Value;
            Match section = Regex.Match(line, @"^\s*\[([^\]\r\n]+)\]");
            if (section.Success)
            {
                CompleteSection();
                platform = Platforms.Contains(section.Groups[1].Value.Trim());
                hasWidth = hasHeight = false;
            }
            if (platform)
            {
                Match size = Regex.Match(line, @"^([ \t]*)(WIDTH|HEIGHT)([ \t]*=[ \t]*)(\d+)(?=[ \t]*(?:[;#]|\r|\n|$))", RegexOptions.IgnoreCase);
                if (size.Success)
                {
                    bool isWidth = size.Groups[2].Value.Equals("WIDTH", StringComparison.OrdinalIgnoreCase);
                    if (isWidth) hasWidth = true; else hasHeight = true;
                    Group value = size.Groups[4];
                    line = line[..value.Index] + (isWidth ? width : height) + line[(value.Index + value.Length)..];
                }
            }
            output.Append(line);
        }
        CompleteSection();
        string result = output.ToString();
        if (result != encoded.Text) await File.WriteAllBytesAsync(path, encoded.Encode(result), token);
    }
}
