using System.Runtime.InteropServices;

namespace UnnamedManager.UI;

/// The error sound (S-033): Windows' "Default Beep", from Settings › System › Sound,
/// so it follows the sound scheme and the volume the person set.
public static partial class Sound
{
    /// How many times the beep played, for the self-test.
    public static int Played { get; private set; }

    public static void Beep()
    {
        Played++;
        MessageBeep(MB_OK);
    }

    private const uint MB_OK = 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);
}
