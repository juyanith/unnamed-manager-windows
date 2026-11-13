using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace UnnamedManager.Actions;

/// An app's name and icon for a context's app list (S-046): an exe or an
/// AppUserModelID, as the config names it. Icons come from the shell, are read on the
/// UI thread once and cached; an app that can't be found has no icon and no error.
public static unsafe partial class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// The app's name from Start, else its exe's description, else the name as written.
    public static string Name(string app)
    {
        var start = Pages.AppPicker.Apps.FirstOrDefault(a =>
            string.Equals(a.AppId, app, StringComparison.OrdinalIgnoreCase)
            || (a.Exe is { } exe && string.Equals(Path.GetFileName(exe), Path.GetFileName(app), StringComparison.OrdinalIgnoreCase)));
        if (start is not null) return start.Name;
        if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && AppSwitcher.Resolve(app) is { } path)
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description;
        }
        return app;
    }

    public static ImageSource? For(string app, int size)
    {
        var key = $"{size}|{app}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var parsing = app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? AppSwitcher.Resolve(app)
            : $@"shell:AppsFolder\{app}";
        var image = parsing is null ? null : Load(parsing, size);
        Cache[key] = image;
        return image;
    }

    private static WriteableBitmap? Load(string parsing, int size)
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
            var image = new WriteableBitmap(size, size);
            using (var stream = image.PixelBuffer.AsStream()) stream.Write(pixels);
            image.Invalidate();
            return image;
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
