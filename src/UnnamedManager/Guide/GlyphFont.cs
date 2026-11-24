using System.Runtime.InteropServices;

namespace UnnamedManager.Guide;

/// Whether Windows' icon font has a glyph (S-093), so a code point it doesn't have
/// shows nothing rather than a box. No UI framework: both apps use it.
public static partial class GlyphFont
{
    public static bool Has(string glyph)
    {
        // The icon fonts' glyphs are all in the 16-bit range.
        if (glyph.Length != 1) return false;
        return Has("Segoe Fluent Icons", glyph) ?? Has("Segoe MDL2 Assets", glyph) ?? false;
    }

    /// Null when Windows doesn't have that font.
    private static unsafe bool? Has(string face, string glyph)
    {
        var screen = UI.Win32.GetDC(0);
        var dc = UI.Win32.CreateCompatibleDC(screen);
        var font = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, DEFAULT_CHARSET, 0, 0, 0, 0, face);
        var previous = UI.Win32.SelectObject(dc, font);
        try
        {
            Span<char> name = stackalloc char[64];
            var length = GetTextFaceW(dc, name.Length, name);
            // Without the font, GDI picks another and says which.
            if (length <= 1 || !new string(name[..(length - 1)]).Equals(face, StringComparison.OrdinalIgnoreCase)) return null;
            ushort index = 0;
            return GetGlyphIndicesW(dc, glyph, 1, &index, GGI_MARK_NONEXISTING_GLYPHS) != GDI_ERROR && index != 0xFFFF;
        }
        finally
        {
            UI.Win32.SelectObject(dc, previous);
            UI.Win32.DeleteObject(font);
            UI.Win32.DeleteDC(dc);
            UI.Win32.ReleaseDC(0, screen);
        }
    }

    private const uint DEFAULT_CHARSET = 1, GGI_MARK_NONEXISTING_GLYPHS = 1, GDI_ERROR = 0xFFFFFFFF;

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetTextFaceW(nint dc, int size, Span<char> name);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)] private static unsafe partial uint GetGlyphIndicesW(nint dc, string text, int count, ushort* indices, uint flags);
}
