using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using UnnamedManager.UI;

namespace UnnamedManager;

/// The self-test's checks for spec v5 to v15 (S-061 to S-071): Fill with the gap, Focus
/// Window's targets and scope, Swap Window, Arrange Windows, built-in actions, the window
/// used before, each app's rules, and the alert sound. They run through ActionRunner on
/// fake windows, with the config's settings, as a binding would.
public static partial class SelfTest
{
    private static async Task CheckWindowTargetsAsync()
    {
        var saved = ActionRunner.Windows;
        var fake = new FakeWindowSystem();
        var settings = new WindowSettings();
        ActionRunner.Windows = new WindowControl(fake, () => settings);
        static Task<string?> Run(ConfigAction action) => ActionRunner.PerformAsync(ActionRegistry.Standard.Family(action.Type)!.Plan(action, PlanContext.Current)!);
        try
        {
            // S-061: Fill leaves the gap, and pressed again restores the first frame, also under its old name.
            var w = fake.Add(1, new(100, 100, 800, 600));
            await Run(new FillAction());
            var filled = w.Frame;
            await Run(new FillAction(true, FillAction.OldType));
            Check("Fill stands 8 pixels from every edge, and pressed again restores the first frame, under its old name too",
                filled == new PixelRect(8, 8, 1904, 1016) && w.Frame == new PixelRect(100, 100, 800, 600), $"{filled} then {w.Frame}");

            // S-062: First, Last and a number; with no focused window too.
            fake.Windows.Clear();
            fake.ZOrder.Clear();
            fake.Add(1, new(0, 0, 600, 1000));
            fake.Add(3, new(1300, 0, 600, 1000));
            fake.Add(2, new(650, 0, 600, 1000));
            await Run(new FocusAction("first"));
            var first = fake.Front;
            fake.Front = null;
            await Run(new FocusAction("last"));
            var last = fake.Front;
            fake.Front = null;
            await Run(new FocusAction(WindowTarget.Of(2)));
            Check("First and Last go to the ends, and Window 2 is the middle one, with no focused window too",
                (first, last, fake.Front) == (1, 3, 2), $"{first} {last} {fake.Front}");

            // S-063: the alert sound plays only when no window is there, and not with Play alert sound off.
            var beeps = Sound.Played;
            var past = await Run(new FocusAction(WindowTarget.Of(5)));
            var afterPast = Sound.Played - beeps;
            fake.Front = null;
            await Run(new SwapAction("left"));
            var afterNoFocus = Sound.Played - beeps;
            Check("a number past the last window says so and plays the alert sound; no focused window doesn't",
                past == "No window 5. There are 3." && afterPast == 1 && afterNoFocus == 1, $"{past} {afterPast} {afterNoFocus}");

            // S-064: Swap trades frames, keeps focus, and swaps back.
            fake.Click(2);
            await Run(new SwapAction("left"));
            var swapped = (fake.Windows[2].Frame.X, fake.Windows[1].Frame.X, fake.Front);
            await Run(new SwapAction("right"));
            Check("Swap left trades frames with the window on the left and keeps focus; Swap right puts them back",
                swapped == (0, 650, 2) && fake.Windows[2].Frame.X == 650 && fake.Windows[1].Frame.X == 0, $"{swapped}");

            // S-065: Arrange Windows puts the two windows used last in halves with the gap, and again moves nothing.
            settings = settings with { Gap = 8 };
            fake.Click(1);
            await Run(new ArrangeAction());
            var halves = (fake.Windows[1].Frame, fake.Windows[2].Frame, fake.Windows[3].Frame);
            await Run(new ArrangeAction());
            Check("Arrange Windows puts the two windows used last in halves, 8 apart, and running it again moves nothing",
                halves == (new PixelRect(8, 8, 948, 1016), new PixelRect(964, 8, 948, 1016), new PixelRect(1300, 0, 600, 1000))
                && (fake.Windows[1].Frame, fake.Windows[2].Frame) == (halves.Item1, halves.Item2), $"{halves}");

            // S-066: a built-in name runs its action, and an action with that name runs instead.
            var builtIn = new Core.Config.Config { Entries = [new("Ctrl+Alt+K", "K", "window-focus-first")] };
            var overridden = builtIn with { Actions = [new("window-focus-first", new FocusAction("last"))] };
            Check("a built-in name stands for its action, and an action with that name overrides it",
                builtIn.Resolve("window-focus-first") == new FocusAction("first") && overridden.Resolve("window-focus-first") == new FocusAction("last"));

            // S-068: Window used before goes back and forth, also to a covered window and across desktops.
            fake.Click(3);
            fake.CurrentDesktop = 1;
            fake.Add(4, new(0, 0, 1920, 1032));
            fake.Click(4);
            await Run(new FocusAction("recent"));
            var back = (fake.Front, fake.CurrentDesktop);
            await Run(new FocusAction("recent"));
            Check("Window used before goes back to the window on the other desktop, and again comes back",
                back == (3, 0) && fake.Front == 4, $"{back} then {fake.Front}");

            // S-069: each app's rules.
            fake.Windows[3].App = "Calculator";
            settings = settings with { Apps = [new("Calculator.exe", new AppRules(Focus: true, Swap: false, Arrange: false))] };
            fake.Click(3);
            var leftOut = await Run(new SwapAction("left"));
            Check("Swap Window says when the focused window's app is left out of it", leftOut == "Calculator is left out of Swap Window.", leftOut ?? "");
        }
        finally
        {
            ActionRunner.Windows = saved;
        }
    }
}
