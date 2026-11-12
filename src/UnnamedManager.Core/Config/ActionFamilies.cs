namespace UnnamedManager.Core.Config;

/// One `type` a family handles: its name in the file and its glossary name.
public sealed record ActionKind(string Type, string DisplayName);

/// An action family: one module that reads its own kinds of action. Reading
/// comes first (S-002); each family's story adds writing, its schema and its
/// plan (S-006 onward). The engine and the Guide know no type by name.
public interface IActionFamily
{
    IReadOnlyList<ActionKind> Kinds { get; }
    ConfigAction Read(ActionFields fields);
}

/// The families the app knows. The reader, and later the writer, the schema and
/// the Actions page, read it, so adding a family is one line here.
public sealed class ActionRegistry(IReadOnlyList<IActionFamily> families)
{
    public static ActionRegistry Standard { get; } = new([
        new UseApplicationFamily(), new OpenFamily(), new RunCommandFamily(), new SendShortcutFamily(), new BasicFamily(),
        new MaximizeFamily(), new MinimizeFamily(), new PlaceFamily(), new MoveFamily(), new ResizeFamily(), new FocusFamily(),
    ]);

    public IReadOnlyList<IActionFamily> Families { get; } = families;

    public IEnumerable<ActionKind> Kinds => Families.SelectMany(f => f.Kinds);

    public IActionFamily? Family(string type) => Families.FirstOrDefault(f => f.Kinds.Any(k => k.Type == type));
}

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

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}

/// Open: a file, folder or URL with its default app.
public sealed record OpenAction(string Target) : ConfigAction
{
    public override string Type => "open";
    public override string DisplayName => "Open";
}

public sealed class OpenFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("open", "Open")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target");
        return new OpenAction(f.RequiredString("target"));
    }
}

/// Run Command: starts a process directly, with no shell.
public sealed record RunCommandAction(IReadOnlyList<string> Command, string? Cwd, IReadOnlyList<KeyValuePair<string, string>> Env) : ConfigAction
{
    public override string Type => "exec";
    public override string DisplayName => "Run Command";
}

public sealed class RunCommandFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("exec", "Run Command")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("command", "cwd", "env");
        var command = f.Strings("command") ?? throw f.Problem(Rule.MissingField, $"Missing “command” in {f.Place}.");
        if (command.Count == 0) throw f.Problem(Rule.MissingField, $"“command” in {f.Place} is empty. Add the program to run.");
        return new RunCommandAction(command, f.OptionalString("cwd"), f.StringMap("env") ?? []);
    }
}

/// Send Shortcut: presses one or more chords in the app that was in front.
public sealed record SendShortcutAction(KeyPath Keys) : ConfigAction
{
    public override string Type => "send";
    public override string DisplayName => "Send Shortcut";
}

public sealed class SendShortcutFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("send", "Send Shortcut")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("keys");
        var text = f.RequiredString("keys");
        var keys = KeyPath.Parse(text)
            ?? throw f.Problem(Rule.BadKeys, $"The keys “{text}” in {f.Place} have a key that isn't a known name. Check the spelling of each key and modifier.");
        return new SendShortcutAction(keys);
    }
}

/// Do Nothing and Show Help: no fields.
public sealed record BasicAction(string Kind) : ConfigAction
{
    public override string Type => Kind;
    public override string DisplayName => Kind == "help" ? "Show Help" : "Do Nothing";
}

public sealed class BasicFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("noop", "Do Nothing"), new("help", "Show Help")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow();
        return new BasicAction(f.Type);
    }
}
