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

    private static string ScaleCsv(string text, string path, double ratio, List<int> diagnostics,
        IReadOnlySet<string>? resourcePaths = null, string? relativePath = null)
    {
        string normalized = path.Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(path);
        // CSV is also used for counts, track lists and arbitrary application data.
        // Only opt in to the engine's image-sidecar and font configuration schemas.
        bool font = normalized.Contains("/fonttext/", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(stem, @"^(?:[a-z0-9]+_)?tag_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

        // Non-pixel schemas take precedence, even if a same-name resource exists.
        string key = stem.Trim().ToLowerInvariant();
        if (key is "slot_matrix" or "info" or "default" or "soundlist" or "bgm" or "trial_end_bgm"
            || key.EndsWith("slot_message_textsize", StringComparison.Ordinal)
            || key is "adv_slotnum" or "adv_overdraw" or "novel_overdraw" or "thumb_face_using"
                or "adv_nametag_fontcolor" or "pos_num_drawing_time" or "pos_num_drawing_mode"
                or "select_align_x" or "font_arrign" or "info_move" or "bg_menu_move" or "tipszoom") return text;

        if (TryScaleCsvRectangles(text, ratio, out string rectangles)) return rectangles;
        string[] fields = text.Split(',');
        int[]? columns = null;
        if (key == "slotnum_pos" && fields.Length == 3) columns = [0, 1, 2];
        if ((key is "slotnum_pos" or "pos") && fields.Length == 4
            && fields[2].Trim() is "left" or "center" or "right")
            columns = [0, 1, 3];
        if (key is "thumb_face" or "adv_face_" && fields.Length == 3) columns = [0, 1];
        if (key == "pos" && fields.Length == 4 && double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) columns = [0, 1];
        if (key is "pos_num_arrign" or "pos_num_drawing_arrign" && fields.Length == 2
            && fields[0].Trim() is "left" or "center" or "right") columns = [1];
        if (key == "btnmove" && fields.Length == 5 && fields[0].Trim() is "left" or "top"
            && Regex.IsMatch(fields[2].Trim(), @"\A[a-zA-Z_]+\z")
            && Regex.IsMatch(fields[4].Trim(), @"\A[a-zA-Z_]+\z")) columns = [1, 3];
        if (Regex.IsMatch(key, @"\Abase_\d+_move\z") && fields.Length == 2)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                Match m = Regex.Match(fields[i], @"\A(\s*)(\+\+|--)?([+-]?\d+(?:\.\d+)?)(\+\+|--)?(\s*)\z");
                if (!m.Success || (m.Groups[2].Success && m.Groups[4].Success)
                    || !TryScaleCsvNumber(m.Groups[3].Value, ratio, out string value))
                { diagnostics.Add(1); return text; }
                fields[i] = m.Groups[1].Value + m.Groups[2].Value + value + m.Groups[4].Value + m.Groups[5].Value;
            }
            return string.Join(',', fields);
        }
        if (columns != null) return ScaleCsvColumns(text, fields, columns, ratio, diagnostics);

        // Explicit engine layout schemas can exist independently of image sidecars.
        bool layout = Regex.IsMatch(key,
            @"\A(?:slot_pos|slot_margin|slotdel_pos|slot_message_pos\d+|thumb_pos|thumb_new_pos|streaming[01]_pos|select_align|adjust_xy_read[01]|size|offset|bg_move|pos_num_(?:\d+|drawing)|(?:adv|novel)_(?:message|nametag|name_message|backlogjump|sceneback|voice(?:_add)?|vsave(?:_add)?|slot_\d+))\z");
        layout |= Regex.IsMatch(key, @"\A(?:(?:[a-z0-9]+_)?(?:btn|sysbtn|chkbox)_.+|slider(?:_.+)?|mw_.+|face|mask|ok|ok_only|cancel)\z");
        bool paired = HasCsvCompanion(path, resourcePaths, relativePath);
        if (fields.Length == 2 && (paired || layout))
            return ScaleCsvColumns(text, fields, [0, 1], ratio, diagnostics);
        if (paired && Regex.IsMatch(text, @"\A\s*[+-]?\d+\s*:\s*[+-]?\d+\s*\z"))
        {
            fields = text.Split(':');
            if (TryScaleCsvNumber(fields[0], ratio, out string x) && TryScaleCsvNumber(fields[1], ratio, out string y))
                return x + ":" + y;
        }
        if (paired || layout) diagnostics.Add(1);
        return text;
    }

    private static string ScaleCsvColumns(string original, string[] fields, int[] columns,
        double ratio, List<int> diagnostics)
    {
        foreach (int i in columns)
        {
            if (!TryScaleCsvNumber(fields[i], ratio, out string scaled)) { diagnostics.Add(1); return original; }
            fields[i] = scaled;
        }
        return string.Join(',', fields);
    }

    private static bool TryScaleCsvRectangles(string text, double ratio, out string result)
    {
        result = text;
        var output = new StringBuilder(text.Length);
        int rows = 0;
        bool named = false;
        foreach (string line in SplitLines(text))
        {
            if (string.IsNullOrWhiteSpace(line)) { output.Append(line); continue; }
            string[] fields = line.Split(',');
            if (fields.Length != 5 || !Regex.IsMatch(fields[0], @"\A[\p{L}\p{N}_./-]+\z")) return false;
            rows++;
            named |= Regex.IsMatch(fields[0], @"[\p{L}_]");
            for (int i = 1; i < 5; i++)
            {
                if (!TryScaleCsvNumber(fields[i], ratio, out string scaled)) return false;
                if (i >= 3 && (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double size) || size <= 0)) return false;
                fields[i] = scaled;
            }
            output.AppendJoin(',', fields);
        }
        // A lone numeric row is ambiguous; require a named, multi-row rectangle table.
        if (!named || rows < 2) return false;
        result = output.ToString();
        return true;
    }

    private static bool HasCsvCompanion(string path,IReadOnlySet<string>? resourcePaths=null,string? relativePath=null)
    {
        if(resourcePaths is not null && relativePath is not null)
        {
            string target=Path.ChangeExtension(relativePath.Replace('\\','/'),null);
            return resourcePaths.Any(p=>p.StartsWith(target+"/",StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(p).Equals(".csv",StringComparison.OrdinalIgnoreCase) &&
                Path.ChangeExtension(p,null).Equals(target,StringComparison.OrdinalIgnoreCase));
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string stem = Path.GetFileNameWithoutExtension(path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Directory.Exists(entry))
            {
                if (Path.GetFileName(entry).Equals(stem, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (!Path.GetExtension(entry).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(entry).Equals(stem, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
