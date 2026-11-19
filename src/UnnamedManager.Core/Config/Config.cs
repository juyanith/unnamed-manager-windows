namespace UnnamedManager.Core.Config;

/// The config file, config.json (spec/CONFIG-SPEC.md). Reading is strict: an
/// unknown field is an error, so a typo never silently does nothing. Rules that
/// need the whole file live in ConfigValidator.
public sealed record Config
{
    public const int SupportedVersion = 1;

    /// The `$schema` line for editors, kept as written.
    public string? Schema { get; init; }
    public int Version { get; init; } = SupportedVersion;
    public RuntimeSettings Runtime { get; init; } = new();
    public UISettings UI { get; init; } = new();
    /// Settings the window actions share (spec v3).
    public WindowSettings Windows { get; init; } = new();
    /// In file order.
    public IReadOnlyList<Entry> Entries { get; init; } = [];
    /// Action id to action, in file order. JSON objects have no order, but
    /// keeping the file's keeps the Actions page and the writer predictable.
    public IReadOnlyList<KeyValuePair<string, ConfigAction>> Actions { get; init; } = [];
    public IReadOnlyList<ConfigContext> Contexts { get; init; } = [];

    public ConfigAction? Action(string id) => Actions.FirstOrDefault(a => a.Key == id).Value;
}

public sealed record RuntimeSettings
{
    public string CancelKey { get; init; } = "Escape";
    /// "" turns Back off.
    public string BackKey { get; init; } = "Backspace";
    public bool ShowHud { get; init; } = true;
    public int HudDelayMs { get; init; }
    public int TimeoutMs { get; init; }
}

public sealed record UISettings
{
    public string Theme { get; init; } = "system";
    public string Position { get; init; } = "center";
    public bool ErrorFlash { get; init; } = true;
    public bool ErrorBell { get; init; } = true;
}

public sealed record WindowSettings
{
    public const int MinLimit = 1, MaxLimit = 8;

    /// Effective pixels Place Window, Fill and Arrange Windows leave around windows and between them.
    public double Gap { get; init; } = 8;
    /// The most columns and rows Arrange Windows picks by itself (spec v8).
    public int MaxColumns { get; init; } = 2;
    public int MaxRows { get; init; } = 2;
    /// Focus Window's and Swap Window's distances, in effective pixels (spec v12).
    public double MinVisible { get; init; } = 20;
    public double RowTolerance { get; init; } = 20;
    public double TieTolerance { get; init; } = 2;
    /// Rules for each app's windows, keyed by app id, in file order (spec v15).
    public IReadOnlyList<KeyValuePair<string, AppRules>> Apps { get; init; } = [];

    /// The app ids an action leaves out: those whose `rule` is false.
    public IReadOnlyList<string> LeftOut(Func<AppRules, bool> rule) => [.. Apps.Where(a => !rule(a.Value)).Select(a => a.Key)];

    public bool Equals(WindowSettings? other) => other is not null
        && (Gap, MaxColumns, MaxRows, MinVisible, RowTolerance, TieTolerance) == (other.Gap, other.MaxColumns, other.MaxRows, other.MinVisible, other.RowTolerance, other.TieTolerance)
        && Apps.SequenceEqual(other.Apps);

    public override int GetHashCode() => HashCode.Combine(Gap, MaxColumns, MaxRows, MinVisible, RowTolerance, TieTolerance, Apps.Count);
}

/// Which window actions take an app's windows (spec v15). False leaves them out.
public sealed record AppRules(bool Focus = true, bool Swap = true, bool Arrange = true);

/// What a list holds (spec v18, Lists). Values stay as written, so the validator can
/// name a wrong one. `Minimized` and `Spaces` are null when the file leaves them out,
/// since with `"choose": "swap"` writing one at all is an error (spec v19).
public sealed record ListSpec(string Type = ListSpec.RecentWindows, int Count = 9, bool? Minimized = null, string? Spaces = null,
    string Scope = "all", string Choose = "focus")
{
    public const string RecentWindows = "windows.recent";
    public const int MinCount = 1, MaxCount = 99;
    public static readonly string[] SpacesValues = ["all", "current"];
    public static readonly string[] ScopeValues = ["all", "display"];
    public static readonly string[] ChooseValues = ["focus", "swap"];

    /// Choosing an item swaps with its window, instead of focusing it.
    public bool Swaps => Choose == "swap";
    /// Swap only works with windows shown on the current desktop.
    public bool IncludesMinimized => !Swaps && (Minimized ?? true);
    public bool CurrentDesktopOnly => Swaps || Spaces == "current";
    public bool DisplayOnly => Scope == "display";
}

/// A menu (no action) or a binding (with an action). `Hold` makes a leader's menus held
/// menus, and `List` makes a menu a list the app fills (spec v18).
public sealed record Entry(string Keys, string Title, string? Action = null, string? Icon = null, bool Hold = false, ListSpec? List = null)
{
    public bool IsMenu => Action is null;
}

/// A named set of apps for Context Aware actions. On Windows, a name ending in
/// `.exe` is an exe and any other is an AppUserModelID (spec v4).
public sealed record ConfigContext(string Id, IReadOnlyList<string> Apps);

/// Any action. Each family (ActionFamilies.cs) has its own subclasses; Context
/// Aware combines other actions and isn't a family.
public abstract record ConfigAction
{
    /// The `type` value in the file.
    public abstract string Type { get; }
    /// The glossary name shown in the UI.
    public abstract string DisplayName { get; }
}

/// Context Aware: the variant for the first matching context, else the fallback.
public sealed record ContextualAction(IReadOnlyList<KeyValuePair<string, ConfigAction>> Variants, ConfigAction? Fallback) : ConfigAction
{
    public override string Type => "contextual";
    public override string DisplayName => "Context Aware";
}
