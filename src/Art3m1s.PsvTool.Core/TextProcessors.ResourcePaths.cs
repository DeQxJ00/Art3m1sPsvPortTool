using System.Text;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    // Resource directory IDs are independent of the scaled viewport dimensions.
    // Activate only for scripts that explicitly define the original directory selector.
    public static async Task RepairResourceDirectoriesAsync(string root, CancellationToken token = default,
        ConversionProfile? profile = null, bool? hasLocalizedManual = null)
    {
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        var scripts = new List<(string Path, EncodedText Source, List<ScriptToken> Tokens)>();
        bool enabled = profile?.ResourceDirectorySelector ?? false;
        foreach (string path in files.Where(p => Path.GetExtension(p).Equals(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            var source = EncodedText.Decode(await File.ReadAllBytesAsync(path, token));
            var tokens = LexScript(source.Text);
            scripts.Add((path, source, tokens));
            if(profile is null)enabled |= FindSizeAssignments(tokens).Any();
        }
        if (!enabled) return;
        foreach (var (path, source, tokens) in scripts)
        {
            var edits = new List<ScriptEdit>();
            foreach (int end in FindSizeAssignments(tokens))
            {
                // The supported engine API is the same receiver used to register paths.
                bool engine = Enumerable.Range(0, Math.Max(0, tokens.Count - 3)).Any(i =>
                    tokens[i].Text == "e" && tokens[i+1].Text == ":" && tokens[i+2].Text == "setMagicPath");
                string export = "e:tag{\"var\", name=\"game.size\", data=game.size}";
                var following = tokens.Skip(end+1).Take(LexScript(export).Count).Select(t => t.Text);
                if (engine && !following.SequenceEqual(LexScript(export).Select(t => t.Text)))
                    edits.Add(new(tokens[end].End, tokens[end].End, "\n\t" + export));
            }
            AddDirectoryEdits(tokens, edits, "..");
            string changed = ApplyPathEdits(source.Text, edits);
            if (changed != source.Text) await File.WriteAllBytesAsync(path, source.Encode(changed), token);
        }
        bool localizedManual = files.Any(p => Path.GetRelativePath(root,p).Replace('\\','/').Contains("/manual_", StringComparison.OrdinalIgnoreCase));
        bool plainManual = files.Any(p => Path.GetFileNameWithoutExtension(p).Equals("manual", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(Path.GetFileName(Path.GetDirectoryName(p)), out _));
        foreach (string path in files.Where(p => Path.GetExtension(p).Equals(".asb", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] original = await File.ReadAllBytesAsync(path, token);
            byte[] changed = RepairAsbPaths(original, hasLocalizedManual ?? (localizedManual && !plainManual));
            if (!original.AsSpan().SequenceEqual(changed)) await File.WriteAllBytesAsync(path, changed, token);
        }
    }

    private static IEnumerable<int> FindSizeAssignments(List<ScriptToken> tokens)
    {
        string[] prefix = ["game", ".", "size", "=", "init", ".", "game_size", "["];
        for (int i=0; i+9<tokens.Count; i++)
            if (tokens.Skip(i).Take(8).Select(t=>t.Text).SequenceEqual(prefix) &&
                tokens[i+8].Kind == 'i' && tokens[i+9].Text == "]") yield return i+9;
    }

    private static void AddDirectoryEdits(List<ScriptToken> tokens, List<ScriptEdit> edits, string concat)
    {
        for (int i=0; i+4<tokens.Count; i++)
        {
            var t=tokens[i];
            if (t.Kind != 's' || t.Text.Length < 2) continue;
            string prefix=t.Text[1..^1];
            if (prefix is not ("ui/" or "image/" or "movie/" or "title")) continue;
            if (tokens[i+1].Text == concat && tokens[i+2].Text == "game" &&
                tokens[i+3].Text == "." && tokens[i+4].Text == "width")
                edits.Add(new(tokens[i+4].Start,tokens[i+4].End,"size"));
        }
    }

    private static string ApplyPathEdits(string text, List<ScriptEdit> edits)
    {
        var result=new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e=>e.Start))
            result.Remove(edit.Start,edit.End-edit.Start).Insert(edit.Start,edit.Text);
        return result.ToString();
    }

    private static byte[] RepairAsbPaths(byte[] bytes, bool localizedManual)
    {
        var records = ReadAsbRecords(bytes);
        if (records is null) return bytes;
        List<(AsbField, string)> changes = [];
        foreach (var record in records)
        {
            foreach (var (key, field) in record.Fields)
            {
                if (key is not ("file" or "storage" or "data") || !field.Value.StartsWith('$')) continue;
                var tokens = LexScript(field.Value);
                List<ScriptEdit> edits = [];
                AddDirectoryEdits(tokens, edits, "+");
                if (edits.Count == 0) continue;
                if (localizedManual && record.Tag == "lyc2" && key == "file")
                    foreach (var t in tokens.Where(t => t.Text is "'/manual'" or "\"/manual\""))
                        edits.Add(new(t.End - 1, t.End - 1, "**"));
                changes.Add((field, ApplyPathEdits(field.Value, edits)));
            }
        }
        return RewriteAsb(bytes, changes);
    }
}
