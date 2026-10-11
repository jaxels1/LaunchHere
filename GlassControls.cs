using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LaunchHere
{
    internal static class Glass
    {
        internal static readonly Color Ink = Color.FromArgb(24, 40, 66);
        internal static readonly Color Muted = Color.FromArgb(79, 97, 122);
        internal static readonly Color Accent = Color.FromArgb(45, 101, 219);
        internal static readonly Color Canvas = Color.FromArgb(235, 242, 252);
        internal static readonly Color CanvasTop = Color.FromArgb(232, 243, 255);
        internal static readonly Color CanvasBottom = Color.FromArgb(231, 233, 250);
        internal static readonly Color CardTop = Color.FromArgb(235, 255, 255, 255);
        internal static readonly Color CardBottom = Color.FromArgb(195, 246, 249, 255);
        internal static readonly Color Border = Color.FromArgb(55, 111, 141, 186);
        // Transparent child controls repaint with a translated, clipped Graphics.
        // TextRenderer must preserve both or text can leak into sibling controls.
        internal const TextFormatFlags PaintText = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
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
            using (var rim = new Pen(Color.FromArgb(235, 255, 255, 255), 1.3F))
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
            using (var fill = new LinearGradientBrush(bounds, CanvasTop, CanvasBottom, 40F)) graphics.FillRectangle(fill, bounds);
            var orb = new Rectangle(-bounds.Width / 3, -bounds.Height / 2, bounds.Width, bounds.Height);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(orb);
                using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(120, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } }) graphics.FillPath(glow, path);
            }
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
                    else if (parent is GlassForm) CanvasBackground(graphics, parent.ClientRectangle);
                    else if (parent is GlassCard) Surface(graphics, parent.ClientRectangle, CardTop, CardBottom, 18 * parent.DeviceDpi / 96F);
                    else if (parent.BackColor.A == 255)
                        using (var fill = new SolidBrush(parent.BackColor)) graphics.FillRectangle(fill, parent.ClientRectangle);
                }
                finally { graphics.Restore(state); }
            }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int length);
        internal static void RoundWindow(IntPtr hwnd)
        {
            if (SystemInformation.HighContrast) return;
            try { int preference = 2; DwmSetWindowAttribute(hwnd, 33, ref preference, 4); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
    internal class GlassForm : Form
    {
        internal GlassForm() { DoubleBuffered = true; BackColor = Glass.Canvas; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Glass.RoundWindow(Handle); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            Glass.CanvasBackground(e.Graphics, ClientRectangle);
        }
    }
    internal sealed class GlassCard : TableLayoutPanel
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
                Glass.Surface(e.Graphics, ClientRectangle, Glass.CardTop, Glass.CardBottom, 18 * DeviceDpi / 96F);
        }
    }
    internal sealed class GlassButton : Button
    {
        private bool hover;
        internal GlassButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
            Glass.ParentBackground(e.Graphics, this);
            bool primary = BackColor == Glass.Accent;
            Color top = primary ? (hover ? Color.FromArgb(75, 131, 245) : Color.FromArgb(66, 121, 237)) : hover ? Color.White : Color.FromArgb(248, 252, 255);
            Color bottom = primary ? Glass.Accent : Color.FromArgb(232, 240, 252);
            if (!Enabled) { top = Color.FromArgb(235, 240, 248); bottom = top; }
            Glass.Surface(e.Graphics, ClientRectangle, top, bottom, 12 * DeviceDpi / 96F);
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -6, -3),
                !Enabled ? Glass.Muted : primary ? Color.White : ForeColor,
                Glass.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }
    internal sealed class GlassToggle : CheckBox
    {
        internal GlassToggle()
        {
            BackColor = Glass.Canvas;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }
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
            using (var fill = new SolidBrush(Checked && Enabled ? Glass.Accent : Color.FromArgb(187, 202, 226))) e.Graphics.FillPath(fill, path);
            using (var knob = new SolidBrush(Color.White)) e.Graphics.FillEllipse(knob, track.Left + (Checked ? 19 : 3) * scale, track.Top + 3 * scale, 16 * scale, 16 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(49 * scale), 0, Math.Max(1, Width - (int)(49 * scale)), Height), Enabled ? ForeColor : Glass.Muted,
                Glass.PaintText | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
    }
    internal sealed class GlassBadge : Label
    {
        internal GlassBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { Glass.ParentBackground(e.Graphics, this); base.OnPaint(e); return; }
            Glass.ParentBackground(e.Graphics, this);
            Glass.Surface(e.Graphics, ClientRectangle, Color.FromArgb(245, 251, 255), BackColor, 16 * DeviceDpi / 96F);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
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
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // Keep Windows' scrolling, keyboard input and accessibility; only
            // recolor its nonclient scrollbar to match this light surface.
            switch (message.Msg)
            {
                case 0x85: case 0xF: case 0x5: case 0x115: case 0x20A:
                case 0xA0: case 0xA1: case 0xA2: case 0x2A2:
                    PaintScrollBar(); break;
            }
        }
        private void PaintScrollBar()
        {
            if (SystemInformation.HighContrast || !IsHandleCreated || !VerticalScroll.Visible) return;
            int width = Width - ClientSize.Width;
            if (width <= 0 || Height < 2) return;
            var info = new ScrollInfo { size = Marshal.SizeOf(typeof(ScrollInfo)), mask = 7 };
            if (!GetScrollInfo(Handle, 1, ref info)) return;
            IntPtr dc = GetWindowDC(Handle);
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
                    using (var brush = new SolidBrush(Color.FromArgb(150, 121, 145, 179))) graphics.FillPath(brush, path);
                    float center = bar.Left + width / 2F, delta = 3 * DeviceDpi / 96F;
                    using (var pen = new Pen(Color.FromArgb(136, 157, 187), 1.4F))
                    {
                        graphics.DrawLines(pen, new[] { new PointF(center - delta, arrow / 2F + delta / 2), new PointF(center, arrow / 2F - delta / 2), new PointF(center + delta, arrow / 2F + delta / 2) });
                        graphics.DrawLines(pen, new[] { new PointF(center - delta, Height - arrow / 2F - delta / 2), new PointF(center, Height - arrow / 2F + delta / 2), new PointF(center + delta, Height - arrow / 2F - delta / 2) });
                    }
                }
            }
            finally { ReleaseDC(Handle, dc); }
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
                        using (var fill = new SolidBrush(Color.FromArgb(239, 245, 255))) graphics.FillRectangle(fill, button.ClientRectangle);
                        float middle = button.Height / 2F, center = button.Width / 2F, delta = 3 * button.DeviceDpi / 96F;
                        using (var divider = new Pen(Color.FromArgb(218, 228, 244)))
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
