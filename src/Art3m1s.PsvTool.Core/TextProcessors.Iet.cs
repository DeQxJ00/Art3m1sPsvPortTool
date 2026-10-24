using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private static string ScaleIet(string text, double ratio, List<int> preserved)
    {
        StringBuilder result = new();
        bool lua = false;
        int lineNumber = 0;
        foreach (string line in SplitLines(text))
        {
            lineNumber++;
            string trimmed = line.TrimStart();
            if (Regex.IsMatch(trimmed, @"^\[lua\s*\]", RegexOptions.IgnoreCase)) lua = true;
            if (Regex.IsMatch(trimmed, @"^\[endlua\s*\]", RegexOptions.IgnoreCase)) lua = false;
            if (lua || !trimmed.StartsWith('[')) { result.Append(line); continue; }

            // Match complete tags, respecting quoted closing brackets. Only
            // engine geometry commands have known pixel units; macros do not.
            int comment = line.Length;
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                if (quote != '\0')
                {
                    if (line[i] == '\\') i++;
                    else if (line[i] == quote) quote = '\0';
                }
                else if (line[i] is '\'' or '"') quote = line[i];
                else if (line[i] == ';' || line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                { comment = i; break; }
            }
            string converted = Regex.Replace(line[..comment],
                "\\[(?<tag>[A-Za-z_][A-Za-z_0-9]*)(?<args>(?:[^\\]\\\"']|\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*')*)\\]",
                tag =>
                {
                    if (tag.Groups["tag"].Value.ToLowerInvariant() is not
                        ("tween" or "systween" or "lyprop" or "lyc" or "lyc2")) return tag.Value;
                    // The lexer protects strings and comments; whitespace is
                    // the attribute separator in IET, unlike Lua tables.
                    var tokens = LexScript(tag.Value);
                    var edits = new List<ScriptEdit>();
                    for (int i = 0; i + 2 < tokens.Count; i++)
                    {
                        string key = tokens[i].Text;
                        if (tokens[i].Kind != 'i' || tokens[i + 1].Text != "=" ||
                            key is not ("x" or "y" or "left" or "top" or "width" or "height" or "anchorx" or "anchory")) continue;
                        int start = i + 2, end = start + 1;
                        if (tokens[start].Text == "-" && end < tokens.Count) end++;
                        bool quoted = tokens[start].Kind == 's';
                        bool boundary = end < tokens.Count && (tokens[end].Text == "]" ||
                            end + 1 < tokens.Count && tokens[end].Kind == 'i' && tokens[end + 1].Text == "=");
                        string value = tag.Value[tokens[start].Start..tokens[end - 1].End];
                        string numberList = quoted ? value[1..^1] : value;
                        if (!boundary || !Regex.IsMatch(numberList, @"^\s*-?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)(?:\s*,\s*-?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))*\s*$"))
                        { preserved.Add(lineNumber); continue; }
                        string scaled = Regex.Replace(numberList, @"-?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)", m =>
                        {
                            double original = double.Parse(m.Value, CultureInfo.InvariantCulture);
                            double n = Math.Truncate(original * ratio);
                            if (key is "width" or "height" && original > 0) n = Math.Max(1, n);
                            if (Math.Abs(original) >= 1 && n == 0) n = Math.Sign(original);
                            return n.ToString("0", CultureInfo.InvariantCulture);
                        });
                        edits.Add(new(tokens[start].Start, tokens[end - 1].End,
                            quoted ? value[0] + scaled + value[^1] : scaled));
                    }
                    StringBuilder output = new(tag.Value);
                    foreach (var edit in edits.OrderByDescending(e => e.Start))
                        output.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
                    return output.ToString();
                });
            result.Append(converted).Append(line.AsSpan(comment));
        }
        return result.ToString();
    }
}
