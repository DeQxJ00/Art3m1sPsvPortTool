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
    private static string ScaleScript(string text, double ratio, string extension, List<int> preservedExpressions, bool runtimeScaledCoordinates = false)
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
        // Older generated layout tables have init/mw_path/mw_pos but no device resolution field.
        bool legacyLayout = tokens.Any(t => KeyName(t) == "mw_path")
            && tokens.Any(t => KeyName(t) == "mw_pos") && tokens.Any(t => t.Text == "init");
        bool luaLayout = extension == ".lua" && tokens.Any(t => t.Text == "csv")
            && (tokens.Any(t => KeyName(t) == "device_resolution_w") || legacyLayout);
        if (luaLayout)
        {
            // A native platform table already describes its own image set.
            // Do not apply the desktop conversion ratio to it a second time.
            for (int n=0;n<tokens.Count && tokens[n].Text!="csv";n++)
            {
                if(KeyName(tokens[n])!="game_os")continue;
                int eq=n+1;if(eq<tokens.Count && tokens[eq].Text=="]")eq++;
                if(eq+1<tokens.Count && tokens[eq].Text=="=" && tokens[eq+1].Kind=='s'
                    && KeyName(tokens[eq+1])=="vita")return text;
            }
        }
        if (luaLayout) keys = [.. keys, "w", "h", "cx", "cy", "cw", "ch", "fx", "fy", "lx", "ly",
            "device_resolution_w", "device_resolution_h", "target_resolution_w", "target_resolution_h",
            "mw_scroll", "backlog_height"];
        HashSet<string> fontTables = [];
        for (int n = 0; n + 8 < tokens.Count; n++)
            if (tokens[n].Kind == 'i' && tokens[n+1].Text == "=" && tokens[n+2].Text == "{"
                && tokens[n+3].Text == "[" && tokens[n+4].Text == "1" && tokens[n+5].Text == "]"
                && tokens[n+6].Text == "=" && KeyName(tokens[n+7]) == "font")
                fontTables.Add(tokens[n].Text);
        bool[] vita = VitaBranches(tokens);
        Stack<string> tables = new();
        Stack<bool> logicalTables = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            ScriptToken token = tokens[i];
            string t = token.Text;
            if (t == "}") { if (tables.Count > 0) tables.Pop(); if (logicalTables.Count > 0) logicalTables.Pop(); continue; }
            if (t == "{")
            {
                string owner = i >= 2 && tokens[i - 1].Text == "="
                    ? KeyName(tokens[i - (tokens[i - 2].Text == "]" ? 3 : 2)]) : "";
                string parent = tables.TryPeek(out string? parentName) ? parentName : "";
                if (luaLayout && !vita[i] && parent is "fgpos" or "config_text")
                    ScaleLayoutTuple(i, parent);
                bool emote = tables.Contains("emote");
                bool chatLayout = tables.Contains("line");
                tables.Push(owner);
                logicalTables.Push(runtimeScaledCoordinates && extension == ".ast" && i+1<tokens.Count &&
                    tokens[i+1].Kind=='s' && RuntimeScaledImageCommands.Contains(KeyName(tokens[i+1])));
                if (!vita[i] && extension == ".tbl")
                {
                    if (TblListKeys.Contains(owner) && !(runtimeScaledCoordinates && owner == "game_scale")) ScaleTuple(i, false);
                    else if (emote) ScaleTuple(i, true);
                    else if (chatLayout) ScaleChatTuple(i, owner);
                }
                continue;
            }
            if (vita[i] || logicalTables.TryPeek(out bool logical) && logical) continue;

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
            if (luaLayout && tables.Count == 1 && tables.Peek() == "init" && At(eq) == "="
                && eq+1 < tokens.Count && tokens[eq+1].Kind == 's')
                ScaleLayoutString(tokens[eq+1], key);
            bool fontSizeTable = extension == ".lua" && tables.Contains("fontsize_table");
            bool fontMember = extension == ".lua" && i >= 2 && At(i-1) == "." && fontTables.Contains(At(i-2));
            bool fontMetric = extension == ".lua" && (tables.Contains("font") || tables.Any(fontTables.Contains) || fontMember) &&
                key is "size" or "rubysize" or "rubykerning" or "kerning" or "spacetop" or "spacemiddle" or "spacebottom";
            if (!(keys.Contains(key) || fontSizeTable || fontMetric || extension == ".lua" && key == "locate") || At(eq) != "=") continue;
            int start = eq + 1;
            // A bare x/y local could be a flag, direction or progress, not a coordinate.
            bool field = tables.Count > 0 && (At(i - 1) is "{" or "," or ";" ||
                bracketKey && At(i - 2) is "{" or "," or ";");
            bool dimensionMember = key is "width" or "height" && At(i - 1) == ".";
            bool tableConfig = extension == ".tbl" && key is "game_width" or "game_height";
            if (!field && !dimensionMember && !tableConfig && !fontMember) continue;
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
            if (start < tokens.Count && tokens[start].Kind is ('s' or 'l') && EndOfValue(start + 1))
            {
                ScaleString(tokens[start], clip, size);
            }
            else if (TryNumber(start, out end, out number) && EndOfValue(end))
            {
                AddNumber(start, end, number, size);
            }
            else if (extension == ".lua" && key is "left" or "top" or "width" or "height"
                && start < tokens.Count && tokens[start].Kind == 'i'
                && tokens[start].Text.EndsWith("_" + key, StringComparison.Ordinal)
                && EndOfValue(start + 1) && IsLiteralPixelAlias(tokens[start].Text, start))
            {
                // Scale a literal pixel alias at its geometry use; its shared
                // declaration may also be consumed by non-layout code.
                ScriptToken alias = tokens[start];
                edits.Add(new(alias.Start, alias.End, "(" + alias.Text + "*" + ratio.ToString("G", CultureInfo.InvariantCulture) + ")"));
            }
            else if (extension == ".lua" && key is "left" or "top"
                && start < tokens.Count && tokens[start].Text == key
                && TryScaleAxisOffsets(start + 1, EndOfValue))
            {
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

        bool IsLiteralPixelAlias(string name, int use)
        {
            int declaration = -1;
            for (int n = 0; n < tokens.Count; n++)
            {
                if (tokens[n].Text != name) continue;
                // Ambiguous multiple assignments and loop bindings require full binding analysis.
                if (At(n + 1) == "," && At(n - 1) != "=" || At(n - 1) == "for") return false;
                if (At(n - 1) == "local")
                {
                    if (declaration >= 0) return false; // shadowed binding
                    declaration = n;
                }
                if (At(n + 1) != "=") continue;
                // A same-named table field is not evidence about this local.
                if (At(n - 1) is "." or "{" or "," ||
                    !TryNumber(n + 2, out int end, out _) || !Boundary(end)) return false;
            }
            if (declaration < 0 || declaration >= use) return false;
            // Captures and parameters need a full binding resolver. Preserve
            // them rather than infer units from another function's local.
            if (tokens.Skip(declaration).Take(use - declaration).Any(t => t.Kind == 'i' && t.Text == "function")) return false;
            int[] declaredScope = BlockScope(declaration);
            int[] useScope = BlockScope(use);
            return declaredScope.Length <= useScope.Length && declaredScope.SequenceEqual(useScope.Take(declaredScope.Length));
        }
        int[] BlockScope(int stop)
        {
            List<(int Id, string Kind, bool AwaitDo)> scope = [];
            for (int n = 0; n < stop; n++)
            {
                if (tokens[n].Kind != 'i') continue;
                string word = tokens[n].Text;
                if (word is "function" or "if" or "repeat" or "for" or "while")
                    scope.Add((n, word, word is "for" or "while"));
                else if (word == "do")
                {
                    if (scope.Count > 0 && scope[^1].AwaitDo)
                        scope[^1] = (scope[^1].Id, scope[^1].Kind, false);
                    else scope.Add((n, word, false));
                }
                else if (word is "else" or "elseif" && scope.Count > 0 && scope[^1].Kind == "if")
                    scope[^1] = (n, "if", false);
                else if (word is "end" or "until" && scope.Count > 0)
                    scope.RemoveAt(scope.Count - 1);
            }
            return scope.Select(s => s.Id).ToArray();
        }
        bool TryScaleAxisOffsets(int index, Func<int, bool> endOfValue)
        {
            List<(int Start, int End, double Value)> offsets = [];
            while (At(index) is "+" or "-")
            {
                int start = index + 1;
                if (!TryNumber(start, out int end, out double value)) return false;
                offsets.Add((start, end, value));
                index = end;
            }
            if (offsets.Count == 0 || !endOfValue(index)) return false;
            foreach (var offset in offsets) AddNumber(offset.Start, offset.End, offset.Value, false);
            return true;
        }
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
            if (end >= tokens.Count || tokens[end].Kind != 'n') return false;
            int length = AsciiNumericPrefixLength(tokens[end].Text);
            if (length == 0 || !double.TryParse(tokens[end].Text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                !double.IsFinite(number)) return false;
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
        void AddNumber(int start, int end, double number, bool size)
        {
            ScriptToken last = tokens[end - 1];
            int editEnd = last.Start + AsciiNumericPrefixLength(last.Text);
            if (editEnd < last.End)
                preservedExpressions.Add(1 + text.AsSpan(0, tokens[start].Start).Count('\n'));
            edits.Add(new(tokens[start].Start, editEnd, Scaled(number, size)));
        }
        void ScaleString(ScriptToken token, bool rectangle, bool size)
        {
            int delimiter = StringDelimiterLength(token);
            string value = KeyName(token);
            if (!Regex.IsMatch(value, @"^\s*-?(?:\d+(?:\.\d+)?|\.\d+)(?:\s*,\s*-?(?:\d+(?:\.\d+)?|\.\d+))*\s*$", RegexOptions.CultureInvariant)) return;
            MatchCollection numbers = Regex.Matches(value, @"-?(?:\d+(?:\.\d+)?|\.\d+)", RegexOptions.CultureInvariant);
            if (rectangle && numbers.Count != 4) return;
            // Scale ASCII numeric prefixes only. Non-ASCII digit suffixes keep
            // their original spelling and never become part of the number.
            for (int n = 0; n < numbers.Count; n++)
            {
                Match m = numbers[n];
                int length = AsciiNumericPrefixLength(m.Value);
                if (length < m.Length)
                    preservedExpressions.Add(1 + text.AsSpan(0, token.Start).Count('\n'));
                if (length == 0 || !double.TryParse(m.Value[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    !double.IsFinite(number))
                {
                    if (length == m.Length)
                        preservedExpressions.Add(1 + text.AsSpan(0, token.Start).Count('\n'));
                    continue;
                }
                edits.Add(new(token.Start + delimiter + m.Index, token.Start + delimiter + m.Index + length,
                    Scaled(number, size || rectangle && n >= 2)));
            }
        }
        void ScaleLayoutString(ScriptToken value, string key)
        {
            int count = key switch
            {
                "mw_name_pos" or "glyph_pos" or "glyph_size" or "face_pos" or "save_new" or "load_new" => 2,
                "mw_pos" or "backlog_pos" or "paddle_area" or "select_back2" or "save_ss" or "save_ss_l" => 4,
                "save_date" or "save_text" or "save_chap" or "save_slot" or "save_date2" or
                "load_date" or "load_text" or "load_chap" or "load_slot" or "load_date2" or
                "select_pos" or "select_pos2" or "bgm_title" or "event_title" => 5,
                _ => 0
            };
            string[] parts = value.Text[1..^1].Split(',');
            if (key == "mw_pos" && parts.Length == 2) count = 2;
            if (count == 0 || parts.Length < count) return;
            double[] numbers = new double[count];
            for (int n=0;n<count;n++)
                if (!double.TryParse(parts[n], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[n]) || !double.IsFinite(numbers[n])) return;
            for (int n=0;n<count;n++) parts[n]=Scaled(numbers[n], n>=2);
            edits.Add(new(value.Start,value.End,value.Text[0]+string.Join(",",parts)+value.Text[^1]));
        }
        void ScaleLayoutTuple(int open, string parent)
        {
            int at=open+1;
            const int count=4;
            List<(int Start,int End,double Value)> fields=[];
            for(int n=0;n<count;n++)
            {
                int start=at;
                if(!TryNumber(at,out int end,out double value))return;
                fields.Add((start,end,value));at=end;
                if(n<count-1) { if(At(at)!=",")return; at++; }
            }
            for(int n=0;n<count;n++)
                if(parent!="fgpos" || n is 1 or 2) AddNumber(fields[n].Start,fields[n].End,fields[n].Value,false);
        }
        void ScaleChatTuple(int open, string owner)
        {
            // The chat layout schema mixes pixel values with layer IDs, assets
            // and durations. Scale only known pixel slots, never entire tuples.
            List<(int Start, int End, double? Value)> values = [];
            int at = open + 1;
            while (at < tokens.Count && At(at) != "}")
            {
                int start = at;
                double? value = null;
                if (TryNumber(at, out int end, out double number)) { at = end; value = number; }
                else if (tokens[at].Kind == 's') at++;
                else return;
                values.Add((start, at, value));
                if (At(at) == ",") at++;
                else if (At(at) != "}") return;
            }
            int[] slots = owner switch
            {
                "pos" when values.Count == 2 => [0,1],
                "area" when values.Count == 5 => [0,1,2,3,4],
                "scroll" when values.Count == 2 => [0],
                "stamp" when values.Count == 3 => [1,2],
                "bg" or "front" when values.Count == 4 => [2,3],
                "title" when values.Count == 5 && KeyName(tokens[values[4].Start]) == "image" => [2,3],
                _ when values.Count == 11 && tokens[values[0].Start].Kind == 's'
                    && tokens[values[1].Start].Kind == 's' => [2,3,4,5,8,9,10],
                _ when values.Count == 7 && values[0].Value.HasValue
                    && tokens[values[1].Start].Kind == 's' && tokens[values[4].Start].Kind == 's' => [2,3,5,6],
                _ => []
            };
            foreach (int slot in slots)
            {
                var v = values[slot];
                if (v.Value is double number) AddNumber(v.Start, v.End, number, false);
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

    private static int StringDelimiterLength(ScriptToken token) => token.Kind == 'l' ? token.Text.IndexOf('[',1)+1 : 1;
    private static string KeyName(ScriptToken token) => token.Kind is 's' or 'l'
        ? token.Text[StringDelimiterLength(token)..^StringDelimiterLength(token)] : token.Text;

    private static int AsciiNumericPrefixLength(string value)
    {
        int length = 0;
        while (length < value.Length && char.IsAscii(value[length])) length++;
        for (int i = length; i < value.Length; i++)
            if (!char.IsDigit(value[i])) return 0;
        return length;
    }

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
