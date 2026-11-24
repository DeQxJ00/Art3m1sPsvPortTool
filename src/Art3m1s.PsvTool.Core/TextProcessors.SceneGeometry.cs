using System.Globalization;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    // The compiled commands supply source-space coordinates. Convert their
    // positions and anchors where the Lua helpers pass them to pixel layers.
    private static void ScaleLegacySceneGeometry(string text, List<ScriptToken> tokens,
        bool[] vita, double ratio, List<ScriptEdit> edits)
    {
        string factor = ratio.ToString("G", CultureInfo.InvariantCulture);
        string[] axes = ["x1", "x2", "y1", "y2"];
        bool scaledSprite = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (vita[i] || tokens[i].Text != "function") continue;
            if (Match(i, "function", "getSpritePOS", "(", "p", ")"))
            {
                int body = i + 5;
                if (Match(body, "if", "p", ".", "y", "==", "\"0-10\"", "then", "p", ".", "y", "=", "-", "10", "end"))
                    body += 14;
                if (!Match(body, "local", "spritepos", "=", "{",
                    "x", "=", "(", "640", "+", "p", ".", "x", "-", "p", ".", "ax", ")", ",",
                    "y", "=", "(", "360", "+", "p", ".", "y", "-", "p", ".", "ay", ")",
                    "}", "return", "spritepos", "end")) continue;
                ScaleExpression(body + 6, body + 16);
                ScaleExpression(body + 20, body + 30);
                scaledSprite = true;
            }
            else if (Match(i, "function", "bg_scroll", "(", "param", ")"))
            {
                int start = i + 5;
                if (!axes.Select((axis, index) => Match(start + index * 7,
                    "local", axis, "=", "-", "param", ".", axis)).All(matches => matches)
                    || !Match(start + axes.Length * 7, "local", "time", "=", "param", ".", "fade")) continue;
                for (int index = 0; index < axes.Length; index++)
                    ScaleExpression(start + index * 7 + 3, start + index * 7 + 6);
            }
        }

        if (!scaledSprite) return;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (vita[i] || tokens[i].Text != "anchorx") continue;
            if (Match(i - 16, "left", "=", "(", "m", ".", "x", ")", ",",
                "top", "=", "(", "m", ".", "y", ")", ",",
                "anchorx", "=", "(", "p", ".", "ax", ")", ",", "anchory", "=", "(", "p", ".", "ay", ")", ",", "rotate"))
            {
                ScaleExpression(i + 2, i + 6);
                ScaleExpression(i + 10, i + 14);
            }
            else if (Match(i - 8, "left", "=", "\"0\"", ",", "top", "=", "\"0\"", ",",
                "anchorx", "=", "\"640\"", ",", "anchory", "=", "\"360\"", ",", "alpha"))
            {
                ScaleLiteral(i + 2, 640);
                ScaleLiteral(i + 6, 360);
            }
        }

        bool Match(int index, params string[] pattern)
        {
            if (index < 0 || index + pattern.Length > tokens.Count) return false;
            for (int offset = 0; offset < pattern.Length; offset++)
            {
                string expected = pattern[offset];
                string actual = tokens[index + offset].Text;
                if (expected[0] == '"' && actual[0] == '\'')
                    actual = "\"" + actual[1..^1] + "\"";
                if (actual != expected) return false;
            }
            return true;
        }

        void ScaleExpression(int first, int last)
        {
            int start = tokens[first].Start;
            int end = tokens[last].End;
            edits.Add(new(start, end, "(" + text[start..end] + "*" + factor + ")"));
        }

        void ScaleLiteral(int index, int original)
        {
            ScriptToken token = tokens[index];
            string value = ((int)(original * ratio)).ToString(CultureInfo.InvariantCulture);
            edits.Add(new(token.Start, token.End, token.Text[0] + value + token.Text[^1]));
        }
    }
}
