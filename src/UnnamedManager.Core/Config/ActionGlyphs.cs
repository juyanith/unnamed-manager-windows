namespace UnnamedManager.Core.Config;

/// The Segoe Fluent Icons glyph the Guide shows for an action (S-045), or null.
/// Window actions have one each; Place Window, Focus Window and Swap Window show their
/// preset or target. App, file and link icons are GuideIcons (S-060).
public static class ActionGlyphs
{
    public const string DockLeft = "", DockRight = "", DockBottom = "", Maximize = "", Minimize = "",
        Move = "", FullScreen = "", BackToWindow = "", Resize = "", Left = "", Right = "",
        Up = "", Down = "", Switch = "", Windows = "", History = "", PageLeft = "",
        PageRight = "", List = "", Sort = "", RepeatAll = "", Tiles = "";
    /// Task View, for Switch Desktop, Send to Desktop and Follow to Desktop (spec v17, v20, v23).
    public const string TaskView = "\uE7C4";

    public static string? For(ConfigAction? action) => action switch
    {
        PlaceAction { Preset: { } p } when p.StartsWith("left") || p is "topLeft" or "bottomLeft" => DockLeft,
        PlaceAction { Preset: { } p } when p.StartsWith("right") || p is "topRight" or "bottomRight" => DockRight,
        PlaceAction { Preset: "bottomHalf" } => DockBottom,
        PlaceAction => Windows,
        FillAction => Maximize,
        MinimizeAction => Minimize,
        MoveAction => Move,
        ResizeAction { Step: "grow" } => FullScreen,
        ResizeAction { Step: "shrink" } => BackToWindow,
        ResizeAction => Resize,
        ArrangeAction => Tiles,
        FocusAction { Target: var t } => t.Word switch
        {
            "left" => Left,
            "right" => Right,
            "up" => Up,
            "down" => Down,
            "first" => PageLeft,
            "last" => PageRight,
            "recent" => History,
            null => List,
            _ => Switch,
        },
        SwapAction { Target.Word: "left" or "right" } => Switch,
        SwapAction { Target.Word: "up" or "down" } => Sort,
        SwapAction => RepeatAll,
        SpaceSwitchAction or SpaceSendAction => TaskView,
        _ => null,
    };
}
