using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

public class ContextAwareTests
{
    private static readonly FrontApp Edge = new(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", null, "Microsoft Edge");
    private static readonly FrontApp Notepad = new(@"C:\Program Files\WindowsApps\Notepad\Notepad.exe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Notepad");
    private static readonly FrontApp Code = new(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", null, "Visual Studio Code");

    private static readonly LoadedConfig Config = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space F", "title": "Find", "action": "find" },
          { "keys": "Hyper+Space L", "title": "Address bar", "action": "address" },
          { "keys": "Ctrl+Alt+L", "title": "Address bar", "action": "address" } ],
          "actions": {
            "find": { "type": "contextual",
              "variants": { "browsers": { "type": "send", "keys": "Primary+L" }, "editors": { "type": "send", "keys": "Primary+Shift+F" } },
              "fallback": { "type": "send", "keys": "Primary+F" } },
            "address": { "type": "contextual", "variants": { "browsers": { "type": "send", "keys": "Primary+L" } } } },
          "contexts": [
            { "id": "editors", "apps": ["Code.exe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"] },
            { "id": "browsers", "apps": ["msedge.exe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"] } ] }
        """).Loaded!;

    private static string? Resolved(string id, FrontApp? front) =>
        (ContextResolver.Resolve(Config.Config.Action(id)!, front, Config.Config.Contexts) as SendShortcutAction)?.Keys.ToString();

    [Fact]
    public void TheFirstMatchingContextInContextsOrderWins()
    {
        Assert.Equal("Ctrl+L", Resolved("find", Edge));
        Assert.Equal("Ctrl+Shift+F", Resolved("find", Code));
        // Notepad is in both: editors comes first in `contexts`.
        Assert.Equal("Ctrl+Shift+F", Resolved("find", Notepad));
    }

    [Fact]
    public void ContextsMatchByExeOrAppUserModelId()
    {
        Assert.True(Notepad.Is("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"));
        Assert.True(Notepad.Is("notepad.EXE"));
        Assert.True(Edge.Is(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"));
        Assert.False(Edge.Is(@"C:\Other\msedge.exe"));
        Assert.False(Code.Is("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"));
    }

    [Fact]
    public void WithNoMatchTheFallbackRunsAndWithNoFallbackItsUnavailable()
    {
        var other = new FrontApp(@"C:\Windows\explorer.exe", null, "File Explorer");
        Assert.Equal("Ctrl+F", Resolved("find", other));
        Assert.Equal("Ctrl+F", Resolved("find", null));
        Assert.Null(Resolved("address", other));
        Assert.False(Config.IsAvailable(Config.Leaders[0].Children[1], other));
        Assert.True(Config.IsAvailable(Config.Leaders[0].Children[1], Edge));
    }

    [Fact]
    public void PressingAnUnavailableBindingGivesErrorFeedbackAndRunsNothing()
    {
        var engine = new SequenceEngine(Config);
        engine.SetFrontApp(Code);
        engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Hyper));
        engine.ModifiersChanged(Modifiers.None);
        var output = engine.Handle(KeyEvent.Down(KeyName.L));
        Assert.True(output.Consume);
        Assert.Equal("Address bar", Assert.IsType<Unavailable>(output.Effect).Binding.Title);
        Assert.True(engine.IsActive);
    }

    [Fact]
    public void TheAppInFrontAtTheLeaderIsTheOneActionsGoTo()
    {
        var engine = new SequenceEngine(Config);
        engine.SetFrontApp(Edge);
        var show = Assert.IsType<ShowMenu>(engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Hyper)).Effect);
        Assert.Equal(Edge, show.Front);
        // The Guide never takes focus, but if the front app changed anyway, the leader's app still counts.
        engine.SetFrontApp(Code);
        engine.ModifiersChanged(Modifiers.None);
        var run = Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.L)).Effect);
        Assert.Equal(Edge, run.Front);
    }

    [Fact]
    public void AnUnavailableDirectBindingLetsTheChordThrough()
    {
        var engine = new SequenceEngine(Config);
        engine.SetFrontApp(Code);
        Assert.False(engine.Handle(KeyEvent.Down(KeyName.L, Modifiers.Ctrl | Modifiers.Alt)).Consume);
        Assert.False(engine.Handle(KeyEvent.Up(KeyName.L)).Consume);
        engine.SetFrontApp(Edge);
        Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.L, Modifiers.Ctrl | Modifiers.Alt)).Effect);
    }
}
