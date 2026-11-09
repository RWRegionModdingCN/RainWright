using System;
using System.Globalization;
using System.Text;
using Menu.Remix.MixedUI;

namespace RainWright.Remix;

// Native WrapText inserts its break after the overflowing glyph. This UI boundary
// measures before appending, so mixed CJK/Latin text never enters the slider lane.
internal static class RemixTextLayout
{
    internal static string Wrap(string text, float width)
    {
        var output = new StringBuilder();
        foreach (string paragraph in (text ?? string.Empty).Replace("<LINE>", "\n").Replace("\r\n", "\n").Split('\n'))
        {
            if (output.Length > 0) output.Append('\n');
            var line = new StringBuilder();
            var characters = StringInfo.GetTextElementEnumerator(paragraph);
            while (characters.MoveNext())
            {
                string character = characters.GetTextElement();
                if (line.Length > 0 && LabelTest.GetWidth(line.ToString() + character) > width)
                {
                    // Preserve whole Latin words when possible; split long tokens
                    // and CJK by grapheme, never through a surrogate pair.
                    int space = line.ToString().LastIndexOf(' ');
                    if (space > 0 && !string.IsNullOrWhiteSpace(character))
                    {
                        output.Append(line.ToString(0, space).TrimEnd()).Append('\n');
                        string tail = line.ToString(space + 1, line.Length - space - 1);
                        line.Clear().Append(tail);
                    }
                    else { output.Append(line.ToString().TrimEnd()).Append('\n'); line.Clear(); }
                }
                if (line.Length > 0 || !string.IsNullOrWhiteSpace(character)) line.Append(character);
            }
            output.Append(line);
        }
        return output.ToString();
    }
}
