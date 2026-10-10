using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
class TestForm : Form { protected override bool ShowWithoutActivation { get { return true; } } }
class WindowChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WindowRect { public int left, top, right, bottom; }
    static readonly BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Pump() { for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(20); } }
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static Rectangle Bounds(IntPtr hwnd)
    {
        WindowRect r;
        if (!GetWindowRect(hwnd, out r)) throw new Exception("Cannot read fixture bounds");
        return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
    }
    static void RoutingChecks(Type type, MethodInfo move)
    {
        // A separate process keeps fixture windows eligible for the real routing
        // code, which deliberately ignores windows owned by LaunchHere itself.
        var info = new ProcessStartInfo(Application.ExecutablePath, "--fixture") {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using (var fixture = Process.Start(info))
        {
            object app = null;
            try
            {
                IntPtr active = new IntPtr(Int64.Parse(fixture.StandardOutput.ReadLine()));
                IntPtr background = new IntPtr(Int64.Parse(fixture.StandardOutput.ReadLine()));
                string settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LaunchHere-routing-" + Guid.NewGuid().ToString("N") + ".json");
                app = Activator.CreateInstance(type, new object[] { false, null, settingsPath });
                var arm = type.GetMethod("BeginRouting", Instance);
                var cancel = type.GetMethod("Cancel", Instance);
                var onEvent = type.GetMethod("OnWindowEvent", Instance);
                var tick = type.GetMethod("Tick", Instance);
                var pending = type.GetField("targetDevice", Instance);
                var candidate = type.GetField("candidate", Instance);
                foreach (var target in Screen.AllScreens)
                {
                    Screen other = Screen.AllScreens[0];
                    foreach (var screen in Screen.AllScreens) if (screen.DeviceName != target.DeviceName) { other = screen; break; }
                    ShowWindow(active, 9); ShowWindow(background, 9); Pump();
                    move.Invoke(null, new object[] { active, target });
                    move.Invoke(null, new object[] { background, other }); Pump();
                    Rectangle original = Bounds(background);
                    Action armClick = delegate { arm.Invoke(app, new object[] { target.DeviceName, unchecked((uint)Environment.TickCount), active }); };
                    Action<uint, IntPtr> sendEvent = delegate(uint id, IntPtr hwnd) {
                        onEvent.Invoke(app, new object[] { IntPtr.Zero, id, hwnd, 0, 0, (uint)0, unchecked((uint)Environment.TickCount) });
                    };

                    armClick();
                    ShowWindow(active, 6);
                    sendEvent(3, background);
                    Check(pending.GetValue(app) == null, "minimized active window cancels routing before foreground event on " + target.DeviceName);
                    Pump();
                    Check(Bounds(background) == original, "revealed background window stays put on " + target.DeviceName);

                    ShowWindow(active, 9); Pump();
                    armClick();
                    sendEvent(3, background);
                    Check((IntPtr)candidate.GetValue(app) == background, "foreground event reached candidate selection");
                    sendEvent(0x16, active);
                    Check(pending.GetValue(app) == null, "late minimize-start cancels queued candidate on " + target.DeviceName);

                    armClick();
                    sendEvent(3, background);
                    ShowWindow(active, 6);
                    tick.Invoke(app, new object[] { null, EventArgs.Empty });
                    Check(pending.GetValue(app) == null, "timer rejects routing after active window minimizes");
                    Pump();
                    Check(Bounds(background) == original, "background bounds unchanged after all minimize paths");

                    // Restoring the clicked app must still route while the app
                    // that was foreground before the click remains unminimized.
                    arm.Invoke(app, new object[] { other.DeviceName, unchecked((uint)Environment.TickCount), background });
                    ShowWindow(active, 9);
                    sendEvent(0x17, active);
                    Check((IntPtr)candidate.GetValue(app) == active, "restored clicked app is still selected on " + other.DeviceName);
                    cancel.Invoke(app, null);
                }
            }
            finally
            {
                if (app != null) ((IDisposable)app).Dispose();
                if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(); }
            }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        if (args.Length > 0 && args[0] == "--fixture")
        {
            using (var active = new TestForm())
            using (var background = new TestForm())
            {
                active.Text = "LaunchHere clicked app test";
                background.Text = "LaunchHere background app test";
                active.Show(); background.Show();
                Console.WriteLine(active.Handle.ToInt64());
                Console.WriteLine(background.Handle.ToInt64()); Console.Out.Flush();
                Application.Run();
            }
            return 0;
        }
        var assembly = Assembly.LoadFrom(args[0]);
        var type = assembly.GetType("LaunchHere.TrayApp");
        var move = type.GetMethod("Move", BindingFlags.NonPublic | BindingFlags.Static);
        Console.WriteLine("Connected monitors: " + Screen.AllScreens.Length);
        foreach (var screen in Screen.AllScreens) Console.WriteLine(screen.DeviceName + " " + screen.WorkingArea);
        if (Screen.AllScreens.Length < 2) { Console.WriteLine("Cross-monitor native movement unavailable: only one display exposed to test session."); return 0; }
        foreach (var source in Screen.AllScreens)
        foreach (var target in Screen.AllScreens)
        {
            if (target.DeviceName == source.DeviceName) continue;
            using (var form = new TestForm())
            {
                form.Text = "LaunchHere temporary movement test";
                form.StartPosition = FormStartPosition.Manual;
                form.Bounds = new Rectangle(source.WorkingArea.Left + 80, source.WorkingArea.Top + 80, 640, 480);
                form.Show(); Pump();
                move.Invoke(null, new object[] { form.Handle, target }); Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName) { Console.WriteLine("FAIL normal window move"); return 1; }
                move.Invoke(null, new object[] { form.Handle, source }); Pump();
                form.WindowState = FormWindowState.Maximized; Pump();
                move.Invoke(null, new object[] { form.Handle, target }); Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName || form.WindowState != FormWindowState.Maximized) { Console.WriteLine("FAIL maximized window move"); return 2; }
                form.WindowState = FormWindowState.Normal; Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName) { Console.WriteLine("FAIL restored location"); return 3; }
                Console.WriteLine("PASS normal, maximized, restored: " + target.DeviceName);
            }
        }
        RoutingChecks(type, move);
        return 0;
    }
}
