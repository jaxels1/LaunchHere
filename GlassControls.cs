using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LaunchHere
{
    internal static class Glass
    {
        internal static readonly Color Ink = Color.FromArgb(235, 231, 242);
        internal static readonly Color Muted = Color.FromArgb(164, 155, 179);
        internal static readonly Color Accent = Color.FromArgb(106, 78, 150);
        internal static readonly Color Cyan = Color.FromArgb(185, 159, 220);
        internal static readonly Color Canvas = Color.FromArgb(10, 10, 12);
        internal static readonly Color CanvasTop = Canvas;
        internal static readonly Color CanvasBottom = Canvas;
        internal static readonly Color CardTop = Color.FromArgb(216, 25, 22, 31);
        internal static readonly Color CardBottom = Color.FromArgb(230, 19, 17, 24);
        internal static readonly Color Input = Color.FromArgb(19, 17, 24);
        internal static readonly Color ButtonTop = Color.FromArgb(45, 37, 57);
        internal static readonly Color ButtonBottom = Color.FromArgb(31, 27, 39);
        internal static readonly Color Disabled = Color.FromArgb(118, 109, 132);
        internal static readonly Color Track = Color.FromArgb(57, 48, 70);
        internal static readonly Color Border = Color.FromArgb(90, 125, 105, 150);
        // Transparent child controls repaint with a translated, clipped Graphics.
        // TextRenderer must preserve both or text can leak into sibling controls.
        internal const TextFormatFlags PaintText = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
        internal static Color Mix(Color from, Color to, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb((int)Math.Round(from.A + (to.A - from.A) * amount),
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }
        internal static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void Surface(Graphics graphics, Rectangle bounds, Color top, Color bottom, float radius)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            using (var path = Rounded(rect, radius))
            using (var gradient = new LinearGradientBrush(rect, top, bottom, 80F))
            using (var rim = new Pen(Color.FromArgb(18, 215, 193, 245), 1F))
            using (var edge = new Pen(Border, 1F))
            {
                graphics.FillPath(gradient, path);
                graphics.DrawPath(edge, path);
                var shine = new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                using (var inner = Rounded(shine, Math.Max(1, radius - 1))) graphics.DrawPath(rim, inner);
            }
        }
        internal static void CanvasBackground(Graphics graphics, Rectangle bounds)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var fill = new SolidBrush(Canvas)) graphics.FillRectangle(fill, bounds);
        }
        internal static void ParentBackground(Graphics graphics, Control control)
        {
            using (var fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Control : Canvas)) graphics.FillRectangle(fill, control.ClientRectangle);
            // Paint only ancestor surfaces, never their child controls. Replaying a
            // parent's native paint would bring back the overlapping-text bug.
            var ancestors = new System.Collections.Generic.List<Control>();
            for (Control parent = control.Parent; parent != null; parent = parent.Parent) ancestors.Add(parent);
            ancestors.Reverse();
            foreach (Control parent in ancestors)
            {
                var offset = Point.Empty;
                for (Control child = control; child != parent; child = child.Parent) offset.Offset(child.Location);
                var state = graphics.Save();
                try
                {
                    graphics.TranslateTransform(-offset.X, -offset.Y);
                    if (SystemInformation.HighContrast)
                    {
                        using (var fill = new SolidBrush(parent.BackColor)) graphics.FillRectangle(fill, parent.ClientRectangle);
                    }
                    else if (parent is GlassForm) ((GlassForm)parent).PaintCanvas(graphics, false);
                    else if (parent is GlassCard) CardBackground(graphics, parent);
                    else if (parent.BackColor.A == 255)
                        using (var fill = new SolidBrush(parent.BackColor)) graphics.FillRectangle(fill, parent.ClientRectangle);
                }
                finally { graphics.Restore(state); }
            }
        }
        internal static void CardBackground(Graphics graphics, Control card)
        {
            var form = card.FindForm() as GlassForm;
            if (form != null)
            {
                var state = graphics.Save();
                try
                {
                    using (var clip = Rounded(new RectangleF(1, 1, Math.Max(1, card.Width - 3), Math.Max(1, card.Height - 3)), 18 * card.DeviceDpi / 96F)) graphics.SetClip(clip, CombineMode.Intersect);
                    var offset = Point.Empty;
                    for (Control child = card; child != form && child != null; child = child.Parent) offset.Offset(child.Location);
                    graphics.TranslateTransform(-offset.X, -offset.Y); form.PaintCanvas(graphics, true);
                }
                finally { graphics.Restore(state); }
            }
            Surface(graphics, card.ClientRectangle, CardTop, CardBottom, 18 * card.DeviceDpi / 96F);
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int length);
        internal static void RoundWindow(IntPtr hwnd)
        {
            if (SystemInformation.HighContrast) return;
            try {
                int preference = 2; DwmSetWindowAttribute(hwnd, 33, ref preference, 4);
                int dark = 1; DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
    // One finite transition, measured in elapsed time rather than timer ticks.
    // Hidden controls finish immediately, so the tray never keeps animation timers running.
    internal sealed class GlassMotion : IDisposable
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfo(uint action, uint parameter, out int result, uint flags);
        internal static bool AnimationsEnabled()
        {
            int enabled;
            return !SystemInformation.HighContrast && SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled != 0;
        }
        private readonly Control owner;
        private readonly Action changed;
        private readonly Func<bool> allowed;
        private readonly Timer timer;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly System.Collections.Generic.List<Control> ancestors = new System.Collections.Generic.List<Control>();
        private double from, target, value;
        private int duration;
        private bool disposed;
        internal double Value { get { return value; } }
        internal bool Running { get { return !disposed && timer.Enabled; } }
        internal GlassMotion(Control owner, double initial, Action changed, Func<bool> allowed = null)
        {
            this.owner = owner; this.changed = changed;
            this.allowed = allowed ?? AnimationsEnabled;
            from = target = value = initial;
            timer = new Timer { Interval = 16 };
            timer.Tick += OnTick;
            owner.VisibleChanged += OnVisibilityChanged;
            owner.ParentChanged += OnParentChanged;
            owner.HandleDestroyed += OnHandleDestroyed;
            owner.Disposed += OnOwnerDisposed;
            WatchAncestors();
        }
        internal void MoveTo(double destination, int milliseconds)
        {
            if (disposed) return;
            if (target == destination && Running) return;
            if (Running) Sample();
            from = value; target = destination; duration = Math.Max(1, milliseconds);
            if (!owner.Visible || !owner.IsHandleCreated || !allowed() || Math.Abs(from - target) < .0001) { Finish(); return; }
            clock.Restart(); timer.Start();
        }
        internal void Snap(double destination)
        {
            if (disposed) return;
            target = destination; Finish();
        }
        internal void Finish()
        {
            if (disposed) return;
            timer.Stop(); clock.Reset(); from = value = target;
            if (!owner.IsDisposed) changed();
        }
        private void Sample()
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            value = from + (target - from) * (1 - Math.Pow(1 - t, 3));
        }
        private void OnTick(object sender, EventArgs e)
        {
            if (!owner.Visible || !owner.IsHandleCreated || !allowed()) { Finish(); return; }
            Sample();
            if (clock.Elapsed.TotalMilliseconds >= duration) Finish(); else changed();
        }
        private void OnVisibilityChanged(object sender, EventArgs e) { if (!owner.Visible) Finish(); }
        private void OnParentChanged(object sender, EventArgs e) { WatchAncestors(); if (!owner.Visible) Finish(); }
        private void WatchAncestors()
        {
            foreach (Control parent in ancestors) { parent.VisibleChanged -= OnVisibilityChanged; parent.ParentChanged -= OnParentChanged; }
            ancestors.Clear();
            for (Control parent = owner.Parent; parent != null; parent = parent.Parent)
            {
                ancestors.Add(parent); parent.VisibleChanged += OnVisibilityChanged; parent.ParentChanged += OnParentChanged;
            }
        }
        private void OnHandleDestroyed(object sender, EventArgs e) { Finish(); }
        private void OnOwnerDisposed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Dispose(); clock.Stop();
            owner.VisibleChanged -= OnVisibilityChanged;
            owner.ParentChanged -= OnParentChanged;
            owner.HandleDestroyed -= OnHandleDestroyed;
            owner.Disposed -= OnOwnerDisposed;
            foreach (Control parent in ancestors) { parent.VisibleChanged -= OnVisibilityChanged; parent.ParentChanged -= OnParentChanged; }
            ancestors.Clear();
        }
    }
    internal sealed class GlassTint : IDisposable
    {
        private readonly GlassMotion motion;
        private Color fromTop, fromBottom, fromText, toTop, toBottom, toText;
        internal Color Top { get { return Glass.Mix(fromTop, toTop, motion.Value); } }
        internal Color Bottom { get { return Glass.Mix(fromBottom, toBottom, motion.Value); } }
        internal Color Text { get { return Glass.Mix(fromText, toText, motion.Value); } }
        internal GlassTint(Control owner, Color top, Color bottom, Color text)
        {
            fromTop = toTop = top; fromBottom = toBottom = bottom; fromText = toText = text;
            motion = new GlassMotion(owner, 1, owner.Invalidate);
        }
        internal void Set(Color top, Color bottom, Color text)
        {
            if (toTop == top && toBottom == bottom && toText == text) return;
            Color currentTop = Top, currentBottom = Bottom, currentText = Text;
            fromTop = currentTop; fromBottom = currentBottom; fromText = currentText;
            toTop = top; toBottom = bottom; toText = text;
            motion.Snap(0); motion.MoveTo(1, 160);
        }
        public void Dispose() { motion.Dispose(); }
    }
    internal class GlassForm : Form
    {
        internal readonly ChevronScene Scene;
        internal GlassForm() { DoubleBuffered = true; BackColor = Glass.Canvas; SetStyle(ControlStyles.ResizeRedraw, true); Scene = new ChevronScene(this); }
        internal void PaintCanvas(Graphics graphics, bool blurred) { Scene.Draw(graphics, blurred); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Glass.RoundWindow(Handle); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            PaintCanvas(e.Graphics, false);
        }
        protected override void Dispose(bool disposing) { if (disposing) Scene.Dispose(); base.Dispose(disposing); }
    }
    internal class GlassCard : TableLayoutPanel
    {
        internal GlassCard()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Glass.ParentBackground(e.Graphics, this);
            if (!SystemInformation.HighContrast)
            Glass.CardBackground(e.Graphics, this);
        }
    }
    internal sealed class GlassButton : Button
    {
        private bool hover, pressed;
        private readonly GlassTint tint;
        private readonly GlassMotion depression;
        internal GlassButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            tint = new GlassTint(this, Glass.ButtonTop, Glass.ButtonBottom, ForeColor);
            depression = new GlassMotion(this, 0, Invalidate);
            RefreshTint();
        }
        private void RefreshTint()
        {
            if (tint == null) return;
            bool primary = BackColor == Glass.Accent;
            Color top = primary ? (hover ? Color.FromArgb(139, 107, 181) : Color.FromArgb(120, 91, 164)) : hover ? Color.FromArgb(63, 50, 79) : Glass.ButtonTop;
            Color bottom = primary ? Glass.Accent : Glass.ButtonBottom;
            if (pressed && Enabled) { top = Glass.Mix(top, Color.Black, .16); bottom = Glass.Mix(bottom, Color.Black, .16); }
            if (!Enabled) { top = Color.FromArgb(27, 24, 33); bottom = top; }
            tint.Set(top, bottom, !Enabled ? Glass.Disabled : primary ? Color.White : ForeColor);
        }
        private void SetPressed(bool value)
        {
            pressed = value && Enabled; RefreshTint();
            if (depression != null) depression.MoveTo(pressed ? 1 : 0, pressed ? 70 : 130);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; RefreshTint(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; SetPressed(false); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetPressed(true); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { SetPressed(false); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) SetPressed(false); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) SetPressed(true); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) SetPressed(false); base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { SetPressed(false); base.OnLostFocus(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) { hover = false; SetPressed(false); } base.OnVisibleChanged(e); }
        protected override void OnBackColorChanged(EventArgs e) { RefreshTint(); base.OnBackColorChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { RefreshTint(); base.OnForeColorChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { hover = false; SetPressed(false); } RefreshTint(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) { if (tint != null) tint.Dispose(); if (depression != null) depression.Dispose(); } base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
            Glass.ParentBackground(e.Graphics, this);
            var surface = ClientRectangle;
            int inset = (int)Math.Round(depression.Value * DeviceDpi / 96F);
            surface.Inflate(-inset, -inset);
            Glass.Surface(e.Graphics, surface, tint.Top, tint.Bottom, 12 * DeviceDpi / 96F);
            var textBounds = Rectangle.Inflate(ClientRectangle, -6, -3); textBounds.Offset(0, inset);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, tint.Text,
                Glass.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }
    internal sealed class GlassToggle : CheckBox
    {
        private readonly GlassMotion position;
        internal GlassToggle()
        {
            BackColor = Glass.Canvas;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            position = new GlassMotion(this, Checked ? 1 : 0, Invalidate);
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            if (position != null) position.MoveTo(Checked ? 1 : 0, 180);
            base.OnCheckedChanged(e);
        }
        protected override void Dispose(bool disposing) { if (disposing && position != null) position.Dispose(); base.Dispose(disposing); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + 54 * DeviceDpi / 96, Math.Max(text.Height + 6, 28 * DeviceDpi / 96));
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            Glass.ParentBackground(e.Graphics, this);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            OnPaintBackground(e);
            if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
            float scale = DeviceDpi / 96F;
            var track = new RectangleF(1, (Height - 22 * scale) / 2, 38 * scale, 22 * scale);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Glass.Rounded(track, 11 * scale))
            using (var fill = new SolidBrush(Glass.Mix(Glass.Track, Glass.Accent, Enabled ? position.Value : 0))) e.Graphics.FillPath(fill, path);
            using (var knob = new SolidBrush(Color.White)) e.Graphics.FillEllipse(knob, track.Left + (float)(3 + 16 * position.Value) * scale, track.Top + 3 * scale, 16 * scale, 16 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(49 * scale), 0, Math.Max(1, Width - (int)(49 * scale)), Height), Enabled ? ForeColor : Glass.Disabled,
                Glass.PaintText | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
    }
    internal sealed class GlassBadge : Label
    {
        private readonly GlassTint tint;
        internal GlassBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            tint = new GlassTint(this, Glass.ButtonTop, BackColor, ForeColor);
        }
        private void RefreshTint() { if (tint != null) tint.Set(Glass.ButtonTop, BackColor, ForeColor); }
        protected override void OnBackColorChanged(EventArgs e) { RefreshTint(); base.OnBackColorChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { RefreshTint(); base.OnForeColorChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing && tint != null) tint.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { Glass.ParentBackground(e.Graphics, this); base.OnPaint(e); return; }
            Glass.ParentBackground(e.Graphics, this);
            Glass.Surface(e.Graphics, ClientRectangle, tint.Top, tint.Bottom, 16 * DeviceDpi / 96F);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, tint.Text,
                Glass.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
    internal sealed class GlassFlow : FlowLayoutPanel
    {
        [StructLayout(LayoutKind.Sequential)] private struct ScrollInfo
        {
            internal int size, mask, min, max;
            internal uint page;
            internal int position, trackPosition;
        }
        [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        internal GlassFlow() { DoubleBuffered = true; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // Layout can redraw the native bar directly through SetScrollInfo,
            // without WM_NCPAINT (for example after a setting changes).
            PaintScrollBar();
        }
        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); PaintScrollBar(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x317 && (message.LParam.ToInt64() & 2) != 0) PaintScrollBar(message.WParam);
            // Keep Windows' scrolling, keyboard input and accessibility; only
            // recolor its nonclient scrollbar to match the surrounding surface.
            switch (message.Msg)
            {
                case 0x85: case 0xF: case 0x5: case 0x115: case 0x20A:
                case 0xA0: case 0xA1: case 0xA2: case 0x2A2:
                    PaintScrollBar(); break;
            }
        }
        private void PaintScrollBar(IntPtr paintDc = default(IntPtr))
        {
            if (SystemInformation.HighContrast || !IsHandleCreated || !VerticalScroll.Visible) return;
            int width = Width - ClientSize.Width;
            if (width <= 0 || Height < 2) return;
            var info = new ScrollInfo { size = Marshal.SizeOf(typeof(ScrollInfo)), mask = 7 };
            if (!GetScrollInfo(Handle, 1, ref info)) return;
            IntPtr dc = paintDc == IntPtr.Zero ? GetWindowDC(Handle) : paintDc;
            if (dc == IntPtr.Zero) return;
            try
            {
                using (var graphics = Graphics.FromHdc(dc))
                {
                    var bar = new Rectangle(ClientSize.Width, 0, width, Height);
                    graphics.SetClip(bar);
                    Glass.ParentBackground(graphics, this);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    int arrow = SystemInformation.VerticalScrollBarArrowHeight;
                    float track = Math.Max(1, Height - 2 * arrow);
                    float thumb = Math.Min(track, Math.Max(26 * DeviceDpi / 96F, track * info.page / Math.Max(1, info.max - info.min + 1)));
                    float position = (track - thumb) * (info.position - info.min) / Math.Max(1, info.max - info.min - (int)info.page + 1);
                    float inset = Math.Max(3, width * .28F);
                    var rect = new RectangleF(bar.Left + inset, arrow + position, Math.Max(2, width - 2 * inset), thumb);
                    using (var path = Glass.Rounded(rect, rect.Width / 2))
                    using (var brush = new SolidBrush(Color.FromArgb(150, 145, 125, 169))) graphics.FillPath(brush, path);
                    float center = bar.Left + width / 2F, delta = 3 * DeviceDpi / 96F;
                    using (var pen = new Pen(Glass.Muted, 1.4F))
                    {
                        graphics.DrawLines(pen, new[] { new PointF(center - delta, arrow / 2F + delta / 2), new PointF(center, arrow / 2F - delta / 2), new PointF(center + delta, arrow / 2F + delta / 2) });
                        graphics.DrawLines(pen, new[] { new PointF(center - delta, Height - arrow / 2F - delta / 2), new PointF(center, Height - arrow / 2F + delta / 2), new PointF(center + delta, Height - arrow / 2F - delta / 2) });
                    }
                }
            }
            finally { if (paintDc == IntPtr.Zero) ReleaseDC(Handle, dc); }
        }
    }
    internal sealed class GlassNumber : NumericUpDown
    {
        private readonly System.Collections.Generic.Dictionary<Control, SpinnerPaint> painters = new System.Collections.Generic.Dictionary<Control, SpinnerPaint>();
        internal GlassNumber()
        {
            foreach (Control child in Controls)
            {
                if (child is TextBoxBase) continue;
                child.HandleCreated += delegate(object sender, EventArgs args) {
                    var button = (Control)sender;
                    SpinnerPaint previous;
                    if (painters.TryGetValue(button, out previous)) previous.ReleaseHandle();
                    painters[button] = new SpinnerPaint(button);
                };
                child.HandleDestroyed += delegate(object sender, EventArgs args) {
                    var button = (Control)sender;
                    SpinnerPaint previous;
                    if (painters.TryGetValue(button, out previous)) { previous.ReleaseHandle(); painters.Remove(button); }
                };
                if (child.IsHandleCreated) painters[child] = new SpinnerPaint(child);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var painter in painters.Values) painter.ReleaseHandle(); painters.Clear(); }
            base.Dispose(disposing);
        }
        private sealed class SpinnerPaint : NativeWindow
        {
            private readonly Control button;
            [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
            [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
            internal SpinnerPaint(Control button) { this.button = button; AssignHandle(button.Handle); }
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != 0xF || SystemInformation.HighContrast || Handle == IntPtr.Zero) return;
                IntPtr dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (var graphics = Graphics.FromHdc(dc))
                    {
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var fill = new SolidBrush(Glass.ButtonBottom)) graphics.FillRectangle(fill, button.ClientRectangle);
                        float middle = button.Height / 2F, center = button.Width / 2F, delta = 3 * button.DeviceDpi / 96F;
                        using (var divider = new Pen(Glass.Track))
                        {
                            graphics.DrawLine(divider, 0, 0, 0, button.Height);
                            graphics.DrawLine(divider, 0, middle, button.Width, middle);
                        }
                        using (var pen = new Pen(button.Enabled ? Glass.Muted : Color.FromArgb(155, 168, 188), 1.4F))
                        {
                            graphics.DrawLines(pen, new[] { new PointF(center - delta, middle / 2 + delta / 2), new PointF(center, middle / 2 - delta / 2), new PointF(center + delta, middle / 2 + delta / 2) });
                            graphics.DrawLines(pen, new[] { new PointF(center - delta, middle * 1.5F - delta / 2), new PointF(center, middle * 1.5F + delta / 2), new PointF(center + delta, middle * 1.5F - delta / 2) });
                        }
                    }
                }
                finally { ReleaseDC(Handle, dc); }
            }
        }
    }
}
