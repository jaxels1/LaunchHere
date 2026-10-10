using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LaunchHere
{
    internal sealed class SettingsWindow : GlassForm
    {
        private static readonly Color Ink = Glass.Ink;
        private static readonly Color Muted = Glass.Muted;
        private static readonly Color Accent = Glass.Accent;
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
        private readonly Button updateButton, cancelUpdate;
        private readonly Label updateStatus;
        private readonly ProgressBar updateProgress;
        private readonly UpdateClient updater;
        private CancellationTokenSource updateCancellation;
        private readonly System.Collections.Generic.List<Form> identification = new System.Collections.Generic.List<Form>();
        private readonly System.Windows.Forms.Timer identifyTimer;
        private bool syncing;

        internal SettingsWindow(TrayApp app, UpdateClient updater = null)
        {
            this.app = app;
            this.updater = updater ?? new UpdateClient();
            Text = "LaunchHere";
            Icon = app.AppIcon;
            Font = new Font("Segoe UI", 10F);
            ForeColor = Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(900, 740);
            MinimumSize = new Size(780, 640);
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(28, 20, 28, 16), ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            var brand = new PictureBox { Image = BrandImage(app.AppIcon), BackColor = Color.Transparent, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 6, 12, 0), AccessibleName = "LaunchHere logo" };
            header.Controls.Add(brand, 0, 0);
            var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            title.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            title.Controls.Add(new Label { Text = "LaunchHere", Font = new Font("Segoe UI", 22F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0), ForeColor = Ink });
            title.Controls.Add(new Label { Text = "Open apps on the display you choose.", AutoSize = true, ForeColor = Muted, Margin = new Padding(2, 5, 0, 0) });
            header.Controls.Add(title, 1, 0);
            badge = new Label { TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 36, Margin = new Padding(8, 13, 0, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold), AccessibleName = "App status" };
            header.Controls.Add(badge, 2, 0);
            root.Controls.Add(header, 0, 0);

            tabs = new SettingsPages { Dock = DockStyle.Fill, Margin = new Padding(24, 18, 24, 0), AccessibleName = "Settings pages" };
            root.Controls.Add(tabs, 0, 1);
            var general = Page("Settings");
            var apps = Page("App exceptions");
            var about = Page("Updates && privacy");

            var generalFlow = Flow(general);
            enable = Check("&Enable LaunchHere", "Enable monitor routing");
            generalFlow.Controls.Add(Row(enable, "Move app windows to the display whose taskbar you click."));
            enable.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.Enabled = enable.Checked); };

            startup = Check("&Start with Windows", "Start with Windows");
            var startupRow = Row(startup, "Launch quietly in the tray when you sign in. Keep the app in a permanent folder.");
            startupHelp = (Label)startupRow.GetControlFromPosition(0, 1);
            generalFlow.Controls.Add(startupRow);
            startup.CheckedChanged += delegate { if (!syncing) app.SetStartup(startup.Checked); };

            previews = Check("Include taskbar &thumbnail previews", "Include taskbar thumbnail previews");
            generalFlow.Controls.Add(Row(previews, "Route windows selected from taskbar thumbnail previews."));
            previews.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.ThumbnailPreviews = previews.Checked); };

            retries = Check("&Retry if an app resets its position", "Retry window movement");
            generalFlow.Controls.Add(Row(retries, "Repeat movement up to twice if the app reapplies its saved position."));
            retries.CheckedChanged += delegate { if (!syncing) app.ChangeSettings(s => s.RetryMovement = retries.Checked); };

            var timeoutLine = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
            timeoutLine.Controls.Add(new Label { Text = "Wait for launched apps", AutoSize = true, Margin = new Padding(0, 4, 16, 0) });
            timeout = new NumericUpDown { Minimum = 3, Maximum = 30, Width = 68, Value = 12, BackColor = Color.White, ForeColor = Ink, AccessibleName = "Launch detection timeout in seconds" };
            timeoutLine.Controls.Add(timeout);
            timeoutLine.Controls.Add(new Label { Text = "seconds", AutoSize = true, Margin = new Padding(8, 4, 0, 0) });
            generalFlow.Controls.Add(Row(timeoutLine, "A shorter wait reduces the chance of moving an unrelated app that activates later."));
            timeout.ValueChanged += delegate { if (!syncing) app.ChangeSettings(s => s.LaunchTimeoutSeconds = (int)timeout.Value); };

            var mapCard = Card();
            mapCard.Controls.Add(Heading("Connected displays"), 0, 0);
            displays = new DisplayMap { Dock = DockStyle.Top, Height = 116, Margin = new Padding(0, 8, 0, 0) };
            mapCard.Controls.Add(displays, 0, 1);
            mapCard.Controls.Add(Description("Click an app on the taskbar of the display where you want it to open."), 0, 2);
            var displayActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 0) };
            displayActions.Controls.Add(Button("Identify displays", IdentifyDisplays));
            displayActions.Controls.Add(Button("Taskbar settings", OpenTaskbarSettings));
            mapCard.Controls.Add(displayActions, 0, 3);
            generalFlow.Controls.Add(mapCard);
            var backupCard = Card();
            backupCard.Controls.Add(Heading("Settings backup"), 0, 0);
            backupCard.Controls.Add(Description("Export a local copy of your settings, or restore a previous backup."), 0, 1);
            var backupActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 0) };
            backupActions.Controls.Add(Button("Export settings", ExportSettings));
            backupActions.Controls.Add(Button("Import settings", ImportSettings));
            backupCard.Controls.Add(backupActions, 0, 2);
            generalFlow.Controls.Add(backupCard);

            var appFlow = Flow(apps);
            appFlow.Controls.Add(TextCard("App exceptions", "Leave these apps on their current display. Use this for games or apps that manage their own window positions."));
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
            exceptionNotice = Description("No app exceptions.");
            exceptionNotice.AccessibleName = "Exception list feedback";
            listCard.Controls.Add(exceptionNotice, 0, 4);
            appFlow.Controls.Add(listCard);
            appFlow.Controls.Add(TextCard("Finding the right name", "If you're unsure, open Windows Task Manager and look in the Details tab. Some Windows apps share a host process, so an exception for that host can affect more than one app."));

            var aboutFlow = Flow(about);
            aboutFlow.Controls.Add(TextCard("LaunchHere " + UpdateClient.DisplayVersion(UpdateClient.CurrentVersion), "Windows 10 and 11 · 64-bit\nOpen apps on the monitor where you click their taskbar icon."));
            var updateCard = Card();
            updateCard.Controls.Add(Heading("App updates"), 0, 0);
            updateCard.Controls.Add(Description("Check the latest GitHub release. A newer version is downloaded, verified and applied automatically. LaunchHere closes briefly and reopens after updating."), 0, 1);
            updateStatus = Description("Updates are checked only when you press the button.");
            updateStatus.AccessibleName = "Update status";
            updateCard.Controls.Add(updateStatus, 0, 2);
            updateProgress = new ProgressBar { Dock = DockStyle.Top, Height = 8, Visible = false, Margin = new Padding(0, 12, 0, 4), AccessibleName = "Update download progress" };
            updateCard.Controls.Add(updateProgress, 0, 3);
            var updateActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 0) };
            updateButton = Button("Check for updates", delegate { CheckForUpdates(); });
            updateButton.BackColor = Accent; updateButton.ForeColor = Color.White;
            updateButton.AccessibleName = "Check for updates";
            cancelUpdate = Button("Cancel download", delegate { if (updateCancellation != null) updateCancellation.Cancel(); });
            cancelUpdate.Visible = false;
            updateActions.Controls.Add(updateButton); updateActions.Controls.Add(cancelUpdate);
            updateCard.Controls.Add(updateActions, 0, 4);
            aboutFlow.Controls.Add(updateCard);
            aboutFlow.Controls.Add(TextCard("Privacy", "Internet is used only when you press \"Check for updates\". That action contacts GitHub and its download servers to check and download a release. GitHub receives your IP address and the app version.\n\nNo accounts, analytics or background update checks. Preferences, app exceptions and window activity stay on your PC. Mouse click history and keystrokes are not recorded."));
            aboutFlow.Controls.Add(TextCard("Get started", "1. Show the taskbar on all displays in Windows taskbar settings.\n2. Click an app on your preferred display's taskbar.\n3. Close or minimize this window to keep the app running in the tray."));
            aboutFlow.Controls.Add(TextCard("A few things to know", "Windows does not identify which app a taskbar click launches, so the utility uses the next eligible window activation. Unrelated apps activating soon afterward can sometimes be moved. Apps may briefly appear on their old display; slow launches, administrator apps, and exclusive fullscreen games may need manual movement."));

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(24, 14, 24, 12), Margin = Padding.Empty, BackColor = Color.Transparent };
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
            identifyTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            identifyTimer.Tick += delegate { CloseIdentification(); };
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
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 0, 6, 8), BackColor = Color.Transparent };
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
            var card = new GlassCard { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 14) };
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
        private static CheckBox Check(string text, string name) { return new GlassToggle { Text = text, AccessibleName = name, AutoSize = true, Margin = Padding.Empty }; }
        private static Button Button(string text, Action action)
        {
            var button = new GlassButton { Text = text, AutoSize = true, MinimumSize = new Size(0, 38), BackColor = Color.White, ForeColor = Ink, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 215, 225);
            button.Click += delegate { action(); };
            return button;
        }

        private void AddException()
        {
            string name = AppSettings.ProcessName(processName.Text);
            if (name == null) { exceptionNotice.Text = "Enter a process name, such as game.exe. Paths and wildcards are not supported."; return; }
            if (app.Settings.IsExcluded(name)) { exceptionNotice.Text = "This app is already excluded."; return; }
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
        private void OpenTaskbarSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "Could not open Windows settings: " + ex.Message, "LaunchHere"); }
        }
        private void IdentifyDisplays()
        {
            CloseIdentification();
            int number = 1;
            foreach (var screen in Screen.AllScreens)
            {
                var overlay = new DisplayIdentification { Text = "Display " + number, ClientSize = new Size(220, 130), StartPosition = FormStartPosition.Manual,
                    FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false };
                overlay.Controls.Add(new Label { Text = number++ + (screen.Primary ? "\nPrimary display" : "\nDisplay"), Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent });
                overlay.Location = new Point(screen.WorkingArea.Left + (screen.WorkingArea.Width - overlay.Width) / 2,
                    screen.WorkingArea.Top + (screen.WorkingArea.Height - overlay.Height) / 2);
                identification.Add(overlay); overlay.Show(this);
            }
            identifyTimer.Start();
        }
        private void CloseIdentification()
        {
            if (identifyTimer != null) identifyTimer.Stop();
            foreach (var overlay in identification) { overlay.Close(); overlay.Dispose(); }
            identification.Clear();
        }
        private void ExportSettings()
        {
            using (var dialog = new SaveFileDialog { Title = "Export LaunchHere settings", Filter = "Settings (*.json)|*.json", FileName = "LaunchHere-settings.json", DefaultExt = "json", AddExtension = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { new SettingsStore(dialog.FileName).Save(app.Settings); footer.Text = "Settings exported."; }
                catch (Exception ex) { MessageBox.Show(this, "Could not export settings: " + ex.Message, "LaunchHere", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        internal void ImportSettingsFile(string path)
        {
            string notice;
            var settings = new SettingsStore(path).Load(out notice);
            if (notice != null || !File.Exists(path)) throw new InvalidDataException("This file is not a valid LaunchHere settings backup.");
            app.ChangeSettings(s => {
                s.Enabled = settings.Enabled; s.ThumbnailPreviews = settings.ThumbnailPreviews;
                s.RetryMovement = settings.RetryMovement; s.LaunchTimeoutSeconds = settings.LaunchTimeoutSeconds;
                s.ExcludedApps = new System.Collections.Generic.List<string>(settings.ExcludedApps);
            });
        }
        private void ImportSettings()
        {
            using (var dialog = new OpenFileDialog { Title = "Import LaunchHere settings", Filter = "Settings (*.json)|*.json", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { ImportSettingsFile(dialog.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "Could not import settings: " + ex.Message, "LaunchHere", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void CheckForUpdates()
        {
            // Keep callbacks on this window's thread even if the host has not
            // installed a WinForms synchronization context (for example tests).
            var previous = SynchronizationContext.Current;
            if (previous is WindowsFormsSynchronizationContext) { CheckForUpdatesAsync(); return; }
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                CheckForUpdatesAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        private async void CheckForUpdatesAsync()
        {
            if (updateCancellation != null) return;
            var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            updateCancellation = cancellation;
            updateButton.Enabled = false; cancelUpdate.Visible = true; updateProgress.Visible = true;
            DownloadedUpdate download = null;
            try
            {
                var progress = new Progress<UpdateProgress>(delegate(UpdateProgress value) {
                    if (IsDisposed || updateCancellation != cancellation || cancellation.IsCancellationRequested) return;
                    updateStatus.Text = value.Message; updateProgress.Value = Math.Max(0, Math.Min(100, value.Percent));
                });
                string folder = Path.GetDirectoryName(Application.ExecutablePath);
                bool portable = !UpdateClient.IsInstalled(folder);
                download = await updater.CheckAndDownloadAsync(UpdateClient.CurrentVersion, portable,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchHere", "Updates"), progress, cancellation.Token);
                if (IsDisposed || cancellation.IsCancellationRequested) return;
                if (download == null) updateStatus.Text = "You're up to date. Checked at " + DateTime.Now.ToShortTimeString() + ".";
                if (download != null)
                {
                    updateStatus.Text = "Applying " + UpdateClient.DisplayVersion(download.Version) + ". LaunchHere will reopen shortly…";
                    cancelUpdate.Enabled = false;
                    UpdateClient.Apply(download, folder);
                    download = null; // The helper now owns the payload.
                    app.ExitThread();
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) updateStatus.Text = "Update cancelled. No changes were applied."; }
            catch (Exception ex) { if (!IsDisposed) updateStatus.Text = "Could not update: " + ex.Message; }
            finally
            {
                if (download != null) UpdateClient.DeleteDownload(download.Folder);
                updateCancellation = null; cancellation.Dispose();
                if (!IsDisposed) { updateButton.Enabled = true; cancelUpdate.Visible = false; cancelUpdate.Enabled = true; updateProgress.Visible = false; }
            }
        }
        internal void RefreshState()
        {
            syncing = true;
            try
            {
                enable.Checked = app.Settings.Enabled;
                startup.Checked = app.StartupEnabled;
                startup.Enabled = app.StartupAvailable;
                startupHelp.Text = !app.StartupAvailable ? "Windows has restricted startup settings for this account. The app still works when launched manually." : "Launch quietly in the tray when you sign in. Keep portable copies in a permanent folder.";
                previews.Checked = app.Settings.ThumbnailPreviews;
                retries.Checked = app.Settings.RetryMovement;
                timeout.Value = app.Settings.LaunchTimeoutSeconds;
                badge.Text = app.Settings.Enabled ? "●  Enabled" : "●  Paused";
                badge.BackColor = app.Settings.Enabled ? Color.FromArgb(219, 234, 255) : Color.FromArgb(240, 236, 225);
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
                exceptionNotice.Text = exclusions.Items.Count == 0 ? "No app exceptions." : exclusions.Items.Count + " app(s) excluded. Changes take effect immediately.";
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
                if (updateCancellation != null) updateCancellation.Cancel();
                CloseIdentification();
                if (identifyTimer != null) identifyTimer.Dispose();
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

    internal sealed class DisplayIdentification : GlassForm
    {
        protected override bool ShowWithoutActivation { get { return true; } }
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
                    buttons[i].BackColor = SystemInformation.HighContrast ? i == value ? SystemColors.Highlight : SystemColors.Control : i == value ? Glass.Accent : Color.White;
                    buttons[i].ForeColor = SystemInformation.HighContrast ? i == value ? SystemColors.HighlightText : SystemColors.ControlText : i == value ? Color.White : Glass.Muted;
                }
                pages[value].BringToFront();
            }
        }
        internal SettingsPages()
        {
            BackColor = Color.Transparent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent };
            content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.Controls.Add(navigation, 0, 0);
            layout.Controls.Add(content, 0, 1);
        }
        internal Panel AddPage(string title)
        {
            int index = pages.Count;
            var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), BackColor = Color.Transparent };
            var button = new GlassButton { Text = title, AccessibleName = title.Replace("&&", "&") + " page", AutoSize = true, MinimumSize = new Size(140, 38), Padding = new Padding(16, 4, 16, 4), Margin = new Padding(0, 0, 8, 0), TabIndex = index };
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
            BackColor = Color.FromArgb(240, 246, 255);
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
                Color fill = SystemInformation.HighContrast ? SystemColors.Control : screen.Primary ? Color.FromArgb(216, 233, 255) : Color.FromArgb(237, 243, 253);
                Color stroke = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(106, 139, 189);
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, rect);
                using (var pen = new Pen(stroke, 1.5F)) e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                string label = "Display " + index++ + (screen.Primary ? "\nPrimary" : "");
                TextRenderer.DrawText(e.Graphics, label, Font, Rectangle.Round(rect), SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(25, 39, 58), Glass.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
