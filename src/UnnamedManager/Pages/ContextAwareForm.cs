using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.UI;

namespace UnnamedManager.Pages;

/// A Context Aware action's form (S-096, S-097): a switch for each context in Contexts
/// order, then one for every other app, each with a Type list and that type's whole form.
/// A section is rebuilt only when its saved value changed from outside it, so what you're
/// typing, and where the focus is, survive the saves.
public sealed class ContextAwareForm(StackPanel panel)
{
    private string? _id;
    private List<string> _contexts = [];
    private readonly List<VariantSection> _sections = [];

    /// The sections, for the self-test and snapshots.
    public IReadOnlyList<VariantSection> Sections => _sections;

    public void Show(string id, ContextualAction action, IReadOnlyList<ConfigContext> contexts, bool canEdit)
    {
        var ids = contexts.Select(c => c.Id).ToList();
        if (_id != id || !_contexts.SequenceEqual(ids))
        {
            Clear();
            _id = id;
            _contexts = ids;
            panel.Children.Add(Words(ids.Count == 0
                ? "Add a context on the Contexts page first. Then you can choose what this does in its apps."
                : "The first context, in Contexts order, that has the app in front wins."));
            foreach (var context in ids) Add(new VariantSection(id, context));
            Add(new VariantSection(id, null));
        }
        foreach (var section in _sections)
        {
            section.Update(section.Context is { } context
                ? action.Variants.FirstOrDefault(v => v.Key == context).Value
                : action.Fallback, canEdit);
        }
    }

    private void Add(VariantSection section)
    {
        _sections.Add(section);
        panel.Children.Add(section.Root);
    }

    /// Forgets the action. What its sections still hold is never saved.
    public void Clear()
    {
        foreach (var section in _sections) section.Close();
        _sections.Clear();
        panel.Children.Clear();
        _id = null;
        _contexts = [];
    }

    private static TextBlock Words(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["FieldDescriptionTextStyle"],
        TextWrapping = TextWrapping.Wrap,
    };
}

/// What a Context Aware action does in one context, or everywhere else when `Context` is null.
public sealed class VariantSection
{
    private readonly string _id;
    private readonly string _name;
    private readonly ToggleSwitch _switch;
    private readonly StackPanel _body;
    private readonly ComboBox _type;
    private readonly ActionFormView _form;
    private readonly StackPanel _appButtons;
    private readonly TextBlock _error;

    private bool _closed, _showing, _seen;
    /// The section's value in the draft, as last shown or written here.
    private string? _saved;
    private ConfigAction? _action;

