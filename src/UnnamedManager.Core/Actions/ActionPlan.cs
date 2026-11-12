namespace UnnamedManager.Core.Config;

/// One thing to do in the world, worked out from an action with no side effects.
/// The app carries it out (ActionRunner), so every rule here is unit-tested and the
/// self-test can check plans without running them.
public abstract record ActionPlan
{
    /// Use Application: bring an app forward, start it, or both, by its mode.
    public sealed record UseApp(UseApplicationAction App) : ActionPlan;

    /// Run Command: start a program directly, with no shell, and don't wait for it.
    public sealed record Run(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyList<KeyValuePair<string, string>> Env) : ActionPlan
    {
        /// What starts it: the arguments quoted for CommandLineToArgvW, `env` added to
        /// the inherited environment.
        public System.Diagnostics.ProcessStartInfo StartInfo()
        {
            var start = new System.Diagnostics.ProcessStartInfo(Program, CommandLine.Join(Arguments))
            {
                UseShellExecute = false,
                WorkingDirectory = WorkingDirectory,
            };
            foreach (var (key, value) in Env) start.Environment[key] = value;
            return start;
        }
    }

    /// Send Shortcut: press these chords in the app in front, in order.
    public sealed record Send(IReadOnlyList<Chord> Chords) : ActionPlan
    {
        public bool Equals(Send? other) => other is not null && Chords.SequenceEqual(other.Chords);
        public override int GetHashCode() => Chords.Count;
    }

    /// Do Nothing.
    public sealed record Nothing : ActionPlan;

    /// A window action on the window in front (M3).
    public sealed record Window(Windows.WindowCommand Command) : ActionPlan;

    /// Show Help: the Help page of the Bindings window (M5).
    public sealed record ShowHelp : ActionPlan;

    /// Open a file, folder or URL with its default app, as double-clicking it would.
    public sealed record Open(string Target) : ActionPlan
    {
        /// A URL or another protocol ("https://…", "mailto:…"), not a path.
        public bool IsUrl => Uri.TryCreate(Target, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme.Length > 1;
    }
}
