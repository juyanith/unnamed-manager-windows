using System.Runtime.InteropServices;

namespace UnnamedManager.Actions;

/// The shell's icon for a path or an app, as pixels. No UI framework here: the WinUI and
/// WPF apps each turn the pixels into their own image (AppIcons.Bitmap).
public static unsafe partial class ShellIcons
{
    /// The shell's icon for a parsing name (a path, or shell:AppsFolder\<AppUserModelID>)
    /// as premultiplied BGRA pixels, or null. Any thread; call it off the UI thread for
    /// many icons (S-060).
    public static byte[]? Pixels(string parsing, int size)
    {
        var iid = IID_IShellItemImageFactory;
        if (SHCreateItemFromParsingName(parsing, 0, in iid, out var factory) != 0) return null;
        nint bitmap = 0;
        try
        {
            var getImage = (delegate* unmanaged<nint, SIZE, uint, nint*, int>)(*(nint**)factory)[3];
            if (getImage(factory, new SIZE { cx = size, cy = size }, SIIGBF_RESIZETOFIT | SIIGBF_ICONONLY, &bitmap) != 0) return null;
            var header = new UI.Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<UI.Win32.BITMAPINFOHEADER>(), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32,
            };
            var pixels = new byte[size * size * 4];
            var screen = UI.Win32.GetDC(0);
            try
            {
                fixed (byte* p = pixels)
                    if (GetDIBits(screen, bitmap, 0, (uint)size, p, ref header, 0) == 0) return null;
            }
            finally { UI.Win32.ReleaseDC(0, screen); }
            return pixels;
        }
        finally
        {
            if (bitmap != 0) UI.Win32.DeleteObject(bitmap);
            Marshal.Release(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    private const uint SIIGBF_RESIZETOFIT = 0, SIIGBF_ICONONLY = 0x4;
    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, byte* bits, ref UI.Win32.BITMAPINFOHEADER info, uint usage);
}
