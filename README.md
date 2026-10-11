# LaunchHere

LaunchHere makes apps open on the monitor where you click their taskbar icon, instead of opening on your main monitor.

It runs in the system tray, with a glass-style settings window and app exceptions.

## Download

[Download LaunchHere for Windows](https://github.com/jaxels1/LaunchHere/releases/latest)

On the release page, download the **Setup-x64.exe** file under **Assets** for the installer, or the Windows app ZIP for the portable version.

## Windows SmartScreen

LaunchHere is currently unsigned, so Windows may show **"Windows protected your PC"** when you run the installer or app.

If you downloaded it from [this repository's releases](https://github.com/jaxels1/LaunchHere/releases/latest) and trust the app:

1. Click **More info**.
2. Click **Run anyway** to continue.

You don't need to turn off SmartScreen or your antivirus. If **Run anyway** isn't available, your Windows security settings or organization may block it.

## How to use it

1. Run the installer, then open LaunchHere from the Start menu.
2. In Windows taskbar settings, turn on showing the taskbar on all displays.
3. Click an app's taskbar icon on the monitor where you want it to open.

For the portable version, extract the ZIP and run `LaunchHere.exe`. Keep `LaunchHere.exe` and `LaunchHere.exe.config` in the same folder.

Click the LaunchHere tray icon to open settings. If you don't see it, check the hidden-icons arrow beside the clock.

Closing the settings window keeps LaunchHere running. Use **Quit app** to stop it.

To update or uninstall, quit LaunchHere first. Run a newer installer to update, or remove LaunchHere through Windows **Settings > Apps**. Your preferences are kept if you reinstall.

## Settings

You can pause LaunchHere, start it with Windows, adjust how long it waits for an app to open, and choose apps it should leave alone. **Position new windows early** is on by default: LaunchHere tries to place new windows while they are still hidden, then checks their position when they appear. Turn it off if an app behaves unexpectedly. Your settings save automatically. Use **Identify displays** to see the display numbers, or export and import a local settings backup.

## Updates and privacy

Press **Check for updates** on the **Updates & privacy** page to check the latest GitHub release. If a newer version is available, LaunchHere downloads it, verifies its checksum, applies it and reopens. You can cancel during the download.

Installed copies update through the installer. Portable copies update in their existing folder. Your preferences are kept.

**Internet is used only when you press Check for updates.** There are no background checks, accounts or analytics. That action connects to GitHub and its download servers, which receive your IP address and the app version. Your settings, app exceptions and window activity stay on your PC. LaunchHere does not record click history or keystrokes.

If Windows security or a work/school policy blocks an unsigned update, it may need to be installed manually. LaunchHere does not disable security settings.

## Requirements

Windows 10 or 11, 64-bit, with .NET Framework 4.8.

## Things to know

Early positioning reduces the flash on the old monitor, but it cannot guarantee that every app appears directly on the chosen display. Windows sends window notifications asynchronously, and some apps set their own position afterward. Windows that start maximized use the faster after-show path and can still briefly appear on the old monitor. Some games, slow launches, and apps running as administrator may need to be moved manually. Occasionally, an unrelated app opening just after a taskbar click can get moved too.

## Building from source

Open PowerShell in the source folder and run:

```powershell
./build.ps1
```

You can also open `LaunchHere.csproj` in Visual Studio with the .NET Framework 4.8 developer targeting pack installed.

To build the installer, install [Inno Setup 7](https://jrsoftware.org/isdl.php), then run:

```powershell
./build-installer.ps1
```

If the compiler isn't found, pass `-IsccPath` with the full path to `ISCC.exe`. The installer is created beside the source folder.

To run the tests, close LaunchHere first, then run:

```powershell
./Tests/test.ps1
```

Installer and update handoff checks are in `Tests/InstallerChecks.ps1` and `Tests/UpdateHandoffChecks.ps1`. Pass `-IsccPath` with the path to `ISCC.exe`; they create and uninstall isolated test copies.

## Publishing a release

Run `./package.ps1 -IsccPath "C:\path\to\ISCC.exe"` to build the installer, portable ZIP and source ZIP. Upload the source files to GitHub, then create a release with the tag `v2.1` and attach the generated installer and portable ZIP.

Keep the generated filenames: `LaunchHere-2.1-Setup-x64.exe` and `LaunchHere-2.1-Windows-x64.zip` (using the new version number each release). The updater needs those assets and the SHA-256 digest GitHub supplies. It ignores drafts, prereleases and older versions.
