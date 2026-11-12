using System.Runtime.InteropServices;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

public partial class KeyboardTrackerTests
{
    private const int LShift = 0xA0, LCtrl = 0xA2, LAlt = 0xA4, LWin = 0x5B;

    private static readonly LoadedConfig Apps = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space A", "title": "Applications" },
          { "keys": "Hyper+Space A T", "title": "Terminal", "action": "x" },
          { "keys": "Hyper+Space Left", "title": "Left", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    private static RawKey Down(KeyName key, int vk = 0) => Raw(key, vk, false);
    private static RawKey Up(KeyName key, int vk = 0) => Raw(key, vk, true);
    private static RawKey Raw(KeyName key, int vk, bool up) => new(vk, key.ScanCode() & 0xFF, key.ScanCode() > 0xFF, up);
    private static RawKey Mod(int vk, bool up = false) => new(vk, 0, false, up);

    private static void HoldHyper(KeyboardTracker t)
    {
        foreach (var vk in new[] { LCtrl, LAlt, LShift, LWin }) Assert.False(t.Process(Mod(vk)).Consume);
    }

    private static void ReleaseHyper(KeyboardTracker t)
    {
        foreach (var vk in new[] { LCtrl, LAlt, LShift, LWin }) Assert.False(t.Process(Mod(vk, up: true)).Consume);
    }

    [Fact]
    public void HyperSpaceStartsASequenceAndModifiersAlwaysPass()
    {
        var t = new KeyboardTracker(Apps);
        HoldHyper(t);
        Assert.Equal(Modifiers.Hyper, t.Held);
        var start = t.Process(Down(KeyName.Space));
        Assert.True(start.Consume);
        Assert.IsType<ShowMenu>(start.Effect);
        Assert.True(t.Process(Up(KeyName.Space)).Consume);
        ReleaseHyper(t);
        Assert.True(t.IsActive);
    }

    [Fact]
    public void AKeyHeldDownRepeatsAsConsumedKeyDowns()
    {
        var t = new KeyboardTracker(Apps);
        HoldHyper(t);
        t.Process(Down(KeyName.Space));
        for (var i = 0; i < 5; i++)
        {
            var repeat = t.Process(Down(KeyName.Space));
            Assert.True(repeat.Consume);
            Assert.Null(repeat.Effect);
        }
    }

    [Fact]
    public void TheMaskIsSentOnceWhileWinOrAltIsHeld()
    {
        var t = new KeyboardTracker(Apps);
        HoldHyper(t);
        Assert.True(t.Process(Down(KeyName.Space)).SendMask);
        Assert.False(t.Process(Down(KeyName.A)).SendMask);
        ReleaseHyper(t);
        // Win and Alt are up: nothing to mask.
        Assert.False(t.Process(Down(KeyName.T)).SendMask);
        // A key that passes through needs no mask.
        Assert.False(t.Process(Mod(LWin)).Consume);
        Assert.False(t.Process(Down(KeyName.E)).SendMask);
    }

    [Fact]
    public void EventsTheAppInjectedPassUntouched()
    {
        var t = new KeyboardTracker(Apps);
        HoldHyper(t);
        t.Process(Down(KeyName.Space));
        Assert.Equal(HookDecision.Pass, t.Process(new RawKey(0x41, KeyName.A.ScanCode(), false, false, IsOurs: true)));
        Assert.True(t.IsActive);
    }

    [Fact]
    public void ExtendedKeysMatchByTheirExtendedScanCode()
    {
        var t = new KeyboardTracker(Apps);
        HoldHyper(t);
        t.Process(Down(KeyName.Space));
        ReleaseHyper(t);
        // Numpad 4 has Left's scan code without the extended flag: it isn't Left.
        Assert.IsType<Unbound>(t.Process(new RawKey(0x64, 0x4B, false, false)).Effect);
        Assert.Equal("Left", Assert.IsType<Run>(t.Process(Down(KeyName.Left, vk: 0x25)).Effect).Binding.Title);
    }

    [Fact]
    public void KeysMatchByPositionWithAFrenchLayoutLoaded()
    {
        const string French = "0000040C";
        var before = Layouts();
        var hkl = LoadKeyboardLayoutW(French, KLF_NOTELLSHELL);
        Assert.NotEqual(0, hkl);
        try
        {
            // On AZERTY, the key where US ANSI has A types Q, and the T key is still T.
            var vkAtA = (int)MapVirtualKeyExW((uint)(KeyName.A.ScanCode()), MAPVK_VSC_TO_VK, hkl);
            Assert.Equal('Q', vkAtA);
            var t = new KeyboardTracker(Apps);
            HoldHyper(t);
            t.Process(Down(KeyName.Space, vk: 0x20));
            ReleaseHyper(t);
            Assert.Equal("Applications", Assert.IsType<ShowMenu>(t.Process(Down(KeyName.A, vk: vkAtA)).Effect).Menu.Title);
            var vkAtT = (int)MapVirtualKeyExW((uint)KeyName.T.ScanCode(), MAPVK_VSC_TO_VK, hkl);
            Assert.Equal("Terminal", Assert.IsType<Run>(t.Process(Down(KeyName.T, vk: vkAtT)).Effect).Binding.Title);
        }
        finally
        {
            if (!before.Contains(hkl)) UnloadKeyboardLayout(hkl);
        }
    }

    [Fact]
    public void ThousandEventsTakeWellUnderAMillisecondEach()
    {
        var t = new KeyboardTracker(Apps);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            t.Process(Down(KeyName.E));
            t.Process(Up(KeyName.E));
        }
        watch.Stop();
        Assert.True(watch.Elapsed.TotalMilliseconds / 2000 < 1, $"{watch.Elapsed.TotalMilliseconds} ms for 2,000 events");
    }

    private static nint[] Layouts()
    {
        var count = GetKeyboardLayoutList(0, null);
        var list = new nint[count];
        GetKeyboardLayoutList(count, list);
        return list;
    }

    private const uint KLF_NOTELLSHELL = 0x80, MAPVK_VSC_TO_VK = 1;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadKeyboardLayoutW(string id, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnloadKeyboardLayout(nint hkl);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyExW(uint code, uint mapType, nint hkl);

    [LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int count, nint[]? list);
}
