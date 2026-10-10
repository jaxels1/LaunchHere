# LaunchHere

LaunchHere makes apps open on the monitor where you click their taskbar icon, instead of opening on your main monitor.

It runs in the system tray and has a small settings window. There's also a tiny window concierge. You can boop it.

## Download

[Download LaunchHere for Windows](https://github.com/jaxels1/LaunchHere/releases/latest)

Extract the ZIP, then run LaunchHere.exe.

## How to use it

1. Download and extract the Windows app ZIP.
2. Keep `LaunchHere.exe` and `LaunchHere.exe.config` in the same folder.
3. Run `LaunchHere.exe`.
4. In Windows taskbar settings, turn on showing the taskbar on all displays.
5. Click an app's taskbar icon on the monitor where you want it to open.

Click the LaunchHere tray icon to open settings. If you don't see it, check the hidden-icons arrow beside the clock.

Closing the settings window keeps LaunchHere running. Use **Quit app** to stop it.

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

To run the tests, close LaunchHere first, then run:

```powershell
./Tests/test.ps1
```
