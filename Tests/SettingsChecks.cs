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
        private static void CheckScrollColors(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var flow = child as GlassFlow;
                if (flow != null && flow.Visible && flow.VerticalScroll.Visible && !SystemInformation.HighContrast)
                {
                    Form form = flow.FindForm(); form.Activate(); Application.DoEvents();
                    if (Native.GetForegroundWindow() != form.Handle) throw new Exception("Only the foreground test window may be sampled.");
                    int barWidth = flow.Width - flow.ClientSize.Width;
                    var point = flow.PointToScreen(new Point(flow.ClientSize.Width + barWidth / 2, flow.Height * 3 / 4));
                    using (var image = new Bitmap(1, 1))
                    using (var graphics = Graphics.FromImage(image))
                    {
                        graphics.CopyFromScreen(point, Point.Empty, new Size(1, 1));
                        Color pixel = image.GetPixel(0, 0);
                        Color expected;
                        using (var reference = new Bitmap(1, 1))
                        using (var background = Graphics.FromImage(reference))
                        {
                            var local = flow.PointToClient(point);
                            background.TranslateTransform(-local.X, -local.Y);
                            Glass.ParentBackground(background, flow); expected = reference.GetPixel(0, 0);
                        }
                        Check(Math.Abs(pixel.R - expected.R) <= 6 && Math.Abs(pixel.G - expected.G) <= 6 && Math.Abs(pixel.B - expected.B) <= 6, "native scrollbar matches the theme after a setting changes");
                    }
                }
                CheckScrollColors(child);
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
        private static void CheckMotion()
        {
            using (var form = new GlassForm { ClientSize = new Size(420, 160), ShowInTaskbar = false })
            using (var owner = new Control { Bounds = new Rectangle(10, 10, 100, 40) })
            {
                form.Controls.Add(owner); form.Show(); Application.DoEvents();
                bool allowed = true; int frames = 0;
                using (var motion = new GlassMotion(owner, 0, delegate { frames++; }, delegate { return allowed; }))
                {
                    motion.MoveTo(1, 240);
                    WaitFor(() => motion.Value > .1);
                    Check(motion.Value < 1 && motion.Running, "motion draws intermediate frames");
                    double beforeReverse = motion.Value;
                    motion.MoveTo(0, 180);
                    Check(motion.Value >= beforeReverse && motion.Value < 1, "reversing motion continues from its current position");
                    WaitFor(() => !motion.Running);
                    Check(motion.Value == 0, "reversed motion settles at the requested state");
                    int settledFrames = frames;
                    var idle = System.Diagnostics.Stopwatch.StartNew();
                    while (idle.ElapsedMilliseconds < 80) { Application.DoEvents(); Thread.Sleep(5); }
                    Check(frames == settledFrames, "settled motion stops producing frames");
                    motion.MoveTo(1, 240); owner.Hide();
                    Check(!motion.Running && motion.Value == 1, "hidden controls finish motion and stop the timer");
                    owner.Show(); allowed = false; motion.MoveTo(0, 240);
                    Check(!motion.Running && motion.Value == 0, "reduced motion changes state immediately");
                    allowed = true; motion.MoveTo(1, 240); allowed = false;
                    WaitFor(() => !motion.Running);
                    Check(motion.Value == 1, "disabling motion during a transition settles safely");
                    allowed = true; motion.MoveTo(0, 240); motion.Dispose();
                    Check(!motion.Running, "disposing an active control transition releases its timer");
                }
                using (var toggle = new GlassToggle { Text = "", Bounds = new Rectangle(10, 70, 120, 40) })
                {
                    form.Controls.Add(toggle); Application.DoEvents();
                    var position = (GlassMotion)typeof(GlassToggle).GetField("position", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(toggle);
                    toggle.Checked = true;
                    Check(toggle.Checked, "toggle behavior changes before the visual transition finishes");
                    if (GlassMotion.AnimationsEnabled())
                    {
                        WaitFor(() => position.Value > .1);
                        using (var image = new Bitmap(toggle.Width, toggle.Height))
                        {
                            toggle.DrawToBitmap(image, toggle.ClientRectangle);
                            int first = -1, last = -1;
                            double scale = toggle.DeviceDpi / 96.0;
                            for (int x = 3; x < Math.Min(toggle.Width, (int)(39 * scale)); x++)
                            {
                                Color pixel = image.GetPixel(x, toggle.Height / 2);
                                if (pixel.R > 250 && pixel.G > 250 && pixel.B > 250) { if (first < 0) first = x; last = x; }
                            }
                            double center = (first + last) / 2.0;
                            Check(first >= 0 && center > 1 + 11 * scale && center < 1 + 27 * scale, "toggle paints its knob between the two endpoints (center " + center + ", DPI " + toggle.DeviceDpi + ")");
                        }
                    }
                    WaitFor(() => !position.Running);
                    Check(position.Value == 1, "toggle settles fully on");
                    toggle.Checked = false; form.Hide();
                    Check(!position.Running && position.Value == 0, "hiding settings settles the toggle fully off");
                }
            }
        }
        private static Color ButtonPixel(GlassButton button)
        {
            using (var image = new Bitmap(button.Width, button.Height))
            {
                button.DrawToBitmap(image, button.ClientRectangle);
                return image.GetPixel(button.Width / 2, button.Height / 2);
            }
        }
        private static void CheckButtonMotion()
        {
            using (var form = new GlassForm { ClientSize = new Size(300, 180), ShowInTaskbar = false })
            using (var button = new GlassButton { Bounds = new Rectangle(30, 50, 180, 40), Text = "", BackColor = Color.White })
            {
                form.Controls.Add(button); form.Show(); Application.DoEvents();
                Color normal = ButtonPixel(button);
                Color? intermediate = null;
                var tint = (GlassTint)typeof(GlassButton).GetField("tint", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(button);
                var motion = (GlassMotion)typeof(GlassTint).GetField("motion", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tint);
                button.BackColor = Glass.Accent;
                if (GlassMotion.AnimationsEnabled())
                {
                    WaitFor(() => motion.Value > .1);
                    intermediate = ButtonPixel(button);
                }
                WaitFor(() => !motion.Running);
                Color selected = ButtonPixel(button);
                if (intermediate.HasValue) Check(intermediate.Value.ToArgb() != normal.ToArgb() && intermediate.Value.ToArgb() != selected.ToArgb(), "navigation selection blends through intermediate colors");
                Check(selected.R < 90 && selected.B > 190, "selected button settles to the blue accent");
                typeof(GlassButton).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { new KeyEventArgs(Keys.Space) });
                WaitFor(() => !motion.Running);
                Color pressed = ButtonPixel(button);
                Check(pressed.B < selected.B, "keyboard press gives visible button feedback");
                typeof(GlassButton).GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { new KeyEventArgs(Keys.Space) });
                WaitFor(() => !motion.Running);
                Check(ButtonPixel(button).ToArgb() == selected.ToArgb(), "releasing a button restores its resting colors");
            }
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
                CheckMotion();
                CheckButtonMotion();
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
                    tabs.SelectedIndex = 1; tabs.SelectedIndex = 2; tabs.SelectedIndex = 0;
                    var pageMotion = (GlassMotion)typeof(SettingsPages).GetField("pageMotion", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tabs);
                    var blend = (PageBlend)typeof(SettingsPages).GetField("blend", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tabs);
                    WaitFor(() => !pageMotion.Running);
                    Check(!blend.Visible && !blend.HasFrames, "settled transitions release page snapshots");
                    Check(tabs.SelectedIndex == 0 && Find<CheckBox>(window, "Enable monitor routing").Visible, "rapid page changes finish on the requested page");
                    tabs.SelectedIndex = 1; window.Hide();
                    Check(!pageMotion.Running, "hiding settings stops page transitions");
                    Check(!blend.HasFrames, "hiding settings releases page snapshots");
                    window.Show(); tabs.SelectedIndex = 0;
                    window.ClientSize = new Size(window.ClientSize.Width - 10, window.ClientSize.Height - 10);
                    Check(!pageMotion.Running, "resizing during a page transition restores the normal layout");
                    var firstPageButton = Find<Button>(tabs, "Settings page"); firstPageButton.Focus();
                    typeof(GlassButton).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(firstPageButton, new object[] { new KeyEventArgs(Keys.Right) });
                    Check(tabs.SelectedIndex == 1 && Find<Button>(tabs, "App exceptions page").Focused, "keyboard page navigation moves focus immediately");
                    tabs.SelectedIndex = 0; WaitFor(() => !pageMotion.Running);
                    Check(window.Text == "LaunchHere", "window uses LaunchHere branding");
                    var enable = Find<CheckBox>(window, "Enable monitor routing");
                    var signal = Find<AsciiSignal>(window, "Animated display link");
                    Check(signal != null && !signal.TabStop && !signal.Running, "paused routing keeps decorative ASCII animation idle");
                    Check(!enable.Checked, "saved pause preference loads into GUI");
                    enable.Checked = true;
                    Application.DoEvents();
                    Check(signal.Running == GlassMotion.AnimationsEnabled(), "ASCII display animation follows Windows motion preferences");
                    Check(app.Settings.Enabled && store.Load(out notice).Enabled, "enable control saves and updates engine");
                    enable.Checked = false; CheckScrollColors(window);
                    enable.Checked = true; CheckScrollColors(window);
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
                    if (GlassMotion.AnimationsEnabled() && pageMotion.Running)
                    {
                        Check(blend.Visible && blend.HasFrames, "page changes draw a composited transition");
                        Check(SendMessage(blend.Handle, 0x84, IntPtr.Zero, IntPtr.Zero) == new IntPtr(-1), "transition overlay allows pointer input through to the real page");
                        Message key = Message.Create(input.Handle, 0x100, new IntPtr((int)Keys.A), IntPtr.Zero);
                        Check(!tabs.PreFilterMessage(ref key) && !pageMotion.Running && !blend.HasFrames, "first page input finishes the transition without consuming the input");
                    }
                    input.Text = "game.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.IsExcluded("GAME") && store.Load(out notice).IsExcluded("game"), "exception control saves normalized process name");
                    input.Text = "GAME.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 1, "duplicate exception is not added");
                    input.Text = "*.exe"; FindButton(window, "&Add").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 1 && Find<Label>(window, "Exception list feedback").Text.Contains("Paths and wildcards"), "invalid exception feedback");
                    Find<ListBox>(window, "Excluded apps").SelectedIndex = 0; FindButton(window, "&Remove selected").PerformClick();
                    Check(app.Settings.ExcludedApps.Count == 0 && store.Load(out notice).ExcludedApps.Count == 0, "remove exception saves");
                    window.Hide(); request.Set();
                    Check(!signal.Running, "ASCII animation stops immediately when settings hide to the tray");
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
