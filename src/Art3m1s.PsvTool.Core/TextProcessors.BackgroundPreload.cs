namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    // Only hide the immediate warm-up copy when the guarded helper also queues
    // the same resource on its normal background layer. Do not alter visible copies.
    private static string HideBackgroundPreloadCopy(string text)
    {
        const string prefix = """
            if not param["prvline"] then
                if game.os ~= "vita" then
                    e:tag{"lyc",id=("0."..id..".0"),file=(p.path..file)}
            """;
        const string suffix = """
                end
            end
            lyc2{id=(id..".0"),file=(p.path..file),eq=(true)
            """;
        List<ScriptToken> tokens = LexScript(text);
        List<ScriptToken> before = LexScript(prefix);
        List<ScriptToken> pattern = LexScript(prefix + "\n" + suffix);
        List<int> insertions = [];
        for (int i = 0; i + pattern.Count <= tokens.Count; i++)
        {
            bool matches = true;
            for (int j = 0; j < pattern.Count; j++)
            {
                if (tokens[i + j].Kind != pattern[j].Kind || KeyName(tokens[i + j]) != KeyName(pattern[j]))
                {
                    matches = false;
                    break;
                }
            }
            // A completed eq value must be followed by a field separator or table end.
            int next = i + pattern.Count;
            if (matches && next < tokens.Count && tokens[next].Text is "," or "}" or ";")
                insertions.Add(tokens[i + before.Count - 1].End);
        }
        for (int i = insertions.Count - 1; i >= 0; i--)
            text = text.Insert(insertions[i], " e:tag{\"lyprop\",id=(\"0.\"..id..\".0\"),visible=\"0\"}");
        return text;
    }
}
