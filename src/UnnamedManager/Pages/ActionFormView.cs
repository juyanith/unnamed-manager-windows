using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// Builds an action's form fields from Core's `ActionForms` into a panel and reads
/// them back. Standard WinUI controls only, so they take the theme on their own.
public sealed class ActionFormView(StackPanel panel, string? scope = null)
{
    private readonly Dictionary<string, (FormField Field, Control Control)> _controls = [];

    /// Raised when a field's value is done changing: a text box loses focus, a
    /// choice or a toggle changes.
    public event Action? Edited;

    /// The read-only fields' values as they were given: a TextBox changes line breaks.
    private readonly Dictionary<string, string> _fixed = [];

    /// `unshowable` names the fields to show read-only (`ActionForms.Unshowable`).
    public void Show(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> values, IReadOnlySet<string>? unshowable = null)
    {
        panel.Children.Clear();
        _controls.Clear();
        _fixed.Clear();
        foreach (var field in fields)
        {
            var value = values.TryGetValue(field.Name, out var v) ? v : field.Default;
            var readOnly = unshowable?.Contains(field.Name) == true;
            if (readOnly) _fixed[field.Name] = value;
            Control control = field.Kind switch
            {
                FieldKind.Choice => Choice(field, value),
                FieldKind.Toggle => Toggle(field, value),
                FieldKind.Number => Number(field, value),
                _ => Text(field, value, readOnly),
            };
            // Inside a Context Aware action, Narrator also says which context it belongs to.
            AutomationName(control, scope is null ? field.Label : $"{scope}, {field.Label}");
            _controls[field.Name] = (field, control);
            panel.Children.Add(control);
        }
        UpdateShown();
    }

    /// Shows only the fields for the way chosen ("To a size", "By points").
    private void UpdateShown()
    {
        var values = Values;
        foreach (var (field, control) in _controls.Values)
            control.Visibility = field.IsShown(values) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _filling;

    private NumberBox Number(FormField field, string value)
    {
        var number = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        var box = new NumberBox
        {
            Header = field.Label,
            // The file can hold a number past the box's usual range (window 150): the box
            // makes room for it, or showing the action would change it.
            Minimum = Math.Min(field.Min, number),
            Maximum = Math.Max(field.Max, number),
            SmallChange = field.Step,
            LargeChange = field.Step * 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            // Out-of-range input is clamped, so the form never makes an invalid file.
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            Value = number,
        };
        if (field.Description.Length > 0)
            box.Description = new TextBlock { Text = field.Description, Style = (Style)Application.Current.Resources["FieldDescriptionTextStyle"] };
        box.ValueChanged += (_, args) =>
        {
            if (_filling) return;
            if (double.IsNaN(args.NewValue)) box.Value = Math.Clamp(0, field.Min, field.Max);
            if (field.Customizes is { } target && _controls.GetValueOrDefault(target).Control is ComboBox choice)
            {
                _filling = true;
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == "custom");
                _filling = false;
            }
            Edited?.Invoke();
        };
        return box;
    }

    /// A preset's or a spot's numbers, shown under its list when it changes.
    private void FillFrom(string field, string choice)
    {
        _filling = true;
        foreach (var (name, value) in ActionForms.Filled(field, choice))
            if (_controls.GetValueOrDefault(name).Control is NumberBox box) box.Value = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        _filling = false;
    }

    public IReadOnlyDictionary<string, string> Values => _controls.ToDictionary(c => c.Key, c => c.Value.Control switch
    {
        TextBox t => _fixed.TryGetValue(c.Key, out var kept) ? kept : t.Text,
        ComboBox b => (b.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
        ToggleSwitch s => s.IsOn ? "true" : "false",
        NumberBox n => (double.IsNaN(n.Value) ? 0 : n.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    });

    /// Sets a text field, as Choose app and Browse do.
    public void Set(string name, string value)
    {
        if (!_controls.TryGetValue(name, out var c)) return;
        switch (c.Control)
        {
            case TextBox box: box.Text = value; break;
            case NumberBox number: number.Value = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case ComboBox choice: choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value); break;
            case ToggleSwitch toggle: toggle.IsOn = value == "true"; break;
        }
    }

    public Control? Control(string name) => _controls.TryGetValue(name, out var c) ? c.Control : null;

    /// The labels of the shown fields made with one kind of control, in order.
    public IEnumerable<string> Controls(Type control) =>
        _controls.Values.Where(c => c.Control.GetType() == control && c.Control.Visibility == Visibility.Visible).Select(c => c.Field.Label);

    private const string ReadOnlyHelp = "Edit this one in the config file: it has a blank value, a line break or a name this field can't show.";

    private TextBox Text(FormField field, string value, bool readOnly)
    {
        var multi = readOnly || field.Kind is FieldKind.Lines or FieldKind.Pairs;
        var box = new TextBox
        {
            Header = field.Label,
            PlaceholderText = field.Kind == FieldKind.Pairs ? "NAME=value" : "",
            AcceptsReturn = multi,
            IsReadOnly = readOnly,
            TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
            // After AcceptsReturn: a one-line TextBox cuts its text at the first line break.
            Text = value,
        };
        var help = readOnly ? ReadOnlyHelp : field.Description;
        if (help.Length > 0)
            box.Description = new TextBlock { Text = help, Style = (Style)Application.Current.Resources["FieldDescriptionTextStyle"] };
        box.LostFocus += (_, _) => Edited?.Invoke();
        if (!multi) box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Edited?.Invoke(); };
        return box;
    }

    private ComboBox Choice(FormField field, string value)
    {
        var box = new ComboBox { Header = field.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (field.Description.Length > 0)
            box.Description = new TextBlock { Text = field.Description, Style = (Style)Application.Current.Resources["FieldDescriptionTextStyle"], TextWrapping = TextWrapping.Wrap };
        foreach (var choice in field.Choices ?? [])
            box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
        box.SelectionChanged += (_, _) =>
        {
            if (_filling) return;
            if ((box.SelectedItem as ComboBoxItem)?.Tag is string chosen) FillFrom(field.Name, chosen);
            UpdateShown();
            Edited?.Invoke();
        };
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
