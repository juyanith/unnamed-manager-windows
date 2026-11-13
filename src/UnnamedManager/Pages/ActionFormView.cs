using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// Builds an action's form fields from Core's `ActionForms` into a panel and reads
/// them back. Standard WinUI controls only, so they take the theme on their own.
public sealed class ActionFormView(StackPanel panel)
{
    private readonly Dictionary<string, (FormField Field, Control Control)> _controls = [];

    /// Raised when a field's value is done changing: a text box loses focus, a
    /// choice or a toggle changes.
    public event Action? Edited;

    public void Show(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> values)
    {
        panel.Children.Clear();
        _controls.Clear();
        foreach (var field in fields)
        {
            var value = values.TryGetValue(field.Name, out var v) ? v : field.Default;
            Control control = field.Kind switch
            {
                FieldKind.Choice => Choice(field, value),
                FieldKind.Toggle => Toggle(field, value),
                _ => Text(field, value),
            };
            AutomationName(control, field.Label);
            _controls[field.Name] = (field, control);
            panel.Children.Add(control);
        }
    }

    public IReadOnlyDictionary<string, string> Values => _controls.ToDictionary(c => c.Key, c => c.Value.Control switch
    {
        TextBox t => t.Text,
        ComboBox b => (b.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
        ToggleSwitch s => s.IsOn ? "true" : "false",
        _ => "",
    });

    /// Sets a text field, as Choose app and Browse do.
    public void Set(string name, string value)
    {
        if (_controls.TryGetValue(name, out var c) && c.Control is TextBox box) box.Text = value;
    }

    public Control? Control(string name) => _controls.TryGetValue(name, out var c) ? c.Control : null;

    private TextBox Text(FormField field, string value)
    {
        var multi = field.Kind is FieldKind.Lines or FieldKind.Pairs;
        var box = new TextBox
        {
            Header = field.Label,
            Text = value,
            PlaceholderText = field.Kind == FieldKind.Pairs ? "NAME=value" : "",
            AcceptsReturn = multi,
            TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (field.Description.Length > 0)
            box.Description = new TextBlock { Text = field.Description, Style = (Style)Application.Current.Resources["FieldDescriptionTextStyle"] };
        box.LostFocus += (_, _) => Edited?.Invoke();
        if (!multi) box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Edited?.Invoke(); };
        return box;
    }

    private ComboBox Choice(FormField field, string value)
    {
        var box = new ComboBox { Header = field.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in field.Choices ?? [])
            box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
        box.SelectionChanged += (_, _) => Edited?.Invoke();
        return box;
    }

    private ToggleSwitch Toggle(FormField field, string value)
    {
        var toggle = new ToggleSwitch { Header = field.Label, IsOn = value == "true" };
        toggle.Toggled += (_, _) => Edited?.Invoke();
        return toggle;
    }

    private static void AutomationName(Control control, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, name);
}
