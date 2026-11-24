using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace UnnamedManager.Actions;

/// An app's name and icon for a context's app list (S-046): an exe or an
/// AppUserModelID, as the config names it. Icons come from the shell, are read on the
/// UI thread once and cached; an app that can't be found has no icon and no error.
public static class AppIcons
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

    private static WriteableBitmap? Load(string parsing, int size) => Pixels(parsing, size) is { } pixels ? Bitmap(pixels, size) : null;

    /// An image of the pixels, on the UI thread.
    public static WriteableBitmap Bitmap(byte[] pixels, int size)
    {
        var image = new WriteableBitmap(size, size);
        using (var stream = image.PixelBuffer.AsStream()) stream.Write(pixels);
        image.Invalidate();
        return image;
    }

    /// The shell's icon as premultiplied BGRA pixels, or null (ShellIcons).
    public static byte[]? Pixels(string parsing, int size) => ShellIcons.Pixels(parsing, size);
}
