using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager.UI;

/// An empty page's state (docs/DESIGN.md, Empty states): set Glyph, Title, Line and
/// ButtonText, and handle Invoked.
public sealed partial class EmptyState : UserControl
{
    public EmptyState() => InitializeComponent();

    public string Glyph { get => Icon.Glyph; set => Icon.Glyph = value; }
    public string Title { get => TitleText.Text; set => TitleText.Text = value; }
    public string Line { get => LineText.Text; set => LineText.Text = value; }
    public string ButtonText { get => (string)Action.Content; set => Action.Content = value; }

    public event Action? Invoked;

    /// Presses the button, for the self-test.
    public void Invoke() => Invoked?.Invoke();

    private void OnClick(object sender, RoutedEventArgs e) => Invoked?.Invoke();
}
