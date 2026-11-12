using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnnamedManager;

/// Where things live. The config file is %USERPROFILE%\.config\unnamed-manager\config.json
/// (the spec's location); APP_STORE names another file. The app's own preferences and
/// logs live in %LOCALAPPDATA%\UnnamedManager.
public static class AppPaths
{
    /// True when APP_STORE points somewhere else (tests, snapshots).
    public static bool IsOverridden => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_STORE"));

    /// Test runs only ever touch files in %TEMP%.
    public static bool IsTestStore =>
        IsOverridden && Path.GetFullPath(ConfigFile).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);

    public static string Folder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnnamedManager");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// The config file: what the user edits, and the only data the app has.
    public static string ConfigFile => IsOverridden
        ? Environment.GetEnvironmentVariable("APP_STORE")!
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "unnamed-manager", "config.json");

    /// Test runs keep their preferences next to the test config, so they never change the real ones.
    public static string Prefs => IsOverridden ? ConfigFile + ".settings.json" : Path.Combine(Folder, "settings.json");

    /// The log folder (S-016). Test runs keep theirs next to the test config.
    public static string Logs => IsOverridden
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ConfigFile))!, "logs")
        : Path.Combine(Folder, "logs");
}

/// Small preferences that aren't part of the config, saved as a JSON file, so a test run keeps its own copy.
public sealed class Prefs
{
    public string Theme { get; set; } = "system";
    /// Whether leaders are captured (S-009). Remembered across starts.
    public bool Enabled { get; set; } = true;

    private static Prefs? _current;
    public static Prefs Current => _current ??= Load();

    /// Reads the file again, as the next start would.
    public static Prefs Reread() => Load();

    private static Prefs Load()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(AppPaths.Prefs), AppJson.Default.Prefs) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.Prefs, JsonSerializer.Serialize(this, AppJson.Default.Prefs)); }
        catch (IOException e) { AgentLog.Line($"Prefs not saved: {e.Message}"); }
    }
}

/// Source-generated JSON: no reflection, so it survives trimming and NativeAOT (tools/publish.ps1),
/// and starts faster. Add every type the app saves as JSON here.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Prefs))]
[JsonSerializable(typeof(UI.LayoutFile))]
internal sealed partial class AppJson : JsonSerializerContext;
