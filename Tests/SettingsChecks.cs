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
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmFlush();
        private static int count;
        private static string liveProofFolder;
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
        private static Label FindLabel(Control parent, string text)
        {
            foreach(Control child in parent.Controls)
            {
                if(child is Label && child.Text == text) return (Label)child;
                var match=FindLabel(child,text); if(match!=null) return match;
            }
            return null;
        }
        private static void CheckCanvasLeaf(SettingsWindow window, Control surface, string area)
        {
            if(!GlassMotion.AnimationsEnabled()) return;
            var activate=typeof(Form).GetMethod("OnActivated",BindingFlags.NonPublic|BindingFlags.Instance);
            var deactivate=typeof(Form).GetMethod("OnDeactivate",BindingFlags.NonPublic|BindingFlags.Instance);
            var tick=typeof(ChevronScene).GetMethod("Tick",BindingFlags.NonPublic|BindingFlags.Instance);
            deactivate.Invoke(window,new object[]{EventArgs.Empty}); Application.DoEvents(); surface.Update();
            using(var before=new Bitmap(surface.Width,surface.Height))
            using(var after=new Bitmap(surface.Width,surface.Height))
            {
                surface.DrawToBitmap(before,surface.ClientRectangle);
                int paints=0; PaintEventHandler painted=delegate { paints++; }; surface.Paint+=painted;
                activate.Invoke(window,new object[]{EventArgs.Empty});
                Point local=new Point(Math.Max(1,surface.Width-30),surface.Height/2);
                Message move=Message.Create(surface.Handle,0x200,IntPtr.Zero,new IntPtr((local.Y<<16)|(local.X&0xffff)));
                Check(!window.Scene.PreFilterMessage(ref move),area+" hover passes through to normal UI input");
                for(int frame=0;frame<3;frame++)
                {
                    Thread.Sleep(20); tick.Invoke(window.Scene,new object[]{null,EventArgs.Empty}); surface.Update();
                }
                surface.Paint-=painted;
                Check(paints>0,area+" receives live background repaints");
                surface.DrawToBitmap(after,surface.ClientRectangle);
                bool changed=false;
                for(int y=0;y<before.Height && !changed;y+=2)
                for(int x=0;x<before.Width;x+=2) if(before.GetPixel(x,y)!=after.GetPixel(x,y)) { changed=true; break; }
                Check(changed,area+" shows a visible hover response");
                Message click=Message.Create(surface.Handle,0x201,IntPtr.Zero,new IntPtr((local.Y<<16)|(local.X&0xffff)));
                Check(!window.Scene.PreFilterMessage(ref click) && window.Scene.RippleCount>0,area+" click starts a ripple without consuming input");
            }
            deactivate.Invoke(window,new object[]{EventArgs.Empty}); Application.DoEvents();
        }
        private static Bitmap CaptureLiveWindow(SettingsWindow window)
        {
            // Read only our own test window's already painted client pixels.
            // DrawToBitmap would force a repaint and hide missing WM_PAINT backgrounds.
            DwmFlush();
            var image = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
            IntPtr source = GetDC(window.Handle);
            try
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    IntPtr target = graphics.GetHdc();
                    try { if (!BitBlt(target, 0, 0, image.Width, image.Height, source, 0, 0, 0x00CC0020)) throw new Exception("Own-window capture failed"); }
                    finally { graphics.ReleaseHdc(target); }
                }
            }
            catch { image.Dispose(); throw; }
            finally { ReleaseDC(window.Handle, source); }
            return image;
        }
        private static Control CanvasSurfaceAt(Control parent, Point location)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible || !child.Bounds.Contains(location)) continue;
                var local = new Point(location.X - child.Left, location.Y - child.Top);
                if (child.Region != null && !child.Region.IsVisible(local)) continue;
                return CanvasSurfaceAt(child, local);
            }
            return parent;
        }
        private static void PumpCanvas(int milliseconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
        }
        private static bool LivePixelsChanged(Bitmap before, Bitmap after, Point location)
        {
            var area = Rectangle.Intersect(new Rectangle(location.X - 40, location.Y - 40, 80, 80), new Rectangle(Point.Empty, before.Size));
            for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++) if (before.GetPixel(x, y) != after.GetPixel(x, y)) return true;
            return false;
        }
        private static void UpdatePendingCanvas(Control control)
        {
            // Update processes existing invalid areas; it does not invalidate or
            // force a new drawing as Refresh/DrawToBitmap would.
            control.Update();
            foreach (Control child in control.Controls) if (child.Visible && !(child is GlassCard)) UpdatePendingCanvas(child);
        }
        private static void SettleTestPointer(SettingsWindow window, Point location)
        {
            // A real cursor can send unrelated moves while the test window is
            // visible. Set the test pointer after pumping those messages, then
            // let the normal scene invalidation and WM_PAINT path draw it.
            window.Scene.MovePointer(location); Thread.Sleep(20);
            typeof(ChevronScene).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window.Scene, new object[] { null, EventArgs.Empty });
            UpdatePendingCanvas(window);
        }
        private static void CheckLiveCanvas(SettingsWindow window, Point location, string area)
        {
            if (!GlassMotion.AnimationsEnabled()) return;
            var activate = typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance);
            var deactivate = typeof(Form).GetMethod("OnDeactivate", BindingFlags.NonPublic | BindingFlags.Instance);
            bool previousTopMost = window.TopMost;
            try
            {
                window.TopMost = true;
                deactivate.Invoke(window, new object[] { EventArgs.Empty }); PumpCanvas(150);
                var target = CanvasSurfaceAt(window, location);
                var local = target.PointToClient(window.PointToScreen(location));
                IntPtr position = new IntPtr((local.Y << 16) | (local.X & 0xffff));
                using (var before = CaptureLiveWindow(window))
                {
                    activate.Invoke(window, new object[] { EventArgs.Empty });
                    Check(PostMessage(target.Handle, 0x200, IntPtr.Zero, position), area + " receives normal queued mouse input");
                    PumpCanvas(100);
                    SettleTestPointer(window, location);
                    using (var hovered = CaptureLiveWindow(window))
                    {
                        if (!LivePixelsChanged(before, hovered, location))
                        {
                            Console.WriteLine("Live canvas diagnostic: " + area + " at " + location + "; target=" + target.GetType().Name + "; target pointer=" + typeof(ChevronScene).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window.Scene) + "; running=" + window.Scene.Running);
                            before.Save(Path.Combine(liveProofFolder, area.Replace(' ', '-') + "-before.png"));
                            hovered.Save(Path.Combine(liveProofFolder, area.Replace(' ', '-') + "-hover.png"));
                        }
                        Check(LivePixelsChanged(before, hovered, location), area + " visibly reacts without a forced repaint");
                        Check(PostMessage(target.Handle, 0x201, IntPtr.Zero, position), area + " receives normal queued clicks");
                        Check(PostMessage(target.Handle, 0x202, IntPtr.Zero, position), area + " releases normal mouse capture after clicking");
                        PumpCanvas(100);
                        SettleTestPointer(window, location);
                        Check(window.Scene.RippleCount > 0, area + " click creates a ripple through the real message loop");
                        using (var clicked = CaptureLiveWindow(window)) Check(LivePixelsChanged(hovered, clicked, location), area + " displays the click ripple live");
                    }
                }
            }
            finally
            {
                deactivate.Invoke(window, new object[] { EventArgs.Empty }); PumpCanvas(50);
                window.TopMost = previousTopMost;
            }
        }
        private static void CheckFlows(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var f = child as FlowLayoutPanel;
                if (f != null && f.Visible && f.AutoScroll)
                {
                    Check(!f.HorizontalScroll.Visible, "page layout fits width at current DPI");
                    Check((Native.GetWindowLongPtr(f.Handle, -16).ToInt64() & 0x00100000) == 0, "page does not retain a native horizontal scrollbar");
                }
                CheckFlows(child);
                var deck = child as LauncherDeck;
                if (deck != null)
                {
                    var tileRow = Find<FlowLayoutPanel>(deck, "Quick launch apps and commands");
                    bool fits = true;
                    foreach (Control tile in tileRow.Controls) if (tile.Right > tileRow.ClientSize.Width || tile.Bottom > tileRow.ClientSize.Height) fits = false;
                    Check(fits, "launcher tiles fit without horizontal scrolling at current DPI");
                }
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
                    int barWidth = flow.Width - flow.ClientSize.Width;
                    var point = flow.PointToScreen(new Point(flow.ClientSize.Width + barWidth / 2, flow.Height * 3 / 4));
                    using (var image = new Bitmap(1, 1))
                    using (var graphics = Graphics.FromImage(image))
                    {
                        if (Native.GetForegroundWindow() == form.Handle) graphics.CopyFromScreen(point, Point.Empty, new Size(1, 1));
                        else
                        {
                            using (var snapshot = new Bitmap(flow.Width, flow.Height))
                            {
                                flow.DrawToBitmap(snapshot, new Rectangle(Point.Empty, flow.Size));
                                var local = flow.PointToClient(point); image.SetPixel(0, 0, snapshot.GetPixel(local.X, local.Y));
                            }
                            Console.WriteLine("Native scrollbar verified with WM_PRINT; desktop did not grant test focus.");
                        }
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
                Check(selected.B > selected.G && selected.G > selected.R && selected.B > 90, "selected button settles to the muted blue accent");
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
                    var content = (Panel)typeof(SettingsPages).GetField("content",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(tabs);
                    Check(content.Height >= tabs.ClientSize.Height - 60 * window.DeviceDpi / 96, "page content fills the space below the compact navigation row");
                    tabs.SelectedIndex = 1; tabs.SelectedIndex = 2; tabs.SelectedIndex = 0;
                    var pageMotion = (GlassMotion)typeof(SettingsPages).GetField("pageMotion", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tabs);
                    var blend = (PageBlend)typeof(SettingsPages).GetField("blend", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tabs);
                    WaitFor(() => !pageMotion.Running);
                    Check(pageMotion.Value == 1, "tab indicator settles without page snapshots");
                    Check(tabs.SelectedIndex == 0 && Find<CheckBox>(window, "Enable monitor routing").Visible, "rapid page changes finish on the requested page");
                    tabs.SelectedIndex = 1; window.Hide();
                    Check(!pageMotion.Running, "hiding settings stops page transitions");
                    Check(pageMotion.Value == 1, "hiding settles the tab indicator");
                    window.Show(); tabs.SelectedIndex = 0;
                    window.ClientSize = new Size(window.ClientSize.Width - 10, window.ClientSize.Height - 10);
                    Check(!pageMotion.Running, "resizing during a page transition restores the normal layout");
                    var firstPageButton = Find<Button>(tabs, "Settings page"); firstPageButton.Focus();
                    typeof(GlassButton).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(firstPageButton, new object[] { new KeyEventArgs(Keys.Right) });
                    Check(tabs.SelectedIndex == 1 && Find<Button>(tabs, "App exceptions page").Focused, "keyboard page navigation moves focus immediately");
                    tabs.SelectedIndex = 0; WaitFor(() => !pageMotion.Running);
                    Check(window.Text == "LaunchHere", "window uses LaunchHere branding");
                    var launcher = Find<LauncherDeck>(window, "Command launcher");
                    launcher.Search.Text = "App exceptions";
                    Check(launcher.MatchCount == 1, "command search filters local actions");
                    typeof(TextBox).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(launcher.Search, new object[] { new KeyEventArgs(Keys.Enter) });
                    Check(tabs.SelectedIndex == 1, "Enter runs the visible command without opening a network connection");
                    launcher.Search.Text = "zz-no-command";
                    Check(launcher.MatchCount == 0, "unmatched search does not invent a command");
                    launcher.Search.Clear(); tabs.SelectedIndex = 0;
                    string pin = Path.Combine(folder, "Pinned test.exe"); File.Copy(Application.ExecutablePath, pin, true);
                    window.PinAppFile(pin); window.PinAppFile(pin);
                    Check(app.Settings.PinnedApps.Count == 1 && store.Load(out notice).PinnedApps.Count == 1, "local app pins persist and reject duplicates");
                    Check(AppSettings.LocalAppPath("https://example.com/app.exe") == null && AppSettings.LocalAppPath(@"\\server\app.exe") == null && AppSettings.LocalAppPath("relative.exe") == null && AppSettings.LocalAppPath(@"C:\app.exe:stream") == null, "app pins reject URLs, network paths, relative paths and streams");
                    string launched = null;
                    app.ChangeSettings(s => s.Enabled = true);
                    app.LaunchPinnedApp(pin, Screen.FromHandle(window.Handle), p => launched = p);
                    Check(launched == pin && (string)typeof(TrayApp).GetField("targetProcess", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app) == "Pinned test", "pinned launch arms routing only for the selected executable");
                    bool failedLaunch = false;
                    try { app.LaunchPinnedApp(pin, Screen.FromHandle(window.Handle), p => { throw new IOException("Test launch failure"); }); } catch (IOException) { failedLaunch = true; }
                    Check(failedLaunch && typeof(TrayApp).GetField("targetDevice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app) == null, "failed pinned launch cancels pending routing");
                    app.ChangeSettings(s => { s.PinnedApps.Clear(); s.Enabled = false; });
                    window.Activate(); Application.DoEvents();
                    typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                    var sceneTest = window.Scene;
                    sceneTest.MovePointer(new Point(180, 130)); sceneTest.Ripple(new Point(180, 130));
                    Check(sceneTest.Running == GlassMotion.AnimationsEnabled(), "reactive background follows Windows animation preferences");
                    for (int ripple = 0; ripple < 8; ripple++) sceneTest.Ripple(new Point(180 + ripple * 4, 130));
                    Check(sceneTest.RippleCount <= 4, "rapid clicks keep a bounded ripple count");
                    Message backgroundMove = Message.Create(launcher.Search.Handle, 0x200, IntPtr.Zero, new IntPtr((4 << 16) | 4));
                    Check(!sceneTest.PreFilterMessage(ref backgroundMove), "cursor reactivity does not consume input");
                    if (GlassMotion.AnimationsEnabled())
                    {
                        using (var before = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
                        using (var after = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
                        {
                            typeof(Form).GetMethod("OnDeactivate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                            Check(!sceneTest.Running && sceneTest.RippleCount == 0, "inactive windows clear reactive work");
                            using (var drawing = Graphics.FromImage(before)) sceneTest.Draw(drawing, false);
                            typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                            sceneTest.MovePointer(new Point(84, 322)); sceneTest.Ripple(new Point(84, 322));
                            using (var drawing = Graphics.FromImage(after)) sceneTest.Draw(drawing, false);
                            bool different = false;
                            for (int y = 305; y < 345; y++) for (int x = 64; x < 104; x++) if (before.GetPixel(x, y) != after.GetPixel(x, y)) different = true;
                            Check(different, "click ripple paints a visible change in the chevron grid");
                        }
                    }
                    window.Hide();
                    Check(!sceneTest.Running && sceneTest.RippleCount == 0, "hiding clears background animation immediately");
                    window.Show(); window.Activate(); Application.DoEvents();
                    var enable = Find<CheckBox>(window, "Enable monitor routing");
                    var scene = window.Scene;
                    WaitFor(() => !scene.Running); Check(!scene.Running, "chevron background rests when there is no interaction");
                    Application.DoEvents();
                    int cardPaints = 0, togglePaints = 0;
                    var card = enable.Parent;
                    while(card != null && !(card is GlassCard)) card = card.Parent;
                    PaintEventHandler cardPaint = delegate { cardPaints++; }, togglePaint = delegate { togglePaints++; };
                    card.Paint += cardPaint; enable.Paint += togglePaint;
                    typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                    scene.MovePointer(new Point(120,450)); scene.Ripple(new Point(120,450));
                    for(int frame=0;frame<5;frame++)
                    {
                        typeof(ChevronScene).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scene,new object[] { null,EventArgs.Empty });
                        Application.DoEvents(); Thread.Sleep(10);
                    }
                    card.Paint -= cardPaint; enable.Paint -= togglePaint;
                    Check(cardPaints == 0 && togglePaints == 0, "background frames do not repaint cards or their interactive controls");
                    typeof(Form).GetMethod("OnDeactivate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                    CheckCanvasLeaf(window,FindLabel(window,"LaunchHere"),"header title background");
                    CheckCanvasLeaf(window,Find<Label>(window,"Save status"),"footer status background");
                    CheckCanvasLeaf(window,(FlowLayoutPanel)typeof(SettingsPages).GetField("navigation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(tabs),"tab strip background");
                    float canvasScale = window.DeviceDpi / 96F;
                    liveProofFolder = folder;
                    var logo = Find<PictureBox>(window, "LaunchHere logo");
                    var nav = (FlowLayoutPanel)typeof(SettingsPages).GetField("navigation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(tabs);
                    Check(launcher.Region != null && !launcher.Region.IsVisible(0, 0) && launcher.Region.IsVisible(launcher.Width / 2, launcher.Height / 2), "cached launcher leaves rounded corner gaps open to the live canvas");
                    CheckLiveCanvas(window, new Point(window.ClientSize.Width / 2, (int)(14 * canvasScale)), "top header gap");
                    CheckLiveCanvas(window, window.PointToClient(logo.PointToScreen(new Point(2, 2))), "logo background");
                    CheckLiveCanvas(window, window.PointToClient(FindLabel(window,"LaunchHere").PointToScreen(new Point(20, 12))), "title background");
                    CheckLiveCanvas(window, window.PointToClient(launcher.PointToScreen(new Point(2, 2))), "launcher rounded corner");
                    CheckLiveCanvas(window, window.PointToClient(launcher.PointToScreen(new Point(-4, launcher.Height / 2))), "launcher outside margin");
                    CheckLiveCanvas(window, window.PointToClient(nav.PointToScreen(new Point(nav.Width - 20, nav.Height / 2))), "top navigation gap");
                    Check(!enable.Checked, "saved pause preference loads into GUI");
                    enable.Checked = true;
                    Application.DoEvents();
                    Check(!scene.Running, "changing routing does not start a background animation loop");
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
                        Check(blend.Visible, "page changes animate a lightweight selection indicator");
                        Check(SendMessage(blend.Handle, 0x84, IntPtr.Zero, IntPtr.Zero) == new IntPtr(-1), "transition overlay allows pointer input through to the real page");
                        Message key = Message.Create(input.Handle, 0x100, new IntPtr((int)Keys.A), IntPtr.Zero);
                        Check(!tabs.PreFilterMessage(ref key) && input.Visible, "page input is immediately available during the selection animation");
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
                    Check(!scene.Running && scene.RippleCount == 0, "chevron animation stops and clears ripples when hidden to tray");
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
