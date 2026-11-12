namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private sealed record OptionalMessageLayer(string Flag, string Parent);

    private static byte[] RepairCompiledScripts(byte[] bytes, double ratio)
    {
        if (bytes.AsSpan().IndexOf("rollzoom"u8) < 0 && bytes.AsSpan().IndexOf("file_exists"u8) < 0 && bytes.AsSpan().IndexOf("g.texture_maxsize"u8) < 0)
            return bytes;
        var records = ReadAsbRecords(bytes);
        if (records is null) return bytes;

        var optionalLayers = FindOptionalMessageLayers(records);
        var callbacks = records.Where(r => r.Tag == "calllua").Select(r => r.Value("function"))
            .ToHashSet(StringComparer.Ordinal);
        List<(AsbField, string)> edits = [];
        foreach (var record in records.Where(r => r.Tag == "lua"))
        {
            foreach (var (key, field) in record.Fields)
            {
                if (key != "script") continue;
                string updated = ScaleSplitImagePitch(RepairLuaCompatibility(field.Value, optionalLayers, callbacks), ratio);
                if (updated != field.Value) edits.Add((field, updated));
            }
        }
        return RewriteAsb(bytes, edits);
    }

    private static Dictionary<string, OptionalMessageLayer> FindOptionalMessageLayers(IReadOnlyList<AsbRecord> records)
    {
        var flags = records.Where(r => r.Tag == "var" && r.Value("system") == "file_exists" &&
                r.Value("file")?.TrimEnd('\'', '"').EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
            .Select(r => "$" + r.Value("name")).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, OptionalMessageLayer> layers = new(StringComparer.Ordinal);
        if (flags.Count == 0) return layers;

        HashSet<string> initialized = new(StringComparer.Ordinal);
        string? selected = null;
        bool fontInitialized = false;
        foreach (var record in records)
        {
            if (record.Tag is "/chgmsg" or "")
                selected = null;
            else if (record.Tag == "chgmsg")
            {
                selected = record.Value("layered") == "1" ? record.Value("id") : null;
                fontInitialized = false;
            }
            else if (record.Tag == "fontinit")
                fontInitialized = true;
            else if (selected is not null && fontInitialized && record.Tag == "execcsv" && record.Value("exec") == "font")
                initialized.Add(selected);
        }

        // The cleanup sequence associates a file-existence flag with a specific
        // layered message and its container. Ignore ambiguous associations.
        HashSet<string> ambiguous = new(StringComparer.Ordinal);
        for (int i = 0; i + 4 < records.Count; i++)
        {
            string? flag = records[i].Value("estimate")?.Trim();
            if (records[i].Tag != "if" || flag is null || !flags.Contains(flag) ||
                !SafeLuaLiteral(flag.AsSpan(1))) continue;
            var message = records[i + 1];
            string? layer = message.Value("id");
            if (message.Tag != "chgmsg" || message.Value("layered") != "1" ||
                layer is null || !initialized.Contains(layer)) continue;
            int cleanup = i + 2;
            if (records[cleanup].Tag == "indentmodify") cleanup++;
            if (cleanup + 2 >= records.Count || records[cleanup].Tag != "rp" ||
                records[cleanup].Value("backlog") != "0" || records[cleanup + 1].Tag != "/chgmsg" ||
                records[cleanup + 2].Tag != "lydel") continue;
            string? parent = records[cleanup + 2].Value("id");
            if (parent is null || !SafeLuaLiteral(parent) || !layer.StartsWith(parent + ".", StringComparison.Ordinal))
                continue;
            OptionalMessageLayer candidate = new(flag[1..], parent);
            if (layers.TryGetValue(layer, out var existing) && existing != candidate) ambiguous.Add(layer);
            else layers[layer] = candidate;
        }
        foreach (string layer in ambiguous) layers.Remove(layer);
        return layers;
    }

    private static bool SafeLuaLiteral(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (char c in value)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) return false;
        return true;
    }

    private static string RepairLuaCompatibility(string script,
        IReadOnlyDictionary<string, OptionalMessageLayer>? optionalLayers = null,
        IReadOnlySet<string?>? callbacks = null)
    {
        if (!script.Contains("rollzoom", StringComparison.Ordinal) && optionalLayers?.Count is not > 0) return script;
        var tokens = LexScript(script);
        List<ScriptEdit> edits = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (At(i) == "local") RepairButtonDefault(i);
            if (At(i) == "function" && optionalLayers?.Count > 0) GuardMessageCallback(i);
        }
        return edits.Count == 0 ? script : ApplyPathEdits(script, edits);

        string At(int index) => index < tokens.Count ? tokens[index].Text : "";
        char Kind(int index) => index < tokens.Count ? tokens[index].Kind : '\0';
        string? Literal(int index) => Kind(index) == 's' && At(index).Length >= 2 ? At(index)[1..^1] : null;

        void RepairButtonDefault(int start)
        {
            string local = At(start + 1);
            if (Kind(start + 1) != 'i' || At(start + 2) != "=" || Kind(start + 3) != 'i' ||
                At(start + 4) != "." || At(start + 5) != "rollzoom" || At(start + 6) != "or" ||
                Literal(start + 7) != "0") return;
            int next = start + 8;
            if (At(next) == ";") next++;
            if (At(next++) != "if") return;

            bool numericZero = false, stringZero = false, off = false, none = false;
            while (true)
            {
                bool wrapped = At(next) == "(";
                if (wrapped) next++;
                if (At(next + 1) != "==") return;
                int value = At(next) == local ? next + 2 : At(next + 2) == local ? next : -1;
                if (value < 0) return;
                numericZero |= At(value) == "0";
                stringZero |= Literal(value) == "0";
                off |= Literal(value) == "off";
                none |= Literal(value) == "none";
                next += 3;
                if (wrapped && At(next++) != ")") return;
                if (At(next) != "or") break;
                next++;
            }
            if (numericZero && !stringZero && off && none && At(next) == "then" &&
                At(next + 1) == local && At(next + 2) == "=" && Literal(next + 3) == "0")
                edits.Add(new(tokens[next - 1].End, tokens[next - 1].End, $" or {local} == {At(start + 7)}"));
        }

        void GuardMessageCallback(int start)
        {
            string function = At(start + 1), engine = At(start + 3);
            if (Kind(start + 1) != 'i' || callbacks?.Contains(function) != true ||
                At(start + 2) != "(" || Kind(start + 3) != 'i' ||
                At(start + 4) != "," || Kind(start + 5) != 'i' || At(start + 6) != ")") return;
            int next = start + 7;
            if (At(next) == "local" && Kind(next + 1) == 'i' && At(next + 1) != engine &&
                At(next + 2) == "=" && At(next + 3) == engine && At(next + 4) == ":" &&
                At(next + 5) == "var" && At(next + 6) == "(" && Literal(next + 7) is not null && At(next + 8) == ")")
            {
                next += 9;
                if (At(next) == ";") next++;
            }
            if (At(next) != engine || At(next + 1) != ":" || At(next + 2) != "tag") return;
            int table = At(next + 3) == "(" ? next + 4 : next + 3;
            if (At(table) != "{" || Literal(table + 1) != "chgmsg") return;
            string? layer = null;
            bool layered = false;
            int field = table + 2;
            while (At(field) is "," or ";")
            {
                field++;
                if (At(field) == "}") break;
                if (Kind(field) != 'i' || At(field + 1) != "=") return;
                if (At(field) == "id") layer = Literal(field + 2);
                if (At(field) == "layered") layered = At(field + 2) == "1" || Literal(field + 2) == "1";
                field += 3;
            }
            if (!layered || layer is null || At(field) != "}" ||
                (table == next + 4 && At(field + 1) != ")") ||
                !optionalLayers!.TryGetValue(layer, out var optional)) return;
            edits.Add(new(tokens[next].Start, tokens[next].Start,
                $"if {engine}:var(\"{optional.Flag}\") == \"0\" then {engine}:tag({{\"lydel\", id = \"{optional.Parent}\"}}); return end\n"));
        }
    }
}
