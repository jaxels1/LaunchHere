# LaunchHere 1.2

Open app windows on the display whose taskbar you click. A portable Windows utility with a settings GUI and system tray controls.

Meet your tiny window concierge: a smiling monitor mascot, a do-not-disturb club for app exceptions, tiny snack breaks when paused, and an optional **Boop the concierge** button in About & help. Click the header mascot for a bonus boop. These touches do not change routing or show pop-up notifications.

## Upgrading from Taskbar Monitor

Quit the old app before opening LaunchHere. Existing preferences in `%LOCALAPPDATA%\TaskbarMonitor\settings.json` are read automatically if LaunchHere has no settings file yet. Changing a setting saves them under `%LOCALAPPDATA%\LaunchHere\settings.json`; the old file remains intact.

If the old app started with Windows, enable **Start with Windows** in LaunchHere to replace its recognized startup entry. This change occurs only when you use the startup control. Windows instance coordination keeps its original internal identifiers so old and new copies do not both route windows at once.

## Run

1. Extract the app download to a permanent folder. Keep **LaunchHere.exe** and **LaunchHere.exe.config** together.
2. Double-click **LaunchHere.exe**. It starts quietly in the system tray, with routing enabled by default.
3. **Single-click its tray icon** to open the settings window. It may be under the hidden-icons arrow.
4. Show the taskbar on all displays in Windows taskbar settings, then click an app on your preferred display's taskbar.

Closing or minimizing the settings window keeps the app running in the tray. **Hide to tray** does the same. Use **Quit app**, or **Exit** in the tray menu, to stop the app. Press Escape to hide settings.

Only one instance runs per Windows session. Launching the executable again opens the existing settings window. `LaunchHere.exe --settings` opens settings on the first launch too.

Requires Windows 10/11 x64 and .NET Framework 4.8. The standard Windows taskbar is supported; replacement taskbars are not supported. This build is unsigned.

## Settings

- **Enable LaunchHere:** pause/resume routing. The tray menu and GUI stay in sync. A paused state survives a restart.
- **Start with Windows:** start quietly in the tray when your Windows account signs in. Off by default. Keep the app in a permanent location before enabling this.
- **Thumbnail previews:** include windows selected through taskbar thumbnail previews. On by default.
- **Retry movement:** make up to two extra attempts when an app resets its position shortly after activation. On by default.
- **Launch detection timeout:** 3–30 seconds; defaults to 12. A shorter wait reduces mistaken moves from unrelated activations.
- **App exceptions:** enter a process name, such as `game.exe`, or browse to an executable. Those processes are ignored. Matching is exact and case-insensitive; paths and wildcards are not supported.
- **Connected displays:** an arrangement diagram updates while settings are open.
- **About & help:** usage information, privacy details, and a shortcut to Windows taskbar settings.

Changes take effect immediately and save automatically to `%LOCALAPPDATA%\LaunchHere\settings.json`. If preferences cannot be saved, the footer reports that they only apply for this session; hover over it to read the complete message. Invalid settings files fall back to defaults and show a notice.

Startup uses a per-user `LaunchHere` entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turn startup off before moving or deleting the app. If Windows restricts access to that key, the startup option is disabled and manual launching still works.

## Behavior and limits

The app observes genuine left clicks on Windows taskbars and enabled thumbnail previews, then moves the next eligible foreground/restored window within the configured timeout. It preserves maximized state and relative position where practical, shrinking windows that exceed the destination's available area. Clicking an already active app follows Windows' normal minimize behavior. Keyboard navigation does not trigger routing.

Windows does not provide a universal link between a taskbar click and the window it launches. This utility infers that link from window activation. An unrelated app activating soon after a taskbar click can sometimes be moved. Clicks outside the taskbar, opening settings, pausing, or a timeout cancel pending routing. If an excluded app activates, pending routing is canceled.

Apps can briefly appear on their previous display before movement. Maximized windows briefly restore during movement. Very slow launches, splash screens taking focus, exclusive fullscreen games, and apps that continually enforce their own position may need manual movement. Administrator apps may reject movement from a normally running utility; moving those windows may require launching this utility as administrator too.

No accounts, analytics, or network requests. Preferences stay on this PC. The app observes clicks and window activation without recording click history or keystrokes. It does not change the primary display or Windows taskbar settings.

## Source and build

The separate source package contains:

- `LaunchHere.cs`: tray lifecycle, single-instance signaling, and native routing.
- `SettingsWindow.cs`: settings GUI, monitor diagram, and accessibility support.
- `Settings.cs`: settings validation and atomic JSON persistence.
- `AssemblyInfo.cs`: product/version metadata and the .NET Framework target.
- `LaunchHere.ico`, `app.manifest`, and `LaunchHere.exe.config`: branding, normal-user permissions, compatibility, and DPI configuration.
- `build.ps1`: an offline build using the C# compiler bundled with Windows .NET Framework.
- `LaunchHere.csproj`: a Visual Studio/MSBuild project targeting .NET Framework 4.8.
- `Tests`: GUI/settings and native movement verification, with a test runner.

Open PowerShell in the source folder and run `./build.ps1`. Build output is `LaunchHere.exe` in that folder; keep its `.exe.config` beside it. The Visual Studio project requires the .NET Framework 4.8 developer targeting pack.

Close the normal utility, then run `./Tests/test.ps1` to execute the test suite. It briefly creates temporary test windows and saves logs/renders to a temporary folder. With only one exposed display, native cross-monitor checks report that they were skipped.

## Verified for this build

- Compilation, native placement structure size, and geometry for negative monitor coordinates, oversized/off-screen windows, and zero dimensions.
- Registration and cleanup of the mouse, foreground, and restoration hooks.
- Settings round-trip, default handling, corrupt-file recovery, bounded timeouts, exception validation/deduplication, and GUI controls updating saved preferences.
- Tray-only launch, single-click opening, instance signaling, close/minimize hiding, reopening the same window, and cleanup when quitting.
- All settings pages fit horizontally at 100% and 125% display scaling, including movement between those displays.
- Normal, maximized, and restored test windows move correctly in both directions across two connected monitors.

## Before public release

This is a portable build, with no installer, code signature, or automatic updater. Review branding and your distribution license, sign the release using your publisher certificate if desired, and verify the full taskbar-launch flow on the Windows versions and taskbar configurations you intend to support. Startup registry writes could not be exercised in the restricted development environment; test enabling/disabling startup and signing back into Windows on an ordinary account.

Manual routing checks still required: launch a new app from each monitor's taskbar; restore a minimized app; select a taskbar thumbnail; try a maximized window and an excluded app; pause/resume; disconnect a display. The automated native movement checks verify movement rather than real taskbar launching.

Native API references: [SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw), [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [SetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos), and [Windows Forms DPI configuration](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms?view=netframeworkdesktop-4.8).
