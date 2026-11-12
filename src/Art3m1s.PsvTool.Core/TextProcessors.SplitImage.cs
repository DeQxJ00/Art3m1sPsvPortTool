using System.Globalization;
using System.Text;
namespace Art3m1s.PsvTool.Core;
public sealed partial class ArtemisTextProcessor
{
    // Resize local tile pitches, keeping device capability queries unchanged.
    private static string ScaleSplitImagePitch(string text, double ratio)
    {
        if (ratio == 1 || !text.Contains("g.texture_maxsize")) return text;
        var tokens = LexScript(text);
        var owners = new int[tokens.Count];
        List<(int Id, string Kind, bool AwaitDo)> stack = [];
        for (int n=0;n<tokens.Count;n++)
        {
            owners[n] = stack.LastOrDefault(x=>x.Kind=="function", (Id: -1,Kind: "",AwaitDo: false)).Id;
            if (tokens[n].Kind != 'i') continue;
            string word=tokens[n].Text;
            if (word is "function" or "if" or "for" or "while" or "repeat") stack.Add((n,word,word is "for" or "while"));
            else if (word=="do")
            {
                if(stack.Count>0 && stack[^1].AwaitDo) stack[^1]=(stack[^1].Id,stack[^1].Kind,false);
                else stack.Add((n,word,false));
            }
            else if (word is "end" or "until" && stack.Count>0) stack.RemoveAt(stack.Count-1);
        }
        var readers=tokens.Select((t,n)=>(t,n)).Where(x=>x.t.Kind=='s' && KeyName(x.t).EndsWith("/size.csv",StringComparison.Ordinal)).Select(x=>owners[x.n]).ToHashSet();
        List<ScriptEdit> edits=[];
        string At(int n)=>tokens[n].Text;
        for(int i=0;i+11<tokens.Count;i++)
        {
            if(owners[i]<0 || !readers.Contains(owners[i]) || At(i)!="local" || At(i+2)!="=" || At(i+3)!="tonumber" || At(i+4)!="(" || At(i+6)!=":" || At(i+7)!="var" || At(i+8)!="(" || KeyName(tokens[i+9])!="g.texture_maxsize" || At(i+10)!=")" || At(i+11)!=")") continue;
            int start=tokens[i+3].Start,end=tokens[i+11].End;
            edits.Add(new(start,end,"("+text[start..end]+"*"+ratio.ToString("G",CultureInfo.InvariantCulture)+")"));
        }
        var result=new StringBuilder(text);
        foreach(var edit in edits.OrderByDescending(e=>e.Start)) result.Remove(edit.Start,edit.End-edit.Start).Insert(edit.Start,edit.Text);
        return result.ToString();
    }
}
