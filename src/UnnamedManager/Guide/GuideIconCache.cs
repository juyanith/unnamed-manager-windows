using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Guide;

/// The Guide's app, file and URL icons (S-060). Loaded when the config loads, on a
/// background thread of their own (the shell can be slow), and kept for the session.
/// `Get` only ever answers from the cache, so the Guide never waits: an icon not loaded
/// yet is asked for and shows next time. Nothing found means no icon and no error.
public static partial class GuideIconCache
{
    private static readonly Dictionary<string, ImageSource?> Images = [];
    private static readonly HashSet<string> Asked = [];
    private static DispatcherQueue? _ui;
    private static int _pending;

    /// Pixels for 24 epx at the display's scale, so icons are sharp at 100 to 200 %.
    public static int PixelSize { get; } = (int)Math.Round(24 * UI.Win32.GetDpiForSystem() / 96.0);

    /// No load in progress, for the self-test and snapshots.
    public static bool Idle => Volatile.Read(ref _pending) == 0;

    public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// Every icon the config's bindings use. On the UI thread.
    public static void Prefetch(LoadedConfig config) => Load(GuideIcons.All(config, Home));

    /// The icon for a binding's action, or null. Never waits.
    public static ImageSource? Get(ConfigAction? action)
    {
        if (GuideIcons.For(action, Home) is not { } source) return null;
        if (Images.TryGetValue(source.Key, out var image)) return image;
        Load([source]);
        return null;
    }

    /// True once the source has been looked up, found or not.
    public static bool Known(IconSource source) => Images.ContainsKey(source.Key);

    public static ImageSource? Cached(IconSource source) => Images.GetValueOrDefault(source.Key);

    public static void Load(IReadOnlyList<IconSource> sources)
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        var fresh = sources.Where(s => Asked.Add(s.Key)).ToList();
        if (fresh.Count == 0 || _ui is null) return;
        Interlocked.Increment(ref _pending);
        var size = PixelSize;
        var ui = _ui;
        // STA, as shell extensions that draw icons expect.
        var thread = new Thread(() =>
        {
            var results = fresh.Select(s => (s.Key, Pixels: Parsing(s) is { } parsing ? Actions.AppIcons.Pixels(parsing, size) : null)).ToList();
            ui.TryEnqueue(() =>
            {
                foreach (var (key, pixels) in results) Images[key] = pixels is null ? null : Actions.AppIcons.Bitmap(pixels, size);
                Interlocked.Decrement(ref _pending);
            });
        }) { IsBackground = true, Name = "Guide icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// What the shell is asked for: the exe's path, the Start entry, the file, or the
    /// exe that opens the URL's scheme. Null when there's nothing to find.
    private static string? Parsing(IconSource source)
    {
        try
        {
            return source switch
            {
                IconSource.Exe exe => Actions.AppSwitcher.Resolve(exe.Name),
                IconSource.AppId app => $@"shell:AppsFolder\{app.Id}",
                IconSource.File file => File.Exists(file.Path) || Directory.Exists(file.Path) ? file.Path : null,
                IconSource.Url url => SchemeApp(url.Scheme),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// The exe Windows opens a URL scheme with, as the shell's associations say.
    private static unsafe string? SchemeApp(string scheme)
    {
        var length = 1024u;
        var buffer = stackalloc char[(int)length];
        return AssocQueryStringW(ASSOCF_NONE, ASSOCSTR_EXECUTABLE, scheme, "open", buffer, ref length) == 0
            ? new string(buffer, 0, (int)length - 1) is { Length: > 0 } path && File.Exists(path) ? path : null
            : null;
    }

    private const uint ASSOCF_NONE = 0, ASSOCSTR_EXECUTABLE = 2;

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int AssocQueryStringW(uint flags, uint str, string assoc, string? extra, char* output, ref uint length);
}
