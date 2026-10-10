using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LaunchHere
{
    internal sealed class SettingsWindow : Form
    {
        private static readonly Color Ink = Color.FromArgb(25, 39, 58);
        private static readonly Color Muted = Color.FromArgb(87, 103, 122);
        private static readonly Color Accent = Color.FromArgb(15, 118, 110);
        private readonly TrayApp app;
        private readonly SettingsPages tabs;
        private readonly Label badge, footer, startupHelp;
        private readonly CheckBox enable, startup, previews, retries;
        private readonly NumericUpDown timeout;
        private readonly ListBox exclusions;
        private readonly TextBox processName;
        private readonly Label exceptionNotice;
        private readonly Button remove;
        private readonly DisplayMap displays;
        private readonly System.Windows.Forms.Timer displayTimer;
        private readonly ToolTip statusTip;
        private readonly Label conciergeMessage;
        private int boopCount;
        private bool syncing;

        internal SettingsWindow(TrayApp app)
        {
            this.app = app;
            Text = "LaunchHere";
            Icon = app.AppIcon;
            Font = new Font("Segoe UI", 10F);
            ForeColor = Ink;
            BackColor = Color.FromArgb(244, 247, 250);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(820, 700);
            MinimumSize = new Size(720, 620);
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(24, 20, 24, 16), ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            var brand = new Button { Image = BrandImage(app.AppIcon), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Size = new Size(52, 52), Padding = Padding.Empty, Margin = new Padding(0, 6, 12, 0), Cursor = Cursors.Hand, AccessibleName = "Boop the window concierge" };
            brand.FlatAppearance.BorderSize = 0;
            brand.Click += delegate { Boop(); };
            header.Controls.Add(brand, 0, 0);
            var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            title.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            title.Controls.Add(new Label { Text = "LaunchHere", Font = new Font("Segoe UI", 22F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0), ForeColor = Ink });
            title.Controls.Add(new Label { Text = "Your apps. Your screen. No more monitor hide-and-seek.", AutoSize = true, ForeColor = Muted, Margin = new Padding(2, 5, 0, 0) });
            header.Controls.Add(title, 1, 0);
            badge = new Label { TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 36, Margin = new Padding(8, 13, 0, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold), AccessibleName = "App status" };
            header.Controls.Add(badge, 2, 0);
            root.Controls.Add(header, 0, 0);

            tabs = new SettingsPages { Dock = DockStyle.Fill, Margin = new Padding(24, 18, 24, 0), AccessibleName = "Settings pages" };
            root.Controls.Add(tabs, 0, 1);
            var general = Page("Settings");
            var apps = Page("App exceptions");
            var about = Page("About & help");

            var generalFlow = Flow(general);
            enable = Check("&Enable LaunchHere", "Enable monitor routing");
            generalFlow.Controls.Add(Row(enable, "Bring app windows to the display whose taskbar you click. A tiny concierge does the legwork."));
            enable.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.Enabled = enable.Checked); };

            startup = Check("&Start with Windows", "Start with Windows");
            var startupRow = Row(startup, "Launch quietly in the tray when you sign in. Keep the app in a permanent folder.");
            startupHelp = (Label)startupRow.GetControlFromPosition(0, 1);
            generalFlow.Controls.Add(startupRow);
            startup.CheckedChanged += delegate { if (!syncing) app.SetStartup(startup.Checked); };

            previews = Check("Include taskbar &thumbnail previews", "Include taskbar thumbnail previews");
            generalFlow.Controls.Add(Row(previews, "Pick a thumbnail; we'll escort that window to the right screen."));
            previews.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.ThumbnailPreviews = previews.Checked); };

            retries = Check("&Retry if an app resets its position", "Retry window movement");
            generalFlow.Controls.Add(Row(retries, "For apps that wander back: up to two gentle nudges just after activation."));
            retries.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.RetryMovement = retries.Checked); };

            var timeoutLine = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
            timeoutLine.Controls.Add(new Label { Text = "Wait for launched apps", AutoSize = true, Margin = new Padding(0, 4, 16, 0) });
            timeout = new NumericUpDown { Minimum = 3, Maximum = 30, Width = 68, Value = 12, BackColor = Color.White, ForeColor = Ink, AccessibleName = "Launch detection timeout in seconds" };
            timeoutLine.Controls.Add(timeout);
            timeoutLine.Controls.Add(new Label { Text = "seconds", AutoSize = true, Margin = new Padding(8, 4, 0, 0) });
            generalFlow.Controls.Add(Row(timeoutLine, "A shorter wait reduces the chance of moving an unrelated app that activates later."));
            timeout.ValueChanged += delegate { if (!syncing) app.ChangeSettings(s => s.LaunchTimeoutSeconds = (int)timeout.Value); };

            var mapCard = Card();
            mapCard.Controls.Add(Heading("Connected displays • the screen squad"), 0, 0);
            displays = new DisplayMap { Dock = DockStyle.Top, Height = 116, Margin = new Padding(0, 8, 0, 0) };
            mapCard.Controls.Add(displays, 0, 1);
            mapCard.Controls.Add(Description("Click an app on the taskbar of the display where you want it to open."), 0, 2);
            generalFlow.Controls.Add(mapCard);

            var appFlow = Flow(apps);
            appFlow.Controls.Add(TextCard("The do-not-disturb club", "LaunchHere ignores the apps in this list. Useful for games or apps that manage their own positions. No window chauffeur for these VIPs."));
            var listCard = Card();
            exclusions = new ListBox { Dock = DockStyle.Top, Height = 174, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Ink, AccessibleName = "Excluded apps", Font = new Font("Segoe UI", 11F) };
            listCard.Controls.Add(exclusions, 0, 0);
            var addLine = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, AutoSize = false, RowCount = 1, ColumnCount = 3, Margin = new Padding(0, 12, 0, 0) };
            addLine.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            addLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            addLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            processName = new TextBox { Dock = DockStyle.Top, BackColor = Color.White, ForeColor = Ink, AccessibleName = "App process name, for example game.exe", Margin = new Padding(0, 4, 8, 0) };
            var add = Button("&Add", AddException);
            var browse = Button("&Browse…", BrowseException);
            add.AutoSize = browse.AutoSize = false;
            add.Padding = browse.Padding = Padding.Empty;
            add.Dock = DockStyle.Fill;
            browse.Dock = DockStyle.Fill;
            addLine.Controls.Add(processName, 0, 0);
            addLine.Controls.Add(add, 1, 0);
            addLine.Controls.Add(browse, 2, 0);
            listCard.Controls.Add(addLine, 0, 1);
            listCard.Controls.Add(Description("Enter an app's process name, such as game.exe, or choose its executable."), 0, 2);
            remove = Button("&Remove selected", RemoveException);
            remove.Enabled = false;
            listCard.Controls.Add(remove, 0, 3);
            exclusions.SelectedIndexChanged += delegate { remove.Enabled = exclusions.SelectedIndex >= 0; };
            processName.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { AddException(); e.SuppressKeyPress = true; } };
            exceptionNotice = Description("No exceptions. Everybody gets a tiny window chauffeur.");
            exceptionNotice.AccessibleName = "Exception list feedback";
            listCard.Controls.Add(exceptionNotice, 0, 4);
            appFlow.Controls.Add(listCard);
            appFlow.Controls.Add(TextCard("Finding the right name", "If you're unsure, open Windows Task Manager and look in the Details tab. Some Windows apps share a host process, so an exception for that host can affect more than one app."));

            var aboutFlow = Flow(about);
            aboutFlow.Controls.Add(TextCard("LaunchHere 1.2", "A tiny window concierge for Windows 10 and 11. Runs in the tray and puts app windows on the display you launch them from. No main-monitor favoritism."));
            var conciergeCard = Card();
            conciergeCard.Controls.Add(Heading("Meet your window concierge"), 0, 0);
            conciergeMessage = Description("A very small employee with a very specific job. Accepts imaginary biscuits.");
            conciergeMessage.AccessibleName = "Concierge message";
            conciergeCard.Controls.Add(conciergeMessage, 0, 1);
            var boop = Button("Boop the concierge", Boop);
            boop.BackColor = Color.FromArgb(255, 241, 207);
            boop.ForeColor = Color.FromArgb(117, 77, 13);
            boop.Margin = new Padding(0, 12, 0, 0);
            conciergeCard.Controls.Add(boop, 0, 2);
            aboutFlow.Controls.Add(conciergeCard);
            aboutFlow.Controls.Add(TextCard("Get started", "1. Show the taskbar on all displays in Windows taskbar settings.\n2. Click an app on your preferred display's taskbar.\n3. Close or minimize this window to keep the app running in the tray."));
            aboutFlow.Controls.Add(Button("Open Windows taskbar settings", delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, "Could not open Windows settings: " + ex.Message, "LaunchHere"); }
            }));
            aboutFlow.Controls.Add(TextCard("Privacy • zero gossip", "No accounts, analytics, or network connections. Preferences stay on this PC. The app observes mouse clicks and window activation without recording click history or keystrokes."));
            aboutFlow.Controls.Add(TextCard("A few things to know", "Windows does not identify which app a taskbar click launches, so the utility uses the next eligible window activation. Unrelated apps activating soon afterward can sometimes be moved. Apps may briefly appear on their old display; slow launches, administrator apps, and exclusive fullscreen games may need manual movement."));

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(24, 14, 24, 12), Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            footer = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font("Segoe UI", 9F), AccessibleName = "Save status" };
            bottom.Controls.Add(footer, 0, 0);
            var quit = Button("Quit app", delegate { app.ExitThread(); });
            var hide = Button("Hide to tray", delegate { Hide(); });
            hide.BackColor = Accent;
            hide.ForeColor = Color.White;
            hide.FlatAppearance.BorderSize = 0;
            quit.AutoSize = hide.AutoSize = false;
            quit.Padding = hide.Padding = Padding.Empty;
            quit.Dock = hide.Dock = DockStyle.Fill;
            bottom.Controls.Add(quit, 1, 0);
            bottom.Controls.Add(hide, 2, 0);
            root.Controls.Add(bottom, 0, 2);

            statusTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 500, ShowAlways = true };

            app.StateChanged += OnStateChanged;
            displayTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            displayTimer.Tick += delegate { if (Visible) displays.Invalidate(); };
            displayTimer.Start();
            RefreshState();
            AutoScaleDimensions = new SizeF(96, 96);
            if (SystemInformation.HighContrast) ApplyHighContrast(this);
        }

        private Panel Page(string text)
        {
            return tabs.AddPage(text);
        }

        private static FlowLayoutPanel Flow(Control parent)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 0, 6, 8) };
            flow.SizeChanged += delegate
            {
                foreach (Control child in flow.Controls) SizeCard(child, flow);
                // DPI changes also scale children's width constraints. Reapply the
                // final available width after Windows finishes that layout pass.
                if (flow.IsHandleCreated && flow.Tag == null)
                {
                    flow.Tag = true;
                    flow.BeginInvoke((Action)delegate
                    {
                        flow.Tag = null;
                        if (!flow.IsDisposed) foreach (Control child in flow.Controls) SizeCard(child, flow);
                    });
                }
            };
            flow.ControlAdded += delegate(object sender, ControlEventArgs e) { SizeCard(e.Control, flow); };
            parent.Controls.Add(flow);
            return flow;
        }

        private static void SizeCard(Control child, FlowLayoutPanel parent)
        {
            int width = Math.Max(200, parent.ClientSize.Width - 30);
            int minHeight = child.MinimumSize.Height;
            child.MinimumSize = new Size(0, minHeight);
            child.MaximumSize = new Size(width, 0);
            child.MinimumSize = new Size(width, minHeight);
            child.Width = width;
        }

        private static TableLayoutPanel Card()
        {
            var card = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12), BackColor = Color.White };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return card;
        }

        private static TableLayoutPanel Row(Control control, string helper)
        {
            var card = Card();
            card.Controls.Add(control, 0, 0);
            card.Controls.Add(Description(helper), 0, 1);
            return card;
        }

        private static TableLayoutPanel TextCard(string title, string text) { return Row(Heading(title), text); }
        private static Label Heading(string text) { return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = Padding.Empty }; }
        private static Image BrandImage(Icon icon)
        {
            using (var image = icon.ToBitmap()) return new Bitmap(image, new Size(48, 48));
        }
        private static Label Description(string text)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = Muted, Margin = new Padding(0, 7, 0, 0) };
        }
        private static CheckBox Check(string text, string name) { return new CheckBox { Text = text, AccessibleName = name, AutoSize = true, Margin = Padding.Empty }; }
        private static Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 215, 225);
            button.Click += delegate { action(); };
            return button;
        }

        private void AddException()
        {
            string name = AppSettings.ProcessName(processName.Text);
            if (name == null) { exceptionNotice.Text = "Enter a process name, such as game.exe. Paths and wildcards are not supported."; return; }
            if (app.Settings.IsExcluded(name)) { exceptionNotice.Text = "This app already has a do-not-disturb pass."; return; }
            app.ChangeSettings(s => s.ExcludedApps.Add(name));
            processName.Clear();
            processName.Focus();
        }

        private void BrowseException()
        {
            using (var dialog = new OpenFileDialog { Title = "Choose an app to exclude", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, Multiselect = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) { processName.Text = Path.GetFileName(dialog.FileName); AddException(); }
            }
        }

        private void RemoveException()
        {
            if (exclusions.SelectedItem == null) return;
            string name = exclusions.SelectedItem.ToString();
            app.ChangeSettings(s => s.ExcludedApps.Remove(name));
        }

        private void OnStateChanged(object sender, EventArgs e) { RefreshState(); }
        private void Boop()
        {
            string[] lines = { "Boop! One imaginary biscuit has been issued.", "Boop! The concierge has filed this under: excellent management.", "Boop! Window teleportation department says hello." };
            string message = lines[boopCount++ % lines.Length];
            conciergeMessage.Text = message;
            footer.Text = message;
            statusTip.SetToolTip(footer, message);
        }
        internal void RefreshState()
        {
            syncing = true;
            try
            {
                enable.Checked = app.Settings.Enabled;
                startup.Checked = app.StartupEnabled;
                startup.Enabled = app.StartupAvailable;
                startupHelp.Text = !app.StartupAvailable ? "Windows has restricted startup settings for this account. The app still works when launched manually." : app.LegacyStartupPresent ? "An older Taskbar Monitor startup entry exists. Enable this option to replace it with LaunchHere." : "The concierge clocks in when you sign in. Keep the app in a permanent folder.";
                previews.Checked = app.Settings.ThumbnailPreviews;
                retries.Checked = app.Settings.RetryMovement;
                timeout.Value = app.Settings.LaunchTimeoutSeconds;
                badge.Text = app.Settings.Enabled ? "●  On duty" : "●  Paused";
                badge.BackColor = app.Settings.Enabled ? Color.FromArgb(224, 243, 237) : Color.FromArgb(240, 236, 225);
                badge.ForeColor = app.Settings.Enabled ? Accent : Color.FromArgb(133, 94, 34);
                footer.Text = app.SaveNotice;
                statusTip.SetToolTip(footer, app.SaveNotice);
                object selection = exclusions.SelectedItem;
                exclusions.BeginUpdate();
                exclusions.Items.Clear();
                foreach (string name in app.Settings.ExcludedApps) exclusions.Items.Add(name);
                if (selection != null) exclusions.SelectedItem = selection;
                exclusions.EndUpdate();
                remove.Enabled = exclusions.SelectedIndex >= 0;
                exceptionNotice.Text = exclusions.Items.Count == 0 ? "No exceptions. Everybody gets a tiny window chauffeur." : exclusions.Items.Count + " app(s) in the do-not-disturb club. Changes take effect immediately.";
                if (SystemInformation.HighContrast) { badge.BackColor = SystemColors.Control; badge.ForeColor = SystemColors.ControlText; }
            }
            finally { syncing = false; }
        }

        internal void ShowAbout() { tabs.SelectedIndex = 2; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.StateChanged -= OnStateChanged;
                if (displayTimer != null) displayTimer.Dispose();
                if (statusTip != null) statusTip.Dispose();
                // PictureBox does not own its Image; release the logo bitmap explicitly.
                DisposeImages(this);
            }
            base.Dispose(disposing);
        }
        private static void DisposeImages(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var picture = child as PictureBox;
                if (picture != null && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
                var button = child as Button;
                if (button != null && button.Image != null) { button.Image.Dispose(); button.Image = null; }
                DisposeImages(child);
            }
        }
        private static void ApplyHighContrast(Control parent)
        {
            parent.BackColor = SystemColors.Control;
            parent.ForeColor = SystemColors.ControlText;
            foreach (Control child in parent.Controls) ApplyHighContrast(child);
        }
    }

    internal sealed class SettingsPages : Panel
    {
        private readonly FlowLayoutPanel navigation;
        private readonly Panel content;
        private readonly System.Collections.Generic.List<Panel> pages = new System.Collections.Generic.List<Panel>();
        private readonly System.Collections.Generic.List<Button> buttons = new System.Collections.Generic.List<Button>();
        private int selected;
        internal int TabCount { get { return pages.Count; } }
        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= pages.Count) return;
                selected = value;
                for (int i = 0; i < pages.Count; i++)
                {
                    pages[i].Visible = i == value;
                    buttons[i].BackColor = SystemInformation.HighContrast ? i == value ? SystemColors.Highlight : SystemColors.Control : i == value ? Color.FromArgb(224, 243, 237) : Color.FromArgb(244, 247, 250);
                    buttons[i].ForeColor = SystemInformation.HighContrast ? i == value ? SystemColors.HighlightText : SystemColors.ControlText : i == value ? Color.FromArgb(15, 118, 110) : Color.FromArgb(87, 103, 122);
                }
                pages[value].BringToFront();
            }
        }
        internal SettingsPages()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            layout.Controls.Add(navigation, 0, 0);
            layout.Controls.Add(content, 0, 1);
        }
        internal Panel AddPage(string title)
        {
            int index = pages.Count;
            var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), BackColor = Color.FromArgb(244, 247, 250) };
            var button = new Button { Text = title, AccessibleName = title + " page", AutoSize = true, MinimumSize = new Size(140, 38), Padding = new Padding(12, 2, 12, 2), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0), TabIndex = index };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { SelectedIndex = index; };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    SelectedIndex = (selected + (e.KeyCode == Keys.Right ? 1 : pages.Count - 1)) % pages.Count;
                    buttons[selected].Focus(); e.Handled = true;
                }
            };
            pages.Add(page); buttons.Add(button);
            navigation.Controls.Add(button); content.Controls.Add(page);
            SelectedIndex = selected;
            return page;
        }
    }

    internal sealed class DisplayMap : Control
    {
        internal DisplayMap()
        {
            DoubleBuffered = true;
            AccessibleName = "Connected monitor arrangement";
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var screens = Screen.AllScreens;
            Rectangle union = screens[0].Bounds;
            foreach (var screen in screens) union = Rectangle.Union(union, screen.Bounds);
            float scale = Math.Min((Width - 24F) / Math.Max(1, union.Width), (Height - 24F) / Math.Max(1, union.Height));
            float ox = (Width - union.Width * scale) / 2, oy = (Height - union.Height * scale) / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int index = 1;
            AccessibleDescription = screens.Length + " displays connected.";
            foreach (var screen in screens)
            {
                var rect = new RectangleF(ox + (screen.Bounds.Left - union.Left) * scale + 2, oy + (screen.Bounds.Top - union.Top) * scale + 2, Math.Max(10, screen.Bounds.Width * scale - 4), Math.Max(10, screen.Bounds.Height * scale - 4));
                Color fill = SystemInformation.HighContrast ? SystemColors.Control : screen.Primary ? Color.FromArgb(224, 243, 237) : Color.FromArgb(236, 241, 247);
                Color stroke = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(119, 153, 163);
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, rect);
                using (var pen = new Pen(stroke, 1.5F)) e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                string label = "Display " + index++ + (screen.Primary ? "\nPrimary" : "");
                TextRenderer.DrawText(e.Graphics, label, Font, Rectangle.Round(rect), SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(25, 39, 58), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
