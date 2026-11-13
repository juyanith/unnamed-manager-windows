using UnnamedManager.Core.Config;

namespace UnnamedManager.Core;

/// Where first-start setup stands (S-058), from plain values so tests reach it: the
/// config file opened once, the Office shortcuts off (or a handler of your own, which
/// the app leaves alone), and one sequence run.
public sealed record WelcomeStatus(bool OpenedConfig, bool? OfficeShortcutsOn, bool RanSequence)
{
    public bool OfficeDone => OfficeShortcutsOn != true;

    public bool IsComplete => OpenedConfig && OfficeDone && RanSequence;

    /// Welcome shows at each start until you've run one sequence; Later only closes it.
    public static bool ShowsAtStart(bool ranSequence) => !ranSequence;

    /// The third step: the first leader that opens a menu, and that menu's name.
    public static (string Title, string Detail) TryStep(LoadedConfig? config) =>
        config?.Leaders.FirstOrDefault(l => l.Children.Count > 0) is { } leader
            ? ($"Try {leader.Path}", $"Then press a key from its {leader.Title} menu.")
            : ("Try your leader", "Add a leader in the Bindings window first.");
}
