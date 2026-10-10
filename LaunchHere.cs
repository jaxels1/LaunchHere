using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LaunchHere
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--self-test") >= 0) return Geometry.Tests();
            bool created;
            // Keep these identities stable so older copies cannot route alongside us.
            using (var mutex = new Mutex(true, "Local\\TaskbarMonitor.Utility", out created))
            using (var openRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TaskbarMonitor.OpenSettings"))
            {
                if (!created)
                {
                    if (Array.IndexOf(args, "--verify-hooks") >= 0) return 2;
                    openRequest.Set();
                    return 0;
                }
                try
                {
                    using (var app = new TrayApp(Array.IndexOf(args, "--verify-hooks") >= 0, openRequest))
                    {
                        if (Array.IndexOf(args, "--settings") >= 0) app.ShowSettings();
                        Application.Run(app);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    if (Array.IndexOf(args, "--verify-hooks") >= 0)
                    {
                        Console.Error.WriteLine(ex.ToString());
                        return 1;
                    }
                    MessageBox.Show(ex.Message, "LaunchHere", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Native.MouseProc mouseProc;
        private readonly Native.EventProc eventProc;
        private IntPtr mouseHook, foregroundHook, restoreHook;
        private readonly ToolStripMenuItem enabledItem, startupItem;
        private readonly SettingsStore store;
        private readonly EventWaitHandle openRequest;
        private readonly Icon appIcon;
        private SettingsWindow window;
        internal AppSettings Settings { get; private set; }
        internal string SaveNotice { get; private set; }
        internal bool StartupAvailable { get { return startupItem.Enabled; } }
        internal bool StartupEnabled { get { return startupItem.Checked; } }
        internal bool LegacyStartupPresent { get; private set; }
        internal Icon AppIcon { get { return appIcon; } }
        internal event EventHandler StateChanged;
        private string targetDevice;
        private uint clickTime;
        private int generation, candidateGeneration, movePass;
        private IntPtr candidate, clickForeground;
        private long moveAt;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly bool verify;
        private bool disposed;
        private const string StartupKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        public TrayApp(bool verifyHooks, EventWaitHandle openRequest, string settingsPath = null)
        {
            verify = verifyHooks;
            this.openRequest = openRequest;
            store = new SettingsStore(settingsPath ?? SettingsStore.DefaultPath);
            string loadNotice = null;
            Settings = verify && settingsPath == null ? new AppSettings() : store.Load(out loadNotice);
            SaveNotice = loadNotice ?? "Tiny window concierge: on duty. Settings save automatically.";
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
            mouseProc = OnMouse;
            eventProc = OnWindowEvent;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open settings", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            enabledItem = new ToolStripMenuItem("Enabled", null, delegate { ChangeSettings(delegate(AppSettings s) { s.Enabled = !s.Enabled; }); });
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            menu.Items.Add(enabledItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("About / help", null, delegate { ShowSettings(); window.ShowAbout(); });
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = appIcon, Text = "LaunchHere", ContextMenuStrip = menu, Visible = !verify };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            try
            {
                mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
                foregroundHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, eventProc, 0, 0, 2);
                restoreHook = Native.SetWinEventHook(0x16, 0x17, IntPtr.Zero, eventProc, 0, 0, 2);
                if (mouseHook == IntPtr.Zero || foregroundHook == IntPtr.Zero || restoreHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register the mouse/window hooks.");
                timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += Tick;
                timer.Start();
                UpdateStatus();
            }
            catch { Dispose(); throw; }
        }

        internal SettingsWindow ShowSettings()
        {
            Cancel();
            UpdateStatus();
            if (window == null || window.IsDisposed) window = new SettingsWindow(this);
            if (!window.Visible)
            {
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                window.Location = new Point(area.Left + Math.Max(0, (area.Width - window.Width) / 2), area.Top + Math.Max(0, (area.Height - window.Height) / 2));
                window.Show();
            }
            window.WindowState = FormWindowState.Normal;
            window.Activate();
            return window;
        }

        internal void ChangeSettings(Action<AppSettings> change)
        {
            change(Settings);
            Settings.Normalize();
            Cancel();
            try { store.Save(Settings); SaveNotice = Settings.Enabled ? "Saved. Tiny window concierge: on duty." : "Saved. Paused for a tiny snack break."; }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException)) throw;
                SaveNotice = "Changes work for this session, but could not be saved: " + ex.Message;
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            enabledItem.Checked = Settings.Enabled;
            tray.Text = Settings.Enabled ? "LaunchHere - enabled, window concierge on duty" : "LaunchHere - paused, tiny snack break";
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
                {
                    startupItem.Checked = key != null && String.Equals(key.GetValue("LaunchHere") as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
                    LegacyStartupPresent = key != null && IsLegacyStartup(key.GetValue("TaskbarMonitor") as string);
                }
            }
            catch (System.Security.SecurityException) { startupItem.Enabled = false; }
            catch (UnauthorizedAccessException) { startupItem.Enabled = false; }
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }
        private static string StartupCommand { get { return "\"" + Application.ExecutablePath + "\""; } }
        internal static bool IsLegacyStartup(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return false;
            try { return String.Equals(Path.GetFileName(command.Trim().Trim('"')), "TaskbarMonitor.exe", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        private void ToggleStartup()
        {
            SetStartup(!StartupEnabled);
        }

        internal void SetStartup(bool value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(StartupKey))
                {
                    if (!value) key.DeleteValue("LaunchHere", false);
                    else key.SetValue("LaunchHere", StartupCommand);
                    // Replace only the old registration generated by this app.
                    if (IsLegacyStartup(key.GetValue("TaskbarMonitor") as string)) key.DeleteValue("TaskbarMonitor", false);
                }
                SaveNotice = value ? "Startup enabled. The concierge clocks in when you sign in." : "Startup disabled. No early shift for the concierge.";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                SaveNotice = "Could not change startup: " + ex.Message;
                UpdateStatus();
            }
        }

        private void Cancel() { targetDevice = null; candidate = IntPtr.Zero; clickForeground = IntPtr.Zero; generation++; }
        internal void BeginRouting(string device, uint time, IntPtr foreground)
        {
            Cancel();
            targetDevice = device;
            clickTime = time;
            // Clicking the active taskbar button minimizes that window. Remember
            // it so the window revealed underneath cannot become our candidate.
            clickForeground = foreground;
        }
        private bool ClickMinimizedForeground()
        {
            return clickForeground != IntPtr.Zero &&
                (!Native.IsWindow(clickForeground) || Native.IsIconic(clickForeground));
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            // Keep the global hook fast: no disk access, UI Automation, or process queries.
            try
            {
                if (code >= 0 && (message.ToInt64() == 0x201 || message.ToInt64() == 0x204))
                {
                    var mouse = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                    if ((mouse.flags & 1) == 0 && !verify)
                    {
                        IntPtr root = Native.GetAncestor(Native.WindowFromPoint(mouse.point), 2);
                        string cls = Native.ClassName(root);
                        if (Settings.Enabled && message.ToInt64() == 0x201 && IsTaskbar(cls) && (Settings.ThumbnailPreviews || cls != "TaskListThumbnailWnd"))
                        {
                            BeginRouting(Screen.FromPoint(new Point(mouse.point.x, mouse.point.y)).DeviceName,
                                mouse.time, Native.GetForegroundWindow());
                        }
                        else Cancel();
                    }
                }
            }
            catch { Cancel(); }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        private static bool IsTaskbar(string cls)
        {
            return cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "TaskListThumbnailWnd";
        }

        private void OnWindowEvent(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint threadId, uint time)
        {
            try
            {
                if (verify || !Settings.Enabled || targetDevice == null || hwnd == IntPtr.Zero) return;
                // MINIMIZESTART can arrive before or after the foreground event.
                // Cancel either way, before the delayed movement can run.
                if (eventId == 0x16)
                {
                    if (hwnd == clickForeground) Cancel();
                    return;
                }
                if (ClickMinimizedForeground()) { Cancel(); return; }
                // Unsigned subtraction handles the system tick counter wrapping after 49 days.
                uint timeout = (uint)Settings.LaunchTimeoutSeconds * 1000;
                if (unchecked(time - clickTime) > timeout || unchecked((uint)Environment.TickCount - clickTime) > timeout) return;
                if (!Eligible(hwnd)) return;
                if (candidate != IntPtr.Zero) return;
                candidate = hwnd;
                candidateGeneration = generation;
                movePass = 0;
                moveAt = clock.ElapsedMilliseconds + 150;
            }
            catch { Cancel(); }
        }

        private bool Eligible(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetAncestor(hwnd, 2) != hwnd) return false;
            long ex = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
            if ((ex & 0x80) != 0 || (ex & 0x08000000) != 0) return false;
            string cls = Native.ClassName(hwnd);
            if (IsTaskbar(cls) || cls == "Progman" || cls == "WorkerW" || cls == "#32768" || cls == "Windows.UI.Core.CoreWindow" && Native.GetWindowTextLength(hwnd) == 0) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    string name = process.ProcessName;
                    if (Settings.IsExcluded(name)) { Cancel(); return false; }
                    if (name == "StartMenuExperienceHost" || name == "ShellExperienceHost" || name == "SearchHost" || name == "SearchApp" || name == "TextInputHost" || name == "Widgets") return false;
                }
            }
            catch (ArgumentException) { return false; }
            return Native.GetWindowTextLength(hwnd) > 0;
        }

        private void Tick(object sender, EventArgs e)
        {
            if (openRequest != null && openRequest.WaitOne(0)) ShowSettings();
            if (verify) { if (clock.ElapsedMilliseconds > 500) ExitThread(); return; }
            if (targetDevice == null) return;
            if (ClickMinimizedForeground()) { Cancel(); return; }
            if (unchecked((uint)Environment.TickCount - clickTime) > (uint)Settings.LaunchTimeoutSeconds * 1000) { Cancel(); return; }
            if (candidate == IntPtr.Zero || clock.ElapsedMilliseconds < moveAt) return;
            if (candidateGeneration != generation || Native.GetForegroundWindow() != candidate) { Cancel(); return; }
            Screen target = null;
            foreach (var screen in Screen.AllScreens) if (screen.DeviceName == targetDevice) target = screen;
            if (target == null) { Cancel(); return; }
            try { Move(candidate, target); }
            catch { Cancel(); return; }
            // Applications sometimes apply their saved position shortly after activation.
            // Stop immediately if the user clicks elsewhere or changes focus.
            if (++movePass >= (Settings.RetryMovement ? 3 : 1)) Cancel();
            else moveAt = clock.ElapsedMilliseconds + (movePass == 1 ? 350 : 600);
        }

        private static void Move(IntPtr hwnd, Screen target)
        {
            if (!Native.IsWindow(hwnd) || Native.IsIconic(hwnd)) return;
            Screen source = Screen.FromHandle(hwnd);
            if (source.DeviceName == target.DeviceName) return;
            var placement = new Native.Placement { length = Marshal.SizeOf(typeof(Native.Placement)) };
            if (!Native.GetWindowPlacement(hwnd, ref placement)) return;
            Rectangle original;
            if (Native.IsZoomed(hwnd))
            {
                // WINDOWPLACEMENT uses workspace coordinates for regular top-level windows.
                original = placement.normal.ToRectangle();
                original.Offset(source.WorkingArea.Left - source.Bounds.Left, source.WorkingArea.Top - source.Bounds.Top);
                Rectangle moved = Geometry.Map(original, source.WorkingArea, target.WorkingArea);
                moved.Offset(target.Bounds.Left - target.WorkingArea.Left, target.Bounds.Top - target.WorkingArea.Top);
                placement.normal = Native.Rect.FromRectangle(moved);
                placement.flags = 4; // Async: do not block on another application's message loop.
                // A maximized HWND can stay on its old monitor if only its restore
                // rectangle changes. Restore at the new position before maximizing.
                placement.showCmd = 4;
                Native.SetWindowPlacement(hwnd, ref placement);
                placement.showCmd = 3;
                Native.SetWindowPlacement(hwnd, ref placement);
            }
            else
            {
                Native.Rect rect;
                if (!Native.GetWindowRect(hwnd, out rect)) return;
                original = rect.ToRectangle();
                Rectangle moved = Geometry.Map(original, source.WorkingArea, target.WorkingArea);
                Native.SetWindowPos(hwnd, IntPtr.Zero, moved.X, moved.Y, moved.Width, moved.Height, 0x0014 | 0x4000);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (foregroundHook != IntPtr.Zero) { Native.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            if (restoreHook != IntPtr.Zero) { Native.UnhookWinEvent(restoreHook); restoreHook = IntPtr.Zero; }
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                if (window != null) window.Dispose();
                if (tray != null) { tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Geometry
    {
        internal static Rectangle Map(Rectangle window, Rectangle source, Rectangle target)
        {
            int w = Math.Min(Math.Max(window.Width, 1), target.Width);
            int h = Math.Min(Math.Max(window.Height, 1), target.Height);
            double fx = source.Width > window.Width ? (double)(window.Left - source.Left) / (source.Width - window.Width) : 0.5;
            double fy = source.Height > window.Height ? (double)(window.Top - source.Top) / (source.Height - window.Height) : 0.5;
            int x = target.Left + (int)Math.Round(Math.Max(0, Math.Min(1, fx)) * (target.Width - w));
            int y = target.Top + (int)Math.Round(Math.Max(0, Math.Min(1, fy)) * (target.Height - h));
            return new Rectangle(x, y, w, h);
        }

        internal static int Tests()
        {
            var source = new Rectangle(0, 0, 1920, 1040);
            var left = new Rectangle(-1280, 40, 1280, 984);
            var centered = Map(new Rectangle(560, 220, 800, 600), source, left);
            if (centered != new Rectangle(-1040, 232, 800, 600)) return 1;
            var huge = Map(new Rectangle(-100, -100, 3000, 2000), source, left);
            if (huge != left) return 2;
            var above = new Rectangle(100, -1440, 2560, 1400);
            if (!above.Contains(Map(new Rectangle(1900, 1000, 800, 600), source, above))) return 3;
            if (!left.Contains(Map(new Rectangle(-4000, -4000, 500, 400), source, left))) return 4;
            var zero = Map(new Rectangle(0, 0, 0, 0), source, left);
            if (zero.Width != 1 || zero.Height != 1) return 5;
            if (Marshal.SizeOf(typeof(Native.Placement)) != 44) return 6;
            return 0;
        }
    }

    internal static class Native
    {
        internal delegate IntPtr MouseProc(int code, IntPtr message, IntPtr data);
        internal delegate void EventProc(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint threadId, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point point; public uint mouseData, flags, time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int left, top, right, bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(left, top, right, bottom); }
            public static Rect FromRectangle(Rectangle r) { return new Rect { left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom }; }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Placement { public int length, flags, showCmd; public Point min, max; public Rect normal; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, MouseProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        internal static string ClassName(IntPtr hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    }
}
