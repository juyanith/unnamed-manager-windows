namespace UnnamedManager.Core.Config;

/// The Segoe Fluent Icons glyph the Guide shows for an action (S-045), or null.
/// Window actions have one each; Place Window and Focus Window show their preset or
/// direction. App, file and link icons are GuideIcons (S-060).
public static class ActionGlyphs
{
    public const string DockLeft = "", DockRight = "", DockBottom = "", Maximize = "", Minimize = "",
        Move = "", FullScreen = "", BackToWindow = "", Resize = "", Left = "", Right = "",
        Up = "", Down = "", Switch = "", Windows = "";

    public static string? For(ConfigAction? action) => action switch
    {
        PlaceAction { Preset: { } p } when p.StartsWith("left") || p is "topLeft" or "bottomLeft" => DockLeft,
        PlaceAction { Preset: { } p } when p.StartsWith("right") || p is "topRight" or "bottomRight" => DockRight,
        PlaceAction { Preset: "bottomHalf" } => DockBottom,
        PlaceAction => Windows,
        MaximizeAction => Maximize,
        MinimizeAction => Minimize,
        MoveAction => Move,
        ResizeAction { Step: "grow" } => FullScreen,
        ResizeAction { Step: "shrink" } => BackToWindow,
        ResizeAction => Resize,
        FocusAction { Direction: "left" } => Left,
        FocusAction { Direction: "right" } => Right,
        FocusAction { Direction: "up" } => Up,
        FocusAction { Direction: "down" } => Down,
        FocusAction => Switch,
        _ => null,
    };
}
