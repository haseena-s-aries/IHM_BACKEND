using iText.IO.Font;
using iText.IO.Font.Otf;
using iText.Layout.Splitting;
using System.Collections.Generic;

public class CustomSplitCharacters : ISplitCharacters
{
    public bool IsSplitCharacter(GlyphLine text, int glyphPos)
    {
        // 👇 Always allow splitting at any character
        return true;
    }
}
