using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private readonly record struct ScriptToken(int Start, int End, string Text, char Kind);
    private readonly record struct ScriptEdit(int Start, int End, string Text);

    // Tokenize first: identifiers inside dialogue/comments are never geometry keys.
    // This deliberately isn't an evaluator. Unknown expressions retain their meaning.
    private static string ScaleScript(string text, double ratio, string extension, List<int> preservedExpressions)
    {
        List<ScriptToken> tokens = LexScript(text);
        List<ScriptEdit> edits = [];
        string[] keys = extension switch
        {
            ".tbl" => [.. TblScalarKeys, .. TblClipKeys, "game_width", "game_height"],
            ".ipt" => IptKeys,
            ".ast" => AstKeys,
            _ => LuaKeys
        };
        bool[] vita = VitaBranches(tokens);
        Stack<string> tables = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            ScriptToken token = tokens[i];
            string t = token.Text;
            if (t == "}") { if (tables.Count > 0) tables.Pop(); continue; }
            if (t == "{")
            {
                string owner = i >= 2 && tokens[i - 1].Text == "=" ? KeyName(tokens[i - 2]) : "";
                bool emote = tables.Contains("emote");
                tables.Push(owner);
                if (!vita[i] && extension == ".tbl")
                {
                    if (TblListKeys.Contains(owner)) ScaleTuple(i, false);
                    else if (emote) ScaleTuple(i, true);
                }
                continue;
            }
            if (vita[i]) continue;

            // mulpos is an explicit pixel conversion API; do not touch calls in strings.
            if (extension == ".lua" && t == "mulpos" && At(i + 1) == "(" &&
                TryNumber(i + 2, out int end, out double number) && At(end) == ")")
            {
                AddNumber(i + 2, end, number, false);
                continue;
            }

            // IPT entries may be unnamed clip rectangles. IDs and other strings stay intact.
            if (extension == ".ipt" && token.Kind == 's' && i > 0 &&
                (At(i - 1) == "=" || At(i - 1) == "," || At(i - 1) == "{"))
            {
                ScaleString(token, true, false);
                continue;
            }

            string key = KeyName(token);
            int eq = i + 1;
            bool bracketKey = token.Kind == 's' && At(i - 1) == "[" && At(i + 1) == "]";
            if (bracketKey) eq++;
            if (!keys.Contains(key) || At(eq) != "=") continue;
            int start = eq + 1;
            // A bare x/y local could be a flag, direction or progress, not a coordinate.
            bool field = tables.Count > 0 && (At(i - 1) is "{" or "," or ";" ||
                bracketKey && At(i - 2) is "{" or "," or ";");
            bool dimensionMember = key is "width" or "height" && At(i - 1) == ".";
            bool tableConfig = extension == ".tbl" && key is "game_width" or "game_height";
            if (!field && !dimensionMember && !tableConfig) continue;
            bool size = key is "w" or "h" or "width" or "height" or "cw" or "ch" or "fw" or "fh" or "size" or "rubysize" or "game_width" or "game_height";
            bool clip = TblClipKeys.Contains(key);
            int parentheses = 0;
            while (At(start) == "(") { start++; parentheses++; }
            bool EndOfValue(int end)
            {
                for (int n = 0; n < parentheses; n++, end++)
                    if (At(end) != ")") return false;
                return Boundary(end);
            }
            if (start < tokens.Count && tokens[start].Kind == 's' && EndOfValue(start + 1))
            {
                ScaleString(tokens[start], clip, size);
            }
            else if (TryNumber(start, out end, out number) && EndOfValue(end))
            {
                AddNumber(start, end, number, size);
            }
            else if (start < tokens.Count)
            {
                // Expose conservative decisions to the conversion log. Do not silently
                // guess which constants in a dynamic expression have pixel units.
                preservedExpressions.Add(1 + text.AsSpan(0, token.Start).Count('\n'));
            }
        }
        StringBuilder result = new(text);
        foreach (ScriptEdit edit in edits.DistinctBy(e => e.Start).OrderByDescending(e => e.Start))
            result.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
        return result.ToString();

        string At(int index) => index >= 0 && index < tokens.Count ? tokens[index].Text : "";
        bool Boundary(int index)
        {
            if (index >= tokens.Count || At(index) is "," or ";" or "}" or "end") return true;
            // A new statement on the next line is allowed, but continued arithmetic isn't.
            return index > 0 && text[tokens[index - 1].End..tokens[index].Start].Contains('\n') &&
                (tokens[index].Kind == 'i' && At(index) is not ("and" or "or"));
        }
        bool TryNumber(int index, out int end, out double number)
        {
            end = index; number = 0;
            bool negative = At(end) == "-";
            if (negative) end++;
            if (end >= tokens.Count || tokens[end].Kind != 'n' ||
                !double.TryParse(tokens[end].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
            if (negative) number = -number;
            end++;
            return true;
        }
        string Scaled(double number, bool positiveSize)
        {
            double value = Math.Truncate(number * ratio);
            if (positiveSize && number > 0) value = Math.Max(1, value);
            // Keep one-pixel helper movements and offscreen sentinels nonzero.
            if (Math.Abs(number) >= 1 && value == 0) value = Math.Sign(number);
            return value.ToString("0", CultureInfo.InvariantCulture);
        }
        void AddNumber(int start, int end, double number, bool size) =>
            edits.Add(new(tokens[start].Start, tokens[end - 1].End, Scaled(number, size)));
        void ScaleString(ScriptToken token, bool rectangle, bool size)
        {
            string value = token.Text[1..^1];
            if (!Regex.IsMatch(value, @"^\s*-?(?:\d+(?:\.\d+)?|\.\d+)(?:\s*,\s*-?(?:\d+(?:\.\d+)?|\.\d+))*\s*$", RegexOptions.CultureInvariant)) return;
            MatchCollection numbers = Regex.Matches(value, @"-?(?:\d+(?:\.\d+)?|\.\d+)", RegexOptions.CultureInvariant);
            if (rectangle && numbers.Count != 4) return;
            for (int n = 0; n < numbers.Count; n++)
            {
                Match m = numbers[n];
                double number = double.Parse(m.Value, CultureInfo.InvariantCulture);
                edits.Add(new(token.Start + 1 + m.Index, token.Start + 1 + m.Index + m.Length,
                    Scaled(number, size || rectangle && n >= 2)));
            }
        }
        void ScaleTuple(int open, bool pose)
        {
            List<(int Start, int End, double Value)> values = [];
            int at = open + 1;
            while (TryNumber(at, out int end, out double number))
            {
                values.Add((at, end, number)); at = end;
                if (At(at) != ",") break;
                at++;
            }
            if (At(at) != "}" || values.Count == 0 || pose && values.Count != 5) return;
            for (int n = pose ? 1 : 0; n < values.Count; n++)
            {
                var v = values[n];
                AddNumber(v.Start, v.End, v.Value, pose && n >= 3);
            }
        }
    }

    private static string KeyName(ScriptToken token) => token.Kind == 's' ? token.Text[1..^1] : token.Text;

    private static List<ScriptToken> LexScript(string text)
    {
        List<ScriptToken> tokens = [];
        int i = 0;
        Regex numberPattern = new(@"\G(?:0[xX][\da-fA-F]+|(?:\d+(?:\.(?!\.)\d*)?|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.CultureInvariant);
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text.AsSpan(i).StartsWith("--"))
            {
                i += 2;
                if (LongBracket(i, out int end)) i = end;
                else { while (i < text.Length && text[i] is not ('\r' or '\n')) i++; }
                continue;
            }
            int start = i;
            char kind;
            if (LongBracket(i, out int longEnd)) { i = longEnd; kind = 'l'; }
            else if (text[i] is '\'' or '"')
            {
                char quote = text[i++];
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
                    if (text[i++] == quote) break;
                }
                kind = 's';
            }
            else if (char.IsLetter(text[i]) || text[i] == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                kind = 'i';
            }
            else if (char.IsDigit(text[i]) || text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))
            {
                Match m = numberPattern.Match(text, i);
                i += m.Length; kind = 'n';
            }
            else
            {
                i++;
                if (i < text.Length && (text[start..(i + 1)] is "==" or "~=" or "<=" or ">=" or "..")) i++;
                kind = 'p';
            }
            tokens.Add(new(start, i, text[start..i], kind));
        }
        return tokens;

        bool LongBracket(int start, out int end)
        {
            end = start;
            if (start >= text.Length || text[start] != '[') return false;
            int k = start + 1;
            while (k < text.Length && text[k] == '=') k++;
            if (k >= text.Length || text[k] != '[') return false;
            string closing = "]" + new string('=', k - start - 1) + "]";
            int found = text.IndexOf(closing, k + 1, StringComparison.Ordinal);
            end = found < 0 ? text.Length : found + closing.Length;
            return true;
        }
    }

    // Fixed coordinates inside an explicit Vita branch already target the handheld.
    // Track nested blocks and else branches instead of depending on a game/file name.
    private static bool[] VitaBranches(List<ScriptToken> tokens)
    {
        bool[] result = new bool[tokens.Count];
        List<(string Kind, bool Vita)> blocks = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i].Text;
            if (tokens[i].Kind == 'i')
            {
                if (t == "if") blocks.Add(("if", IsVitaCondition(i + 1)));
                else if (t is "function" or "do" or "repeat") blocks.Add((t, false));
                else if (t is "else" or "elseif" && blocks.Count > 0 && blocks[^1].Kind == "if")
                    blocks[^1] = ("if", t == "elseif" && IsVitaCondition(i + 1));
                else if (t is "end" or "until" && blocks.Count > 0) blocks.RemoveAt(blocks.Count - 1);
            }
            result[i] = blocks.Any(b => b.Vita);
        }
        return result;

        bool IsVitaCondition(int start)
        {
            bool positive = false;
            for (int j = start; j < tokens.Count; j++)
            {
                if (tokens[j].Kind == 'i' && tokens[j].Text == "then") break;
                if (tokens[j].Kind == 'i' && tokens[j].Text is "not" or "or") return false;
                if (tokens[j].Text == "==" &&
                    (j + 1 < tokens.Count && tokens[j + 1].Kind == 's' && KeyName(tokens[j + 1]).Equals("vita", StringComparison.OrdinalIgnoreCase) ||
                     j > 0 && tokens[j - 1].Kind == 's' && KeyName(tokens[j - 1]).Equals("vita", StringComparison.OrdinalIgnoreCase))) positive = true;
            }
            return positive;
        }
    }
}
