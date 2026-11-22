using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// The Bindings page's key recorder (S-040).
public sealed partial class BindingsPage
{
    /// Selects the row and turns its key recorder on: the hook stops matching
    /// leaders and sends the next key here (S-040).
    public void StartRecording(KeyPath path)
    {
        if (!CanEdit) return;
        Recording = path;
        App.BeginRecording();
        ShowKeyError(null);
        Refresh(path);
        KeyField.Focus(FocusState.Programmatic);
    }

    public void StopRecording()
    {
        if (Recording is null) return;
        Recording = null;
        App.Keys.Recording = false;
        ShowKeyError(null);
        ShowDetail(Selected);
    }

    /// A key from the recorder: the row takes it, or the reason it can't shows under
    /// the field and the recorder stays on. Esc on its own stops recording.
    public void OnRecorded(Chord chord)
    {
        if (Recording is not { } path) return;
        if (chord is { Modifiers: Modifiers.None, Key: KeyName.Escape }) { StopRecording(); return; }
        if (KeyProblemText(path, chord) is { } problem) { ShowKeyError(problem); return; }
        KeyPath renamed = path;
        Editor.Change("Change key", c => c.ChangeKey(path, chord, out renamed));
        Editor.Renamed(path, renamed);
        Recording = null;
        App.Keys.Recording = false;
        ShowKeyError(null);
        Refresh(renamed);
    }

    /// Why a key can't be the row's, in words, or null.
    public static string? KeyProblemText(KeyPath path, Chord chord) => Editor.Draft.CheckKey(path, chord) switch
    {
        KeyProblem.ModifierInStep => "Keys inside a menu can't use modifiers. Only the leader can.",
        KeyProblem.ReservedByWindows => $"Windows keeps {chord} for itself, so Unnamed Manager never sees it.",
        KeyProblem.CancelOrBack => $"{chord} is the {(Editor.Draft.Runtime.CancelKey.Equals(chord.Key.Name(), StringComparison.OrdinalIgnoreCase) ? "cancel" : "back")} key.",
        KeyProblem.Taken => $"{chord} is already used by “{Editor.Draft.Entry(path.WithKey(chord))?.Title}”.",
        KeyProblem.RepeatKey => $"{chord} opens this held menu, so pressing it again moves the highlight. Choose another key.",
        _ => null,
    };

    private void OnKeyFieldLostFocus(object sender, RoutedEventArgs e) => StopRecording();

    private void OnKeyField(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) StartRecording(item.Node.Path);
    }
}
