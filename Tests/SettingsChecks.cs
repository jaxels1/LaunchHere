using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
namespace LaunchHere
{
    internal static class SettingsChecks
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        private static int count;
        private sealed class UpdateRequests { internal int Count, Delay = 40; }
        private sealed class UpdateHandler : HttpMessageHandler
        {
            private readonly UpdateRequests requests;
            internal UpdateHandler(UpdateRequests requests) { this.requests = requests; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                requests.Count++;
                await Task.Delay(requests.Delay, cancellation);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"v1.3.0\",\"draft\":false,\"prerelease\":false,\"assets\":[]}") };
            }
        }
        private static void WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Application.DoEvents();
            if (!condition()) throw new Exception("Timed out waiting for update GUI");
        }
        private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
        private static T Find<T>(Control parent, string accessibleName) where T : Control
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T && control.AccessibleName == accessibleName) return (T)control;
                T child = Find<T>(control, accessibleName); if (child != null) return child;
            }
            return null;
        }
        private static Button FindButton(Control parent, string text)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Button && control.Text == text) return (Button)control;
                var child = FindButton(control, text); if (child != null) return child;
            }
            return null;
        }
        private static void CheckFlows(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var f = child as FlowLayoutPanel;
                if (f != null && f.Visible && f.AutoScroll)
                {
                    Check(!f.HorizontalScroll.Visible, "page layout fits width at current DPI");
                }
                CheckFlows(child);
            }
        }
        private static void CheckNativeScrolling(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var flow = child as GlassFlow;
                if (flow != null && flow.Visible && flow.VerticalScroll.Visible)
                {
                    flow.AutoScrollPosition = Point.Empty;
                    SendMessage(flow.Handle, 0x115, new IntPtr(1), IntPtr.Zero);
                    Application.DoEvents();
                    Check(flow.AutoScrollPosition.Y < 0, "styled scrollbar retains native line scrolling");
                    flow.AutoScrollPosition = Point.Empty;
                }
                CheckNativeScrolling(child);
            }
        }
        private static void CheckClippedPainting(Control control)
        {
            using (control)
            using (var bitmap = new Bitmap(420, 160))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                control.Size = new Size(260, 60);
                control.Font = new Font("Segoe UI", 10F);
                control.Text = "Painting must stay inside this control";
                control.ForeColor = Glass.Ink;
                graphics.Clear(Color.Magenta);
                graphics.TranslateTransform(100, 80);
                graphics.SetClip(control.ClientRectangle);
                var paint = control.GetType().GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance);
                paint.Invoke(control, new object[] { new PaintEventArgs(graphics, control.ClientRectangle) });
                int changedOutside = 0, changedInside = 0;
                for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb()) continue;
                    if (new Rectangle(100, 80, 260, 60).Contains(x, y)) changedInside++; else changedOutside++;
                }
                Check(changedInside > 0 && changedOutside == 0, control.GetType().Name + " respects translated graphics and clipping");
            }
        }
        private static void CheckSurfaceContinuity(Control control, bool insideCard)
        {
            using (control)
            using (var form = new GlassForm { ClientSize = new Size(520, 350) })
            using (var card = new GlassCard { Bounds = new Rectangle(30, 80, 460, 230) })
            using (var panel = new Panel { Bounds = new Rectangle(20, 25, 400, 100), BackColor = Color.Transparent })
            using (var reference = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            using (var painted = new Bitmap(160, 38))
            {
                form.Controls.Add(card);
                (insideCard ? (Control)card : form).Controls.Add(panel);
                control.AutoSize = false; control.Bounds = new Rectangle(10, 10, 160, 38);
                control.Text = ""; panel.Controls.Add(control);
                // A sibling must never be replayed into the control's background.
                panel.Controls.Add(new Label { Text = "SIBLING", ForeColor = Color.Magenta, AutoSize = true, Location = new Point(200, 10) });
                var background = typeof(Control).GetMethod("OnPaintBackground", BindingFlags.NonPublic | BindingFlags.Instance);
                using (var graphics = Graphics.FromImage(reference))
                {
                    background.Invoke(form, new object[] { new PaintEventArgs(graphics, form.ClientRectangle) });
                    if (insideCard)
                    {
                        graphics.TranslateTransform(card.Left, card.Top);
                        graphics.SetClip(card.ClientRectangle);
                        background.Invoke(card, new object[] { new PaintEventArgs(graphics, card.ClientRectangle) });
                    }
                }
                using (var graphics = Graphics.FromImage(painted))
                {
                    graphics.SetClip(control.ClientRectangle);
                    control.GetType().GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control,
                        new object[] { new PaintEventArgs(graphics, control.ClientRectangle) });
                }
                var origin = new Point(panel.Left + control.Left, panel.Top + control.Top);
                if (insideCard) origin.Offset(card.Location);
                bool seamless = true;
                foreach (var corner in new[] { Point.Empty, new Point(159, 0), new Point(0, 37), new Point(159, 37) })
                {
                    Color expected = reference.GetPixel(origin.X + corner.X, origin.Y + corner.Y), actual = painted.GetPixel(corner.X, corner.Y);
                    seamless &= Math.Abs(expected.R - actual.R) <= 2 && Math.Abs(expected.G - actual.G) <= 2 && Math.Abs(expected.B - actual.B) <= 2;
                }
                Check(seamless, control.GetType().Name + " corners match the " + (insideCard ? "nested card" : "window") + " background");
            }
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
                CheckClippedPainting(new GlassToggle());
                CheckClippedPainting(new GlassButton());
                CheckClippedPainting(new DisplayMap());
                CheckClippedPainting(new GlassBadge());
                CheckSurfaceContinuity(new GlassButton(), false);
                CheckSurfaceContinuity(new GlassButton(), true);
                CheckSurfaceContinuity(new GlassToggle(), true);
                CheckSurfaceContinuity(new GlassBadge(), false);
                CheckSurfaceContinuity(new DisplayMap(), true);
                string path = Path.Combine(folder, "test-settings.json");
                var store = new SettingsStore(path);
                string migrationFolder = Path.Combine(folder, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(migrationFolder);
                string oldPath = Path.Combine(migrationFolder, "legacy.json"), newPath = Path.Combine(migrationFolder, "new.json");
                var oldStore = new SettingsStore(oldPath);
                oldStore.Save(new AppSettings { Enabled = false, LaunchTimeoutSeconds = 22 });
                var upgradeStore = new SettingsStore(newPath, oldPath);
                string migrationNotice;
                var upgrade = upgradeStore.Load(out migrationNotice);
                Check(!upgrade.Enabled && upgrade.LaunchTimeoutSeconds == 22 && migrationNotice.Contains("carried over"), "rename preserves legacy preferences");
                upgrade.Enabled = true; upgradeStore.Save(upgrade);
                Check(upgradeStore.Load(out migrationNotice).Enabled && !oldStore.Load(out migrationNotice).Enabled, "new preferences take priority and leave old file unchanged");
                Check(TrayApp.IsLegacyStartup("\"C:\\Apps\\TaskbarMonitor.exe\"") && !TrayApp.IsLegacyStartup("\"C:\\Apps\\Other.exe\"") && !TrayApp.IsLegacyStartup("\"C:\\Apps\\TaskbarMonitor.exe\" --other"), "startup migration recognizes only the old app registration");
                store.Save(new AppSettings { Enabled = false });
                string notice;
                Check(!store.Load(out notice).Enabled, "settings round-trip");
                File.WriteAllText(path, "{}");
                var loaded = store.Load(out notice);
                Check(loaded.Enabled && loaded.EarlyPositioning && loaded.ThumbnailPreviews && loaded.RetryMovement && loaded.LaunchTimeoutSeconds == 12, "missing JSON fields retain defaults including early positioning");
                File.WriteAllText(path, "invalid JSON");
                Check(store.Load(out notice).Enabled && notice != null, "corrupt settings recover with notice");
                store.Save(new AppSettings { Enabled = false });
                Check(AppSettings.ProcessName("C:\\game.exe") == null && AppSettings.ProcessName("*.exe") == null, "invalid exception names rejected");
                Check(AppSettings.ProcessName("Game + Tools.exe") == "Game + Tools", "valid Windows executable names supported");
                var normalize = new AppSettings { LaunchTimeoutSeconds = 100 };
                normalize.ExcludedApps.Add("GAME.exe"); normalize.ExcludedApps.Add("game"); normalize.ExcludedApps.Add("*"); normalize.Normalize();
                Check(normalize.LaunchTimeoutSeconds == 30 && normalize.ExcludedApps.Count == 1 && normalize.IsExcluded("game"), "settings bounds and exception deduplication");
                using (var request = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset))
                using (var app = new TrayApp(true, request, path))
                {
                    var timer = (Timer)typeof(TrayApp).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app); timer.Stop();
                    Check(typeof(TrayApp).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app) == null, "launch starts without a settings window");
                    var window = app.ShowSettings(); Application.DoEvents();
                    window.DpiChanged += delegate(object sender, DpiChangedEventArgs e) { Console.WriteLine("DPI transition " + e.DeviceDpiOld + " -> " + e.DeviceDpiNew); };
                    var tabs = Find<SettingsPages>(window, "Settings pages");
                    Check(window.Visible && tabs.TabCount == 3, "settings window opens with three pages");
                    Check(window.Text == "LaunchHere", "window uses LaunchHere branding");
                    var enable = Find<CheckBox>(window, "Enable monitor routing");
                    Check(!enable.Checked, "saved pause preference loads into GUI");
                    enable.Checked = true;
                    Check(app.Settings.Enabled && store.Load(out notice).Enabled, "enable control saves and updates engine");
                    var early = Find<CheckBox>(window, "Position new windows early");
                    Check(early.Checked, "early positioning enabled for existing preferences");
                    early.Checked = false;
                    Check(!app.Settings.EarlyPositioning && !store.Load(out notice).EarlyPositioning, "early positioning choice persists");
                    early.Checked = true;
                    Find<CheckBox>(window, "Include taskbar thumbnail previews").Checked = false;
                    Find<CheckBox>(window, "Retry window movement").Checked = false;
                    Find<NumericUpDown>(window, "Launch detection timeout in seconds").Value = 20;
                    loaded = store.Load(out notice);
                    Check(!loaded.ThumbnailPreviews && !loaded.RetryMovement && loaded.LaunchTimeoutSeconds == 20, "behavior controls save to disk");
                    var number = Find<NumericUpDown>(window, "Launch detection timeout in seconds");
                    number.UpButton();
                    Check(number.Value == 21 && store.Load(out notice).LaunchTimeoutSeconds == 21, "styled number spinner still increments and saves");
                    number.DownButton();
                    Check(number.Value == 20 && store.Load(out notice).LaunchTimeoutSeconds == 20, "styled number spinner still decrements and saves");
                    CheckNativeScrolling(window);
                    tabs.SelectedIndex = 1; Application.DoEvents();
                    var input = Find<TextBox>(window, "App process name, for example game.exe");
                    input.Text = "game.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.IsExcluded("GAME") && store.Load(out notice).IsExcluded("game"), "exception control saves normalized process name");
                    input.Text = "GAME.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 1, "duplicate exception is not added");
                    input.Text = "*.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 1 && Find<Label>(window, "Exception list feedback").Text.Contains("Paths and wildcards"), "invalid exception feedback");
                    Find<ListBox>(window, "Excluded apps").SelectedIndex = 0; FindButton(window, "&Remove selected").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 0 && store.Load(out notice).ExcludedApps.Count == 0, "remove exception saves");
                    window.Hide(); request.Set();
                    typeof(TrayApp).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, new object[] { null, EventArgs.Empty });
                    Check(window.Visible, "existing-instance request reopens settings");
                    input.Clear();
                    app.ChangeSettings(s => { s.Enabled = true; s.ThumbnailPreviews = true; s.RetryMovement = true; s.LaunchTimeoutSeconds = 12; });
                    window.ShowAbout(); Application.DoEvents();
                    Check(FindButton(window, "Boop the concierge") == null && Find<Label>(window, "Concierge message") == null, "playful controls removed");
                    Check(Find<Button>(window, "Check for updates") != null && Find<Label>(window, "Update status").Text.Contains("only when"), "manual update button and privacy notice present");
                    string importPath = Path.Combine(folder, "import.json");
                    new SettingsStore(importPath).Save(new AppSettings { LaunchTimeoutSeconds = 9, Enabled = false, EarlyPositioning = false });
                    window.ImportSettingsFile(importPath);
                    Check(!app.Settings.Enabled && !app.Settings.EarlyPositioning && !early.Checked && !store.Load(out notice).EarlyPositioning && store.Load(out notice).LaunchTimeoutSeconds == 9, "imported backup persists routing and early positioning settings");
                    File.WriteAllText(importPath, "broken");
                    bool rejected = false;
                    try { window.ImportSettingsFile(importPath); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected && !app.Settings.Enabled && app.Settings.LaunchTimeoutSeconds == 9, "invalid backup does not reset settings");
                    app.ChangeSettings(s => { s.Enabled = true; s.EarlyPositioning = true; s.LaunchTimeoutSeconds = 12; });
                    tabs.SelectedIndex = 0; Application.DoEvents();
                    FindButton(window, "Identify displays").PerformClick();
                    var overlays = (System.Collections.ICollection)typeof(SettingsWindow).GetField("identification", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    Check(overlays.Count == Screen.AllScreens.Length, "display identification shows one overlay per screen");
                    typeof(SettingsWindow).GetMethod("CloseIdentification", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                    var requests = new UpdateRequests();
                    using (var updateWindow = new SettingsWindow(app, new UpdateClient(delegate { return new HttpClient(new UpdateHandler(requests)); })))
                    {
                        updateWindow.Show(); updateWindow.ShowAbout(); Application.DoEvents();
                        Check(requests.Count == 0, "opening settings and update page makes no request");
                        var checkButton = Find<Button>(updateWindow, "Check for updates");
                        checkButton.PerformClick();
                        WaitFor(delegate { return checkButton.Enabled; });
                        Check(requests.Count == 1 && Find<Label>(updateWindow, "Update status").Text.Contains("up to date"), "manual check updates status without downloading older release");
                        requests.Delay = 1500;
                        checkButton.PerformClick();
                        FindButton(updateWindow, "Cancel download").PerformClick();
                        WaitFor(delegate { return checkButton.Enabled; });
                        Check(requests.Count == 2 && Find<Label>(updateWindow, "Update status").Text.Contains("cancelled"), "cancel control stops request and restores update button");
                    }
                    for (int i = 0; i < tabs.TabCount; i++)
                    {
                        tabs.SelectedIndex = i; Application.DoEvents();
                        using (var bitmap = new Bitmap(window.Width, window.Height))
                        { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(folder, "settings-page-" + i + ".png")); }
                    }
                    foreach (var screen in Screen.AllScreens)
                    {
                        Native.SetWindowPos(window.Handle, IntPtr.Zero, screen.WorkingArea.Left + 30, screen.WorkingArea.Top + 30, 0, 0, 0x0015);
                        Application.DoEvents();
                        for (int p = 0; p < tabs.TabCount; p++)
                        {
                            tabs.SelectedIndex = p; Application.DoEvents(); CheckFlows(window);
                        }
                        tabs.SelectedIndex = 0; Application.DoEvents();
                        using (var bitmap = new Bitmap(window.Width, window.Height))
                        { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(folder, screen.Primary ? "settings-primary.png" : "settings-secondary.png")); }
                        Console.WriteLine("Rendered on " + screen.DeviceName + ": " + window.Bounds + " DPI " + GetDpiForWindow(window.Handle));
                    }
                    window.Close();
                    Check(!window.Visible && !window.IsDisposed, "close hides to tray without disposal");
                    app.ShowSettings(); window.WindowState = FormWindowState.Minimized; Application.DoEvents();
                    Check(!window.Visible && !window.IsDisposed, "minimize hides to tray");
                    var tray = (NotifyIcon)typeof(TrayApp).GetField("tray", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                    typeof(NotifyIcon).GetMethod("OnMouseClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tray, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
                    Check(window.Visible && !window.IsDisposed, "single tray click reopens existing GUI");
                    window.Hide();
                    tray.ContextMenuStrip.Items[0].PerformClick();
                    Check(window.Visible, "tray menu opens settings");
                    app.ExitThread();
                    app.Dispose();
                    Check(window.IsDisposed, "quit releases settings window");
                }
                Console.WriteLine(count + " checks passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
