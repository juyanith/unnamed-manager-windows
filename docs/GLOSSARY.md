# Glossary (Windows)

The shared glossary, [../spec/GLOSSARY.md](../spec/GLOSSARY.md), names every idea on both platforms. Use its terms. This file adds only what is Windows' own, and how the shared terms look on screen here.

**On screen, Windows uses sentence case.** The shared glossary writes names in Title Case ("Reload Config", "Use Application"); the Windows UI writes them "Reload config", "Use application". Names of things the user made (a menu called "Commands") keep their own case. Key names follow Windows: Esc, Backspace, Ctrl, Alt, Shift, Win.

| Term | Means | Not |
| --- | --- | --- |
| Tray icon | The app's icon in the notification area of the taskbar. Its tooltip says the state: "Unnamed Manager", "Unnamed Manager, off", "Unnamed Manager, config error". | menu bar icon, system tray icon, status icon |
| Tray menu | The menu that opens from the tray icon, with a left or right click. | context menu, status menu, popup |
| Exit | Stop the app and its keyboard capture. Windows' word for the Mac's Quit. | Quit, Close, Stop |
| Open file location | Open File Explorer on the config file's folder with the file selected. Windows' words for the Mac's Show in Finder. | Show in File Explorer, Reveal, Show in Finder |
| Open log folder | Open File Explorer on `%LOCALAPPDATA%\UnnamedManager\logs`. | Show logs, Logs |
| Run at startup | Start the app when you sign in to Windows (Settings › Startup). Windows' words for the Mac's Open at Login. | Open at Login, Launch at login, Autostart |
| Running as administrator | An app started with "Run as administrator". Unnamed Manager can't change its windows or send it shortcuts. | elevated, admin mode, privileged |
| Office shortcuts | The chords Windows gives to Office: Hyper alone and Hyper with W, T, X, D, O, P, L, N, Y and others. "Turn off Office shortcuts" frees them for bindings. | Office key, Office hotkeys |
| Screen (for window actions) | The monitor the window is mostly on, without the taskbar ("work area" in the code, "usable area" in the spec). | work area, desktop, display |
