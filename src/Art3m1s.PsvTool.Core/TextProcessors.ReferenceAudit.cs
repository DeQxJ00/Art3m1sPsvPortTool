using System.Text;
using System.Text.RegularExpressions;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    internal static IReadOnlyList<ResourceReference> FindResourceReferences(byte[] bytes, string extension, string? archive, string path)
    {
        if (extension == ".asb") return FindAsbReferences(bytes, archive, path);
        string text = EncodedText.Decode(bytes).Text;
        List<ResourceReference> result = [];
        if (extension == ".ini")
        {
            foreach (Match m in Regex.Matches(text, @"(?im)^[ \t]*BOOT[ \t]*=[ \t]*([^;#\r\n]+)"))
                result.Add(new(archive, path, LineAt(text, m.Index), m.Groups[1].Value.Trim().Trim('"', '\''), false));
            return result;
        }
        var tokens = LexScript(text);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (KeyName(tokens[i]) is not ("file" or "storage")) continue;
            if (extension == ".iet")
            {
                int lineStart = text.LastIndexOf('\n', Math.Max(0, tokens[i].Start - 1)) + 1;
                string prefix = text[lineStart..tokens[i].Start].TrimStart();
                if (prefix.StartsWith(';') || prefix.StartsWith('#') || !prefix.Contains('[')) continue;
            }
            int eq = i + 1;
            if (eq < tokens.Count && tokens[eq].Text == "]") eq++;
            if (eq + 1 >= tokens.Count || tokens[eq].Text != "=") continue;
            int value = eq + 1, end = value + 1;
            // A sole literal is auditable. Concatenations, variables and calls
            // remain runtime expressions; do not evaluate arbitrary game code.
            bool literal = tokens[value].Kind == 's';
            bool continued = end < tokens.Count && tokens[end].Text is ".." or "+" or "(" or "[";
            if (!literal || continued)
                while (end < tokens.Count && tokens[end].Text is not ("," or "}" or ";" or "]") &&
                    !text[tokens[end - 1].End..tokens[end].Start].Contains('\n')) end++;
            string expression = literal && !continued ? KeyName(tokens[value]) : text[tokens[value].Start..tokens[end - 1].End];
            result.Add(new(archive, path, LineAt(text, tokens[i].Start), expression, !literal || continued));
        }
        return result;
    }

    private static int LineAt(string text, int position) => 1 + text.AsSpan(0, position).Count('\n');

    internal static IEnumerable<(int Line, int Value)> NumericSettingValues(string text, string key)
    {
        var tokens = LexScript(text);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (KeyName(tokens[i]) != key) continue;
            int eq = i + 1;
            if (tokens[eq].Text == "]") eq++;
            if (eq + 1 >= tokens.Count || tokens[eq].Text != "=") continue;
            int at = eq + 1;
            bool tuple = tokens[at].Text == "{";
            if (tuple) at++;
            while (at < tokens.Count && tokens[at].Kind is 'n' or 's' &&
                int.TryParse(KeyName(tokens[at]), out int value) && value > 0)
            {
                // Accept only whole numeric elements, not arithmetic prefixes.
                if (at + 1 < tokens.Count && tokens[at + 1].Text is not ("," or "}" or ";") &&
                    !text[tokens[at].End..tokens[at + 1].Start].Contains('\n')) break;
                yield return (LineAt(text, tokens[i].Start), value);
                if (!tuple || at + 1 >= tokens.Count || tokens[at + 1].Text != ",") break;
                at += 2;
            }
        }
    }

    internal static string[] ResolveDirectoryReference(string expression, IReadOnlyList<string> ids)
    {
        var tokens = LexScript(expression);
        int at = tokens.Count > 0 && tokens[0].Text == "$" ? 1 : 0;
        string template = "";
        bool selector = false;
        while (at < tokens.Count)
        {
            if (tokens[at].Kind == 's' && !KeyName(tokens[at]).Contains('\\'))
                template += KeyName(tokens[at++]);
            else if (at + 2 < tokens.Count && tokens[at].Text == "game" && tokens[at + 1].Text == "." && tokens[at + 2].Text == "size")
            { template += "{directory}"; selector = true; at += 3; }
            else return [];
            if (at == tokens.Count) break;
            if (tokens[at++].Text is not (".." or "+")) return [];
            if (at == tokens.Count) return [];
        }
        return selector ? ids.Select(id => template.Replace("{directory}", id, StringComparison.Ordinal)).ToArray() : [];
    }

    private static IReadOnlyList<ResourceReference> FindAsbReferences(byte[] bytes, string? archive, string path)
    {
        if (bytes.Length < 9 || !bytes.AsSpan(0, 4).SequenceEqual("ASB\0"u8))
            throw new InvalidDataException("Unsupported or truncated ASB header; reference audit incomplete.");
        List<ResourceReference> result = [];
        using var input = new MemoryStream(bytes);
        using var reader = new BinaryReader(input);
        input.Position = 5;
        uint count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            uint type = reader.ReadUInt32();
            _ = Field();
            if (type == 1) continue;
            if (type != 0) throw new InvalidDataException("Unsupported ASB node; reference audit incomplete.");
            uint line = reader.ReadUInt32();
            uint parameters = reader.ReadUInt32();
            for (uint j = 0; j < parameters; j++)
            {
                string key = Field(), value = Field();
                if (key is "file" or "storage") result.Add(new(archive, path, checked((int)line), value, value.StartsWith('$')));
            }
        }
        return result;
        string Field()
        {
            uint length = reader.ReadUInt32();
            if (length >= input.Length - input.Position) throw new InvalidDataException("Truncated ASB field.");
            byte[] raw = reader.ReadBytes(checked((int)length));
            if (reader.ReadByte() != 0) throw new InvalidDataException("Invalid ASB terminator.");
            return EncodedText.Decode(raw).Text;
        }
    }
}
