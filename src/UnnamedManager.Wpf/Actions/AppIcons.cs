using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UnnamedManager.Actions;

/// App, file and URL icons as WPF images, from the shell's pixels (ShellIcons).
/// GuideIconCache, shared with the WinUI app, calls these two.
public static class AppIcons
{
    /// The shell's icon as premultiplied BGRA pixels, or null. Any thread.
    public static byte[]? Pixels(string parsing, int size) => ShellIcons.Pixels(parsing, size);

    /// An image of the pixels, on the UI thread.
    public static BitmapSource Bitmap(byte[] pixels, int size)
    {
        var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        image.Freeze();
        return image;
    }
}
