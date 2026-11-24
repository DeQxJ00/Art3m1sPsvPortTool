namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private const string LogicalBackgroundAnchor = "art3m1s_logical_ay";
    private static readonly string[] BackgroundAnchorAdjustment =
        ["if", "p", ".", "ay", "then", "y", "=", "y", "-", "getScalePos", "(", "p", ".", "ay", "-", "game", ".", "say", ")", "end"];

    private static string NormalizeBackgroundAnchorOffset(string text)
    {
        var tokens = LexScript(text);
        var vita = VitaBranches(tokens);
        var result = new System.Text.StringBuilder(text);
        for (int i = tokens.Count - BackgroundAnchorAdjustment.Length; i >= 0; i--)
        {
            if (vita[i] || !Matches(tokens, i, BackgroundAnchorAdjustment)) continue;
            // The pixel anchor stays resized. The offset needs the original AY
            // so it can subtract the logical center before applying game.scale.
            int start = tokens[i + 11].Start;
            int end = tokens[i + 13].End;
            result.Remove(start, end - start).Insert(start, $"(p.{LogicalBackgroundAnchor} or p.ay)");
        }
        return result.ToString();
    }

    // A legacy movement helper computes its start through getScale but inlines
    // the original display ratio at its endpoint. Once resized this exposes the
    // underlying CG. Normalize only this verified helper, never arbitrary ratios.
    private static string NormalizeInlineMovementScale(string text)
    {
        var tokens=LexScript(text);
        bool movement=false, scaledStart=false;
        List<ScriptEdit> edits=[];
        bool Match(int i, params string[] pattern) => tokens.Skip(i).Take(pattern.Length).Select(t=>t.Text).SequenceEqual(pattern);
        string[] endpoint=["local","x2","=","(","(","v",".","x","or","480",
            ")","-","480",")","*","(","1280","/","960",")"];
        for(int i=0;i<tokens.Count;i++)
        {
            if(tokens[i].Text=="function")
            {
                movement=Match(i,"function","cg_moveXY","(","p",",","name",")");
                scaledStart=false;
            }
            if(!movement)continue;
            scaledStart |= Match(i,"local","x1","=","getScale","(","(","scr",".","img",".","bg","[","no","]",
                "and","scr",".","img",".","bg","[","no","]",".","x","or","480",")","-","480",")");
            if(scaledStart && Match(i,endpoint) && Match(i+endpoint.Length,"local","y2","=","getScale","("))
                edits.Add(new(tokens[i+3].Start,tokens[i+endpoint.Length-1].End,"getScale((v.x or 480) - 480)"));
        }
        var result=new System.Text.StringBuilder(text);
        foreach(var edit in edits.OrderByDescending(e=>e.Start))
            result.Remove(edit.Start,edit.End-edit.Start).Insert(edit.Start,edit.Text);
        return result.ToString();
    }
    // These custom image commands use script-space positions. The matching
    // runtime converts them through game.scale; native lowercase tags use pixels.
    private static readonly HashSet<string> RuntimeScaledImageCommands = new(StringComparer.Ordinal)
    {
        "BG", "BG_XY", "EV", "EV_C", "CG", "CGS", "CG_X", "CG_Y", "CG_XY", "CG_XYA", "CG_AN", "CG_DEL",
        "ST", "ST0", "STG", "ST_X", "ST_Y", "ST_XY", "ST_DEL", "ST2_DEL"
    };

    public static async Task<bool> DetectRuntimeScaledCoordinatesAsync(string root, CancellationToken token = default)
    {
        var scripts=new List<string>();
        foreach (string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
            .Where(p=>Path.GetExtension(p).Equals(".lua",StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            scripts.Add(EncodedText.Decode(await File.ReadAllBytesAsync(path,token)).Text);
        }
        return DescribeCoordinateProfile(scripts).Scaled;
    }
    internal static (bool Scaled, bool Directories, int? Base, bool ConflictingBases, bool BackgroundPositions)
        DescribeCoordinateProfile(IEnumerable<string> scripts)
    {
        bool scale=false,helper=false,directories=false;
        bool tableBasedScale = false, backgroundMovement = false, scaledPositionHelper = false, anchorAdjustment = false;
        HashSet<int> bases=[];
        foreach(string script in scripts)
        {
            foreach(var item in NumericSettingValues(script,"game_scale"))bases.Add(item.Value);
            var tokens=LexScript(script);
            directories |= FindSizeAssignments(tokens).Any();
            bool Match(int i, params string[] pattern) => Matches(tokens, i, pattern);
            for(int i=0;i<tokens.Count;i++)
            {
                scale |= Match(i,"game",".","scale","=","game",".","width","/","init",".","game_scale");
                if (Match(i, "local", "s", "=", "init", ".", "game_scale"))
                    tableBasedScale |= Enumerable.Range(i + 6, Math.Min(64, tokens.Count - i - 6))
                        .Any(j => Match(j, "game", ".", "scale", "=", "game", ".", "width", "/", "s", "[", "1", "]"));
                if (Match(i, "local", "x1", "=", "p", ".", "x", "and", "getScalePos", "(", "p", ".", "x", ")"))
                    backgroundMovement |= Match(i + 13, "local", "x2", "=", "p", ".", "x2", "and", "getScalePos", "(", "p", ".", "x2", ")")
                        && Match(i + 26, "local", "y1", "=", "p", ".", "y", "and", "getScalePos", "(", "p", ".", "y", ")")
                        && Match(i + 39, "local", "y2", "=", "p", ".", "y2", "and", "getScalePos", "(", "p", ".", "y2", ")");
                if (Match(i, "function", "getScalePos", "(", "p", ")"))
                {
                    for (int j = i + 5; j < Math.Min(tokens.Count, i + 40) && tokens[j].Text != "function"; j++)
                        scaledPositionHelper |= Match(j, "math", ".", "ceil", "(", "p", "*", "game", ".", "scale", ")");
                }
                if (tokens[i].Text == "if") anchorAdjustment |= Matches(tokens, i, BackgroundAnchorAdjustment);
                if(KeyName(tokens[i])=="game_scale")
                {
                    int eq=i+1;if(eq<tokens.Count&&tokens[eq].Text=="]")eq++;
                    if(eq+1<tokens.Count&&tokens[eq].Text=="="&&int.TryParse(tokens[eq+1].Text,out int b)&&b>0)bases.Add(b);
                }
                if (!Match(i,"function","getScale","(") || i+4>=tokens.Count || tokens[i+3].Kind!='i' || tokens[i+4].Text!=")") continue;
                string arg=tokens[i+3].Text;
                for(int j=i+5;j<Math.Min(tokens.Count,i+48);j++)
                {
                    if(tokens[j].Text=="function") break;
                    if(Match(j,arg,"*","game",".","scale")) { helper=true; break; }
                }
            }
        }
        return (scale && helper, directories, bases.Count == 1 ? bases.Single() : null, bases.Count > 1,
            (scale || tableBasedScale) && backgroundMovement && scaledPositionHelper && anchorAdjustment);
    }

    private static bool Matches(List<ScriptToken> tokens, int start, string[] pattern)
    {
        if (start + pattern.Length > tokens.Count) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (tokens[start + i].Text != pattern[i]) return false;
        return true;
    }
}
