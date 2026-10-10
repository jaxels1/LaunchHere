# LaunchHere

LaunchHere makes apps open on the monitor where you click their taskbar icon, instead of opening on your main monitor.

It runs in the system tray and has a small settings window. There's also a tiny window concierge. You can boop it.

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

You can pause LaunchHere, start it with Windows, adjust how long it waits for an app to open, and choose apps it should leave alone. Your settings save automatically.

## Requirements

Windows 10 or 11, 64-bit, with .NET Framework 4.8.

## Things to know

Apps might briefly appear on their old monitor before moving. Some games, slow launches, and apps running as administrator may need to be moved manually. Occasionally, an unrelated app opening just after a taskbar click can get moved too.

LaunchHere doesn't collect data or connect to the internet.

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