    public VariantSection(string id, string? context)
    {
        _id = id;
        Context = context;
        _name = context ?? "Otherwise";
        var space = (double)Application.Current.Resources["SpaceMd"];

        _switch = new ToggleSwitch { Header = context ?? "Otherwise, do something else" };
        AutomationProperties.SetName(_switch, context is null ? "Otherwise, do something else" : $"In {context}");
        _type = new ComboBox { Header = "Type", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_type, $"{_name}, Type");
        foreach (var choice in ActionForms.TypeChoices(nested: true)) _type.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });

        var fields = new StackPanel { Spacing = space };
        _form = new ActionFormView(fields, _name);
        _appButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = (double)Application.Current.Resources["SpaceSm"], Visibility = Visibility.Collapsed };
        _appButtons.Children.Add(AppButton("Choose app…", "Pick an app from Start", ChooseAppAsync));
        _appButtons.Children.Add(AppButton("Browse…", "Pick an exe file", BrowseAsync));
        _error = new TextBlock { Style = (Style)Application.Current.Resources["FormErrorTextStyle"], Visibility = Visibility.Collapsed };

        _body = new StackPanel { Spacing = space, Visibility = Visibility.Collapsed };
        foreach (var part in new UIElement[] { _type, fields, _appButtons, _error }) _body.Children.Add(part);
        var all = new StackPanel { Spacing = space };
        all.Children.Add(_switch);
        all.Children.Add(_body);
        Root = new ContentControl { Content = all, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };

        _switch.Toggled += (_, _) => { if (!_showing) Switch(_switch.IsOn); };
        _type.SelectionChanged += (_, _) => TypeChanged();
        _form.Edited += Apply;
    }

    /// The context's id, or null for the fallback.
    public string? Context { get; }

    public ContentControl Root { get; }

    public ActionFormView Form => _form;

    public bool IsOn => _switch.IsOn;

    public string? FormType => (_type.SelectedItem as ComboBoxItem)?.Tag as string;

    public IReadOnlyList<string> TypeNames => [.. _type.Items.OfType<ComboBoxItem>().Select(i => (string)i.Content)];

    public string? ErrorText => _error.Visibility == Visibility.Visible ? _error.Text : null;

    public bool OffersAppPicker => _appButtons.Visibility == Visibility.Visible;

    /// Flips the switch, as a click would.
    public void SetOn(bool on) => _switch.IsOn = on;

    /// Picks a type, as a click would.
    public void SetType(string type) => _type.SelectedItem = _type.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == type);

    public void Close() => _closed = true;

    private static string? Written(ConfigAction? action) => action is null ? null : ConfigWriter.Action(action, []);

    /// Shows the draft's value, unless it's what's here already.
    public void Update(ConfigAction? action, bool canEdit)
    {
        Root.IsEnabled = canEdit;
        var written = Written(action);
        if (_seen && written == _saved) return;
        _seen = true;
        _saved = written;
        _action = action;
        _showing = true;
        _switch.IsOn = action is not null;
        _body.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        if (action is null)
        {
            _type.SelectedItem = null;
            _form.Show([], new Dictionary<string, string>());
            _appButtons.Visibility = Visibility.Collapsed;
        }
        else
        {
            SetType(ActionForms.FormType(action));
            ShowFields(ActionForms.FormType(action), action);
        }
        ShowError(null);
        _showing = false;
    }

    private void ShowFields(string type, ConfigAction? from)
    {
        if (from is not null) _form.Show(ActionForms.Fields(type) ?? [], ActionForms.Values(from), ActionForms.Unshowable(from));
        else _form.Show(ActionForms.Fields(type) ?? [], ActionForms.Defaults(type));
        _appButtons.Visibility = type == "application" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string? message)
    {
        _error.Text = message ?? "";
        _error.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool CanWrite => !_closed && !_showing && App.CanEdit && App.Editor.Draft.Action(_id) is ContextualAction;

    private void Switch(bool on)
    {
        if (!CanWrite) return;
        _body.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            ShowError(null);
            Save(null);
            return;
        }
        var start = new BasicAction("noop");
        _showing = true;
        SetType(start.Type);
        ShowFields(start.Type, start);
        _showing = false;
        Save(start);
    }

    private void TypeChanged()
    {
        if (_showing || FormType is not { } type) return;
        ShowFields(type, _action is { } saved && ActionForms.FormType(saved) == type ? saved : null);
        Apply();
    }

    /// Saves the section, or says in it why it isn't valid yet and leaves the file alone.
    public void Apply()
    {
        if (!CanWrite || !_switch.IsOn || FormType is not { } type) return;
        ConfigAction action;
        try
        {
            action = ActionForms.Build(type, _form.Values, _id, _action);
            if (_action is { } before) action = ActionForms.KeepNames(action, before);
        }
        catch (ConfigProblem problem)
        {
            ShowError(problem.Message);
            return;
        }
        ShowError(null);
        Save(action);
    }

    private void Save(ConfigAction? action)
    {
        var written = Written(action);
        if (written == _saved) return;
        // Before the change: saving refreshes the page, which mustn't rebuild this section.
        _saved = written;
        _action = action;
        var name = Context is null ? "Change fallback" : "Change variant";
        App.Editor.Change(name, c => Context is { } context ? c.SetVariant(_id, context, action) : c.SetFallback(_id, action));
    }

    /// Fills Use Application from an app in Start or an exe.
    public void UseApp(StartApp app)
    {
        _form.Set("exe", app.Exe ?? "");
        _form.Set("appId", app.AppId ?? "");
        Apply();
    }

    private Button AppButton(string text, string tip, Func<Task> run)
    {
        var button = new Button { Content = text };
        AutomationProperties.SetName(button, $"{_name}, {text}");
        ToolTipService.SetToolTip(button, tip);
        button.Click += async (_, _) => await run();
        return button;
    }

    private async Task ChooseAppAsync()
    {
        if (await AppPicker.ChooseAsync(Root.XamlRoot, Root.ActualTheme) is { } app) UseApp(app);
    }

    private async Task BrowseAsync()
    {
        if (await AppPicker.BrowseAsync(App.MainWindow.Handle()) is { } path) UseApp(new StartApp(Path.GetFileNameWithoutExtension(path), path, null));
    }
}
