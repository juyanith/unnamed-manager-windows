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
| App theme | Light, Dark or Use system setting, for the Guide, the tray menu and every window (the config's `theme`). In Settings › Appearance. | Guide theme, appearance, mode |
| Screen (for window actions) | The monitor the window is mostly on, without the taskbar ("work area" in the code, "usable area" in the spec). | work area, desktop, display |
| Virtual desktop | Windows' word for the Mac's Space. Focus Window's Window used before and Recent Windows can go to a window on another one (spec v13, v18); every other target sees only the current one. | Space, workspace |
| Switch Desktop | The `space.switch` action (spec v17): show the desktop to the left or right, at once. The Mac's Switch Space. | Move desktop, next workspace |
| Send to Desktop | The `space.send` action (spec v20): put the window you're in on another desktop, and stay, or Go with the window. The Mac's Send to Space. | Move to desktop, throw |
| Hold to choose | The switch on a leader that makes its menus held menus (`hold`, spec v18): hold the modifiers, press the leader's key again to move the highlight, let go to choose, as Alt+Tab does. | Hold mode, sticky menu |
| Contents | A menu's choice in the Bindings window: Keys you add, or Recent windows (`list`, spec v18). | List type, generated |
| When chosen | A list's choice: Focus the window, or Swap with the window (`choose`, spec v19). | Action, mode |
| Built in | The menu under a binding's Action list with the built-in actions, and the word after one in the binding's detail ("Swap Window: Left, built in"). | Built-in actions menu, presets |
| Apps (Settings) | The Settings group that leaves an app's windows out of Focus Window, Swap Window or Arrange Windows (`windows.apps`). A checked box means the app takes part. | Excluded apps, Left out |
