using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LaunchHere
{
    // Watches only messages addressed to this window. Nothing is logged or stored.
    internal sealed class ChevronScene : IDisposable, IMessageFilter
    {
        private readonly GlassForm owner;
        private readonly Timer timer = new Timer { Interval = 33 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<Wave> waves = new List<Wave>();
        private Bitmap baseline, frame, frost, thumbnail;
        private PointF pointer = new PointF(-1000, -1000), target = new PointF(-1000, -1000);
        private double glow, lastMove;
        private bool dirty = true, active, disposed;
        private sealed class Wave { internal PointF Center; internal double Started; }
        internal bool Running { get { return !disposed && timer.Enabled; } }
        internal int RippleCount { get { return waves.Count; } }
        internal ChevronScene(GlassForm owner)
        {
            this.owner = owner;
            Application.AddMessageFilter(this);
            owner.Activated += Activated; owner.Deactivate += Deactivated;
            owner.VisibleChanged += Visibility; owner.SizeChanged += Resize;
            timer.Tick += Tick;
        }
        private bool Allowed { get { return !disposed && owner.Visible && owner.IsHandleCreated && active && owner.WindowState != FormWindowState.Minimized && GlassMotion.AnimationsEnabled(); } }
        private void Activated(object sender, EventArgs e) { active = true; }
        private void Deactivated(object sender, EventArgs e) { active = false; Stop(); }
        private void Visibility(object sender, EventArgs e) { if (!owner.Visible) Stop(); }
        private void Resize(object sender, EventArgs e) { ClearImages(); dirty = true; }
        private void Stop()
        {
            timer.Stop(); waves.Clear(); glow = 0; pointer = target = new PointF(-1000, -1000); dirty = true;
            if (!owner.IsDisposed) owner.Invalidate(true);
        }
        internal void MovePointer(Point location)
        {
            if (!Allowed) return;
            target = location; if (glow == 0) pointer = target;
            lastMove = clock.Elapsed.TotalSeconds; timer.Start(); dirty = true;
        }
        internal void Ripple(Point location)
        {
            if (!Allowed) return;
            if (waves.Count == 4) waves.RemoveAt(0);
            waves.Add(new Wave { Center = location, Started = clock.Elapsed.TotalSeconds });
            timer.Start(); dirty = true;
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !owner.Visible) return false;
            var control = Control.FromChildHandle(message.HWnd);
            if (control == null || control.FindForm() != owner) return false;
            if (message.Msg == 0x200 || message.Msg == 0x201)
            {
                long xy = message.LParam.ToInt64();
                var local = new Point(unchecked((short)(xy & 0xffff)), unchecked((short)((xy >> 16) & 0xffff)));
                var position = owner.PointToClient(control.PointToScreen(local));
                MovePointer(position); if (message.Msg == 0x201) Ripple(position);
            }
            else if (message.Msg == 0x101 && (message.WParam.ToInt32() == (int)Keys.Space || message.WParam.ToInt32() == (int)Keys.Enter))
                Ripple(owner.PointToClient(control.PointToScreen(new Point(control.Width / 2, control.Height / 2))));
            return false;
        }
        private void Tick(object sender, EventArgs e)
        {
            if (!Allowed) { Stop(); return; }
            double now = clock.Elapsed.TotalSeconds;
            waves.RemoveAll(w => now - w.Started > 1.15);
            pointer = new PointF(pointer.X + (target.X - pointer.X) * .24F, pointer.Y + (target.Y - pointer.Y) * .24F);
            bool moving = now - lastMove < .12;
            glow += ((moving ? 1 : 0) - glow) * .17;
            if (!moving && glow < .015 && waves.Count == 0) { glow = 0; timer.Stop(); }
            dirty = true; owner.Invalidate(true);
        }
        internal void Draw(Graphics graphics, bool blurred)
        {
            if (disposed || owner.ClientSize.Width < 2 || owner.ClientSize.Height < 2) return;
            EnsureFrame();
            var state = graphics.Save();
            try
            {
                graphics.DrawImageUnscaled(blurred ? frost : frame, Point.Empty);
            }
            finally { graphics.Restore(state); }
        }
        private void EnsureFrame()
        {
            int width = owner.ClientSize.Width, height = owner.ClientSize.Height;
            if (baseline == null || baseline.Width != width || baseline.Height != height)
            {
                ClearImages(); baseline = new Bitmap(width, height); frame = new Bitmap(width, height);
                frost = new Bitmap(width, height);
                thumbnail = new Bitmap(Math.Max(2, width / 18), Math.Max(2, height / 18));
                using (var graphics = Graphics.FromImage(baseline))
                {
                    Glass.CanvasBackground(graphics, owner.ClientRectangle);
                    DrawGrid(graphics, false);
                }
                dirty = true;
            }
            if (!dirty) return;
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.DrawImageUnscaled(baseline, Point.Empty);
                if (!SystemInformation.HighContrast && GlassMotion.AnimationsEnabled() && (glow > 0 || waves.Count > 0)) DrawGrid(graphics, true);
            }
            // Blur once per scene frame. Every transparent child can then replay
            // this cached image without resampling the whole window again.
            using (var graphics = Graphics.FromImage(thumbnail))
            { graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; graphics.DrawImage(frame, new Rectangle(Point.Empty, thumbnail.Size)); }
            using (var graphics = Graphics.FromImage(frost))
            { graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; graphics.DrawImage(thumbnail, new Rectangle(Point.Empty, frost.Size)); }
            dirty = false;
        }
        private void DrawGrid(Graphics graphics, bool reactive)
        {
            if (SystemInformation.HighContrast) return;
            float scale = owner.DeviceDpi / 96F, spacing = 28 * scale;
            double now = clock.Elapsed.TotalSeconds;
            using (var font = new Font("Consolas", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var quiet = new SolidBrush(Color.FromArgb(36, 33, 45)))
            {
                for (float y = 14 * scale; y < owner.Height; y += spacing)
                for (float x = 14 * scale; x < owner.Width; x += spacing)
                {
                    if (!reactive) { graphics.DrawString(">", font, quiet, x, y, StringFormat.GenericTypographic); continue; }
                    double dx = x - pointer.X, dy = y - pointer.Y, distance = Math.Sqrt(dx * dx + dy * dy);
                    double strength = Math.Max(0, 1 - distance / (145 * scale)) * glow, shiftX = 0, shiftY = 0;
                    if (distance > 1) { shiftX = dx / distance * strength * 5 * scale; shiftY = dy / distance * strength * 5 * scale; }
                    foreach (var wave in waves)
                    {
                        double age = now - wave.Started;
                        double wx = x - wave.Center.X, wy = y - wave.Center.Y, radius = Math.Sqrt(wx * wx + wy * wy);
                        double band = Math.Max(0, 1 - Math.Abs(radius - age * 420 * scale) / (45 * scale)) * Math.Max(0, 1 - age / 1.15);
                        strength = Math.Min(1, strength + band);
                        if (radius > 1) { shiftX += wx / radius * band * 7 * scale; shiftY += wy / radius * band * 7 * scale; }
                    }
                    if (strength < .01) continue;
                    // Restore this cell before shifting its glyph, avoiding a doubled character.
                    using (var ink = new SolidBrush(Glass.Canvas)) graphics.FillRectangle(ink, x - 9 * scale, y - 9 * scale, 27 * scale, 27 * scale);
                    using (var halo = new SolidBrush(Color.FromArgb((int)(strength * 20), 178, 145, 222)))
                        graphics.FillEllipse(halo, x - 7 * scale, y - 8 * scale, 22 * scale, 22 * scale);
                    using (var ink = new SolidBrush(Glass.Mix(Color.FromArgb(36, 33, 45), Glass.Cyan, strength)))
                        graphics.DrawString(">", font, ink, x + (float)shiftX, y + (float)shiftY, StringFormat.GenericTypographic);
                }
            }
        }
        private void ClearImages()
        {
            if (baseline != null) baseline.Dispose(); if (frame != null) frame.Dispose(); if (frost != null) frost.Dispose();
            if (thumbnail != null) thumbnail.Dispose(); baseline = frame = frost = thumbnail = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; timer.Dispose(); waves.Clear(); ClearImages();
            Application.RemoveMessageFilter(this); owner.Activated -= Activated; owner.Deactivate -= Deactivated;
            owner.VisibleChanged -= Visibility; owner.SizeChanged -= Resize;
        }
    }

    internal sealed class LauncherDeck : GlassCard
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string text);
        internal sealed class Command { internal string Name, Detail; internal Action Run; internal Action Remove; }
        private readonly TextBox search;
        private readonly Label hint;
        private readonly FlowLayoutPanel tiles;
        private readonly List<Command> commands = new List<Command>();
        private readonly List<Command> matches = new List<Command>();
        private readonly Action add;
        private readonly Font tileFont = new Font("Segoe UI", 9F);
        private string signature;
        private int start;
        private bool pendingLayout;
        internal TextBox Search { get { return search; } }
        internal int MatchCount { get { return matches.Count; } }
        internal LauncherDeck(Action add)
        {
            this.add = add;
            ColumnCount = 1; RowCount = 3; Padding = new Padding(18, 12, 18, 10);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); RowStyles.Add(new RowStyle(SizeType.Percent, 100)); RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            var line = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27)); line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            line.Controls.Add(new Label { Text = ">", ForeColor = Glass.Cyan, Font = new Font("Consolas", 17F), Dock = DockStyle.Fill, Margin = Padding.Empty });
            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = Glass.Input, ForeColor = Glass.Ink, Font = new Font("Segoe UI", 13F), Dock = DockStyle.Top, Margin = new Padding(0, 2, 8, 0), AccessibleName = "Search apps and commands" };
            search.HandleCreated += delegate { SendMessage(search.Handle, 0x1501, new IntPtr(1), "Search apps and commands..."); };
            line.Controls.Add(search, 1, 0);
            line.Controls.Add(new Label { Text = "CTRL K", ForeColor = Glass.Muted, Font = new Font("Consolas", 8F), TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, Margin = Padding.Empty }, 2, 0);
            Controls.Add(line, 0, 0);
            tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, BackColor = Color.Transparent, Margin = new Padding(0, 7, 0, 5), AccessibleName = "Quick launch apps and commands" };
            Controls.Add(tiles, 0, 1);
            hint = new Label { Text = "Search apps and commands · Enter to run · Right-click a pinned app to unpin", Font = new Font("Consolas", 8F), ForeColor = Glass.Muted, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty, AccessibleName = "Launcher feedback" };
            Controls.Add(hint, 0, 2);
            search.TextChanged += delegate { start = 0; Render(); };
            tiles.SizeChanged += delegate
            {
                if (pendingLayout || !IsHandleCreated) return;
                pendingLayout = true;
                BeginInvoke((Action)delegate { pendingLayout = false; if (!IsDisposed) Render(); });
            };
            search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { if (matches.Count > 0) matches[0].Run(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { search.Clear(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Down && tiles.Controls.Count > 0) { tiles.Controls[0].Focus(); e.SuppressKeyPress = true; }
            };
        }
        internal void SetCommands(IEnumerable<Command> items, string newSignature)
        {
            if (signature == newSignature) return;
            signature = newSignature; commands.Clear(); commands.AddRange(items); start = 0; Render();
        }
        public override Size GetPreferredSize(Size proposedSize) { return new Size(1, 148 * DeviceDpi / 96); }
        private void Render()
        {
            tiles.SuspendLayout();
            try
            {
                while (tiles.Controls.Count > 0) { Control old = tiles.Controls[0]; tiles.Controls.Remove(old); old.Dispose(); }
                matches.Clear();
                foreach (var command in commands)
                {
                    if (search.Text.Length > 0 && (command.Name + " " + command.Detail).IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    matches.Add(command);
                }
                int capacity = Math.Max(3, (tiles.ClientSize.Width + 8 * DeviceDpi / 96) / (146 * DeviceDpi / 96));
                bool paging = matches.Count > capacity - 1;
                int count = capacity - (paging ? 2 : 1);
                if (start >= matches.Count) start = 0;
                for (int index = start; index < Math.Min(matches.Count, start + count); index++)
                {
                    var command = matches[index]; var captured = command;
                    var tile = Tile(command.Name.Replace("&", "&&") + "\n" + command.Detail, delegate { captured.Run(); });
                    tile.AccessibleName = command.Name;
                    if (command.Remove != null)
                    {
                        var menu = new ContextMenuStrip(); menu.Items.Add("Unpin app", null, delegate { captured.Remove(); });
                        tile.ContextMenuStrip = menu; tile.Disposed += delegate { menu.Dispose(); };
                    }
                    AddTile(tile);
                }
                if (paging)
                {
                    var next = Tile("Next tiles >\n" + (start + 1) + "–" + Math.Min(matches.Count, start + count) + " of " + matches.Count, delegate
                    {
                        start = start + count >= matches.Count ? 0 : start + count; Render();
                        foreach (Control tile in tiles.Controls) if (tile.AccessibleName == "Next launcher tiles") tile.Focus();
                    });
                    next.AccessibleName = "Next launcher tiles"; AddTile(next);
                }
                AddTile(Tile("+ Pin app\nLocal .exe", add));
                hint.Text = search.Text.Length == 0 ? "Search apps and commands · Enter to run · Right-click an app to unpin" : matches.Count == 0 ? "No matches. Pin an app to add it here." : "Enter: " + matches[0].Name + " · Down: select a tile · Esc: clear";
            }
            finally { tiles.ResumeLayout(); }
        }
        private void AddTile(GlassButton tile)
        {
            tiles.Controls.Add(tile);
            // A new child is scaled as it joins a DPI-aware parent. Set its final
            // physical size afterward so dynamic tiles are not scaled twice.
            tile.Size = new Size(138 * DeviceDpi / 96, 50 * DeviceDpi / 96);
            tile.Margin = new Padding(0, 0, 8 * DeviceDpi / 96, 0);
        }
        private GlassButton Tile(string title, Action action)
        {
            var tile = new GlassButton { Text = title, Size = new Size(138, 50), Margin = new Padding(0, 0, 8, 0), BackColor = Glass.ButtonBottom, ForeColor = Glass.Ink, Font = tileFont, Cursor = Cursors.Hand };
            tile.Click += delegate { action(); }; return tile;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) tileFont.Dispose(); }
    }
}
