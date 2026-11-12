using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

public enum AppMode { FocusOrStart, Focus, Start }

/// Use Application. On Windows an app is named by `exe` (a file name or full
/// path) or `appId` (an AppUserModelID), exactly one (spec v4).
public sealed record UseApplicationAction(string? Exe, string? AppId, AppMode Mode = AppMode.FocusOrStart) : ConfigAction
{
    public override string Type => "application";
    public override string DisplayName => "Use Application";

    /// What the app is called in messages: the exe or the AppUserModelID.
    public string AppName => Exe ?? AppId!;

    public static bool IsExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// True when `Exe` is a full path rather than a file name.
    public bool IsPath => Exe is { } exe && (exe.Contains('\\') || exe.Contains('/'));

    /// Whether a running program's full path is this app: a file name matches that
    /// file name in any folder, a full path matches only that file (spec v4).
    public bool MatchesExe(string runningPath)
    {
        if (Exe is not { } exe) return false;
        if (IsPath) return string.Equals(Path.GetFullPath(exe), Path.GetFullPath(runningPath), StringComparison.OrdinalIgnoreCase);
        return string.Equals(Path.GetFileName(runningPath), exe.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class UseApplicationFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("application", "Use Application")];

    private static readonly string[] Modes = ["focusOrStart", "focus", "start"];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("bundleId", "exe", "appId", "mode");
        if (f.Has("bundleId"))
            throw f.Problem(Rule.OtherPlatformField, $"{Cap(f.Place)} uses “bundleId”, which names a Mac app. On Windows, name the app with “exe” or “appId”.");
        var mode = f.Choice("mode", Modes, Rule.BadAppMode) ?? "focusOrStart";
        var which = f.OneOf("exe", "appId");
        var name = f.RequiredString(which);
        if (which == "exe" && !UseApplicationAction.IsExe(name.Trim()))
            throw f.Problem(Rule.BadAppId, $"The exe “{name}” in {f.Place} should end in .exe, such as “Code.exe”.");
        if (which == "appId" && string.IsNullOrWhiteSpace(name))
            throw f.Problem(Rule.BadAppId, $"The appId in {f.Place} is blank. Use an AppUserModelID, or “exe” instead.");
        var appMode = Array.IndexOf(Modes, mode) switch { 1 => AppMode.Focus, 2 => AppMode.Start, _ => AppMode.FocusOrStart };
        return which == "exe" ? new UseApplicationAction(name, null, appMode) : new UseApplicationAction(null, name, appMode);
    }

    /// `exe` or `appId`, then `mode` unless it's the default, Focus or Start.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var app = (UseApplicationAction)action;
        var fields = new List<KeyValuePair<string, JsonNode>>
        {
            app.Exe is { } exe ? KeyValuePair.Create("exe", (JsonNode)exe) : KeyValuePair.Create("appId", (JsonNode)app.AppId!),
        };
        if (app.Mode != AppMode.FocusOrStart) fields.Add(KeyValuePair.Create("mode", (JsonNode)Modes[(int)app.Mode]));
        return fields;
    }

    /// One variant for `exe` and one for `appId`, as in the spec's schema (spec v4).
    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        Variant("exe", "Windows: the program's file name, such as Code.exe, or its full path.", new JsonObject { ["pattern"] = @"\.[Ee][Xx][Ee]$" }),
        Variant("appId", "Windows: an AppUserModelID, for Store and packaged apps.", new JsonObject { ["minLength"] = 1 }),
    ];

    private static JsonObject Variant(string field, string description, JsonObject extra)
    {
        var property = new JsonObject { ["description"] = description };
        foreach (var (key, value) in extra.ToList()) { extra.Remove(key); property[key] = value; }
        property["type"] = "string";
        return new JsonObject
        {
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                [field] = property,
                ["mode"] = new JsonObject
                {
                    ["description"] = "focusOrStart (default): bring it forward, starting it if needed. focus: only if running. start: a new copy.",
                    ["enum"] = new JsonArray("focusOrStart", "focus", "start"),
                },
                ["type"] = new JsonObject { ["enum"] = new JsonArray("application") },
            },
            ["required"] = new JsonArray("type", field),
            ["type"] = "object",
        };
    }

    public ActionPlan Plan(ConfigAction action) => new ActionPlan.UseApp((UseApplicationAction)action);

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
