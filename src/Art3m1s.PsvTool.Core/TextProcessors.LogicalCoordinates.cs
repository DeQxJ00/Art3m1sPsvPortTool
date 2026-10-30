namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    // These custom image commands use script-space positions. The matching
    // runtime converts them through game.scale; native lowercase tags use pixels.
    private static readonly HashSet<string> RuntimeScaledImageCommands = new(StringComparer.Ordinal)
    {
        "BG", "BG_XY", "EV", "EV_C", "CG", "CGS", "CG_X", "CG_Y", "CG_XY", "CG_XYA", "CG_AN", "CG_DEL",
        "ST", "ST0", "STG", "ST_X", "ST_Y", "ST_XY", "ST_DEL", "ST2_DEL"
    };

    public static async Task<bool> DetectRuntimeScaledCoordinatesAsync(string root, CancellationToken token = default)
    {
        bool scale = false, helper = false;
        foreach (string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
            .Where(p=>Path.GetExtension(p).Equals(".lua",StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            var tokens=LexScript(EncodedText.Decode(await File.ReadAllBytesAsync(path,token)).Text);
            bool Match(int i, params string[] pattern) => tokens.Skip(i).Take(pattern.Length).Select(t=>t.Text).SequenceEqual(pattern);
            for(int i=0;i<tokens.Count;i++)
            {
                scale |= Match(i,"game",".","scale","=","game",".","width","/","init",".","game_scale");
                if (!Match(i,"function","getScale","(") || i+4>=tokens.Count || tokens[i+3].Kind!='i' || tokens[i+4].Text!=")") continue;
                string arg=tokens[i+3].Text;
                for(int j=i+5;j<Math.Min(tokens.Count,i+48);j++)
                {
                    if(tokens[j].Text=="function") break;
                    if(Match(j,arg,"*","game",".","scale")) { helper=true; break; }
                }
            }
        }
        return scale && helper;
    }
}
