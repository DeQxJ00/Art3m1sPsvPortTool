using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private static readonly HashSet<string> CsvFontPixels = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "top", "width", "height", "size", "rubysize", "kerning", "rubykerning",
        "spacetop", "spacemiddle", "spacebottom", "shadow", "rubyshadow", "outline", "rubyoutline"
    };

    private static string ScaleCsv(string text, string path, double ratio, List<int> diagnostics)
    {
        string normalized = path.Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(path);
        // CSV is also used for counts, track lists and arbitrary application data.
        // Only opt in to the engine's image-sidecar and font configuration schemas.
        bool font = normalized.Contains("/fonttext/", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(stem, @"^(?:[a-z]+_)?tag_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (font)
        {
            var output = new StringBuilder(text.Length);
            int lineNumber = 0;
            foreach (string line in SplitLines(text))
            {
                lineNumber++;
                Match m = Regex.Match(line, @"^([ \t]*)([a-zA-Z]+)([ \t]*=[ \t]*)([^\r\n]*?)([ \t]*(?://[^\r\n]*)?)(\r?\n|\r)?$");
                if (!m.Success || !CsvFontPixels.Contains(m.Groups[2].Value)) { output.Append(line); continue; }
                string value = m.Groups[4].Value;
                // Some font tables retain a trailing Lua-style comma. It is
                // punctuation, not a second numeric field or an expression.
                string suffix = value.EndsWith(',') ? "," : "";
                string number = suffix.Length == 0 ? value : value[..^1];
                if (!TryScaleCsvNumber(number, ratio, out string scaled))
                {
                    diagnostics.Add(lineNumber);
                    output.Append(line);
                    continue;
                }
                output.Append(line.AsSpan(0, m.Groups[4].Index)).Append(scaled).Append(suffix)
                    .Append(line.AsSpan(m.Groups[4].Index + m.Groups[4].Length));
            }
            return output.ToString();
        }

        string[] fields = text.Split(',');
        bool assetTree = Regex.IsMatch(normalized, @"/_data/(?:bgimage|fgimage|evimage|face|mask)/", RegexOptions.IgnoreCase);
        bool uiTree = normalized.Contains("/_data/image/", StringComparison.OrdinalIgnoreCase);
        bool pairedImage = new[] { ".png", ".jpg", ".jpeg", ".webp", ".dds" }
            .Any(ext => File.Exists(Path.ChangeExtension(path, ext)));
        bool uiPair = uiTree && Regex.IsMatch(stem,
            @"^(?:btn_.+|chkbox_.+|slider(?:_.+)?|adv_(?:backlogjump|message|name_message|nametag|slot_\d+|voice|vsave)|novel_backlogjump|base_\d+_move|adjust_xy_read\d+|pos_num_(?:\d+|drawing)|slot_message_pos\d+|slot_(?:pos|margin)|slotdel_pos|thumb_(?:pos|new_pos)|offset|face|info|guage|select_align|msg_operation_ios|ok|ok_only|cancel)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int count = fields.Length == 2 && (assetTree || pairedImage || uiPair) ? 2 : 0;
        if (uiTree && stem.Equals("pos", StringComparison.OrdinalIgnoreCase) && fields.Length == 4) count = 2;
        if (uiTree && stem.Equals("slotnum_pos", StringComparison.OrdinalIgnoreCase) && fields.Length == 3) count = 3;
        if (uiTree && stem.Equals("thumb_face", StringComparison.OrdinalIgnoreCase) && fields.Length == 3) count = 2;
        if (count == 0) return text;
        // Reject the entire row if a coordinate is dynamic or contains extra rows.
        var replacements = new string[count];
        for (int i = 0; i < count; i++)
            if (!TryScaleCsvNumber(fields[i], ratio, out replacements[i])) { diagnostics.Add(1); return text; }
        for (int i = 0; i < count; i++) fields[i] = replacements[i];
        return string.Join(',', fields);
    }

    private static bool TryScaleCsvNumber(string value, double ratio, out string result)
    {
        result = value;
        Match m = Regex.Match(value, @"\A(\s*)([+-]?(?:\d+(?:\.\d*)?|\.\d+))(\s*)\z");
        if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
        double scaled = Math.Truncate(n * ratio);
        if (!double.IsFinite(scaled)) return false;
        if (n != 0 && scaled == 0) scaled = Math.Sign(n);
        result = m.Groups[1].Value + scaled.ToString("0", CultureInfo.InvariantCulture) + m.Groups[3].Value;
        return true;
    }
}
