using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// What the app does with the Guide and the sound.
public abstract record GuideCommand
{
    /// Show this menu, or update the Guide to it if it's already up.
    public sealed record Show(BindingNode Menu, IReadOnlyList<string> Path, FrontApp? Front) : GuideCommand;
    /// Show Home, or update the Guide to it (S-021).
    public sealed record ShowHome(Home Home, IReadOnlyList<string> Path, FrontApp? Front) : GuideCommand;
    /// Hide the Guide. `Cancelled` when the sequence was cancelled, for Narrator.
    public sealed record Hide(bool Cancelled = false) : GuideCommand;
}

/// When the Guide shows, updates and hides. Pure: sequence effects in, with the
/// time in milliseconds; commands out. Every Guide timing rule lives here, so unit
/// tests check them with a made-up clock.
public sealed class GuideSchedule(RuntimeSettings runtime, UISettings ui)
{
    public RuntimeSettings Runtime { get; } = runtime;
    public UISettings UI { get; } = ui;

    /// True while the Guide is on screen.
    public bool IsVisible { get; private set; }

    public IReadOnlyList<GuideCommand> Handle(SequenceEffect effect, long now)
    {
        switch (effect)
        {
            case SequenceEffect.ShowMenu m:
                return Reveal(new GuideCommand.Show(m.Menu, m.Path, m.Front));
            case SequenceEffect.ShowHome h:
                return Reveal(new GuideCommand.ShowHome(h.Home, h.Path, h.Front));
            case SequenceEffect.Run:
                IsVisible = false;
                return [new GuideCommand.Hide()];
            case SequenceEffect.Cancelled:
                IsVisible = false;
                return [new GuideCommand.Hide(Cancelled: true)];
            default:
                return [];
        }
    }

    private IReadOnlyList<GuideCommand> Reveal(GuideCommand show)
    {
        if (!Runtime.ShowHud) return [];
        IsVisible = true;
        return [show];
    }
}
