using System.Runtime.InteropServices;

namespace UnnamedManager.Actions;

/// An app in Start: its name, and the exe or AppUserModelID Use Application takes.
public sealed record StartApp(string Name, string? Exe, string? AppId)
{
    public string Detail => Exe ?? AppId ?? "";
}

/// The apps in Start (shell:AppsFolder), packaged ones included, for Choose app (S-044,
/// S-046). A desktop app's parsing name is its exe's path, often with a known folder's
/// GUID in front; any other name is an AppUserModelID. Shortcuts to documents and
/// links are left out.
public static unsafe partial class StartApps
{
    public static IReadOnlyList<StartApp> List()
    {
        var apps = new List<StartApp>();
        var iid = IID_IShellItem;
        if (SHCreateItemFromParsingName("shell:AppsFolder", 0, in iid, out var folder) != 0) return apps;
        try
        {
            var bhid = BHID_EnumItems;
            var iidEnum = IID_IEnumShellItems;
            var vtable = *(nint**)folder;
            nint items;
            if (((delegate* unmanaged<nint, nint, Guid*, Guid*, nint*, int>)vtable[3])(folder, 0, &bhid, &iidEnum, &items) != 0) return apps;
            try
            {
                var next = (delegate* unmanaged<nint, uint, nint*, uint*, int>)(*(nint**)items)[3];
                nint item;
                uint fetched;
                while (next(items, 1, &item, &fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        if (Read(item) is { } app) apps.Add(app);
                    }
                    finally { Marshal.Release(item); }
                }
            }
            finally { Marshal.Release(items); }
        }
        finally { Marshal.Release(folder); }
        return apps.DistinctBy(a => a.Detail.ToLowerInvariant()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static StartApp? Read(nint item)
    {
        var name = DisplayName(item, SIGDN_NORMALDISPLAY);
        var parsing = DisplayName(item, SIGDN_PARENTRELATIVEPARSING);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parsing)) return null;
        if (!parsing.Contains('\\')) return new StartApp(name, null, parsing);
        if (!parsing.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return new StartApp(name, ExpandKnownFolder(parsing), null);
    }

    /// "{6D809377-…}\Microsoft VS Code\Code.exe" to "C:\Program Files\Microsoft VS Code\Code.exe".
    private static string ExpandKnownFolder(string path)
    {
        if (path.Length < 38 || path[0] != '{' || !Guid.TryParse(path[..38], out var id)) return path;
        if (SHGetKnownFolderPath(in id, 0, 0, out var folder) != 0) return path;
        try { return Marshal.PtrToStringUni(folder) + path[38..]; }
        finally { Marshal.FreeCoTaskMem(folder); }
    }

    private static string? DisplayName(nint item, uint kind)
    {
        nint text;
        var get = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)item)[5];
        if (get(item, kind, &text) != 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
    [LibraryImport("shell32.dll")] private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}
