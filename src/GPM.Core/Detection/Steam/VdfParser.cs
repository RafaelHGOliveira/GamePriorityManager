using System.Text;

namespace GPM.Core.Detection.Steam;

/// Minimal parser for Valve's text KeyValues format (.vdf/.acf).
public static class VdfParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var pos = 0;
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        ParseInto(root, text, ref pos);
        return root;
    }

    private static void ParseInto(Dictionary<string, object> target, string text, ref int pos)
    {
        while (true)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length) return;
            if (text[pos] == '}') { pos++; return; }
            var key = ReadString(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (text[pos] == '{')
            {
                pos++;
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ParseInto(child, text, ref pos);
                target[key] = child;
            }
            else
            {
                target[key] = ReadString(text, ref pos);
            }
        }
    }

    private static void SkipWhitespace(string t, ref int pos)
    {
        while (pos < t.Length && char.IsWhiteSpace(t[pos])) pos++;
    }

    private static string ReadString(string t, ref int pos)
    {
        pos++; // opening quote
        var sb = new StringBuilder();
        while (t[pos] != '"')
        {
            if (t[pos] == '\\') pos++; // unescape \" and \\
            sb.Append(t[pos]);
            pos++;
        }
        pos++; // closing quote
        return sb.ToString();
    }
}
