using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using UnnamedManager.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UnnamedManager;

/// The self-test's Guide checks; SelfTest.cs runs them in order.
public static partial class SelfTest
{
    /// S-005: the Guide shows without taking the foreground, and stays out of the taskbar and Alt+Tab.
    private static async Task CheckGuideAsync(MainWindow window)
    {
        if (App.Config.Current is not { } loaded) return;
        UI.Win32.ForceForeground(window.Handle());
        await Task.Delay(200);
        var front = UI.Win32.GetForegroundWindow();
        var leader = loaded.Leaders[0];
        App.Guide.Show(leader, [leader.Title], loaded);
        await Task.Delay(300);
        var guide = App.Guide.Handle();
        Check("the Guide shows", App.Guide.IsShown && UI.Win32.IsWindowVisible(guide));
        Check("showing the Guide never changes the foreground window", UI.Win32.GetForegroundWindow() == front && front != guide);
        var ex = Guide.GuideWindow.GetWindowLongPtrW(guide, -20);
        Check("the Guide is never activated, topmost, and not in the taskbar",
            (ex & Guide.GuideWindow.WS_EX_NOACTIVATE) != 0 && (ex & Guide.GuideWindow.WS_EX_TOOLWINDOW) != 0
            && (ex & Guide.GuideWindow.WS_EX_TOPMOST) != 0 && (ex & Guide.GuideWindow.WS_EX_APPWINDOW) == 0);
        Check("the Guide is not in Alt+Tab", !App.Guide.AppWindow.IsShownInSwitchers);
        var texts = Find<TextBlock>(App.Guide.View.RootElement).Select(t => t.Text).ToList();
        Check("the Guide shows the path and one row per child, in file order",
            texts.Contains("Commands") && texts.IndexOf("File Explorer") < texts.IndexOf("Terminal") && texts.IndexOf("File Explorer") >= 0,
            string.Join(", ", texts));
        Check("the footer shows Esc Cancel and Backspace Back",
            texts.Contains("Esc") && texts.Contains("Cancel") && texts.Contains("Backspace") && texts.Contains("Back"));
        App.Guide.Hide();
        await Task.Delay(100);
        Check("the Guide hides", !App.Guide.IsShown);

        var rows = Enumerable.Range(0, 30).Select(i => new Guide.GuideRow(KeyName.A, $"Row {i}", false)).ToList();
        Check("up to 12 rows make one column, more two, more than 24 three",
            Guide.GuideView.Columns(rows[..12]).Count == 1 && Guide.GuideView.Columns(rows[..13]).Count == 2
            && Guide.GuideView.Columns(rows[..24]).Count == 2 && Guide.GuideView.Columns(rows[..25]).Count == 3
            && Guide.GuideView.Columns(rows).Count == 3);
    }
}
