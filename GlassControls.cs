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
            using (var edge = new Pen(Color.FromArgb(42, 66, 103, 155), 1F))
            {
                graphics.FillPath(gradient, path);
                graphics.DrawPath(edge, path);
                var shine = new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                using (var inner = Rounded(shine, Math.Max(1, radius - 1))) graphics.DrawPath(rim, inner);
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
        internal GlassForm() { DoubleBuffered = true; BackColor = Color.FromArgb(226, 237, 252); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Glass.RoundWindow(Handle); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaintBackground(e); return; }
            using (var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(229, 242, 255), Color.FromArgb(232, 226, 250), 40F)) e.Graphics.FillRectangle(fill, ClientRectangle);
            var orb = new Rectangle(-Width / 3, -Height / 2, Width, Height);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(orb);
                using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(165, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } }) e.Graphics.FillPath(glow, path);
            }
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
            base.OnPaintBackground(e);
            if (!SystemInformation.HighContrast)
                Glass.Surface(e.Graphics, ClientRectangle, Color.FromArgb(235, 255, 255, 255), Color.FromArgb(195, 241, 246, 255), 18 * DeviceDpi / 96F);
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
            using (var background = new SolidBrush(Parent == null ? Color.FromArgb(226, 237, 252) : Parent.BackColor.A == 0 ? Color.FromArgb(235, 241, 251) : Parent.BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            bool primary = BackColor == Glass.Accent;
            Color top = primary ? (hover ? Color.FromArgb(75, 131, 245) : Color.FromArgb(66, 121, 237)) : hover ? Color.White : Color.FromArgb(248, 252, 255);
            Color bottom = primary ? Glass.Accent : Color.FromArgb(218, 230, 249);
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
            BackColor = Color.FromArgb(235, 241, 251);
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
            // CheckBox's themed transparent background uses DrawThemeParentBackground,
            // which can replay other controls into this small buffered surface.
            // Paint the card surface directly instead of using that native path.
            using (var background = new SolidBrush(Color.FromArgb(235, 241, 251)))
                e.Graphics.FillRectangle(background, ClientRectangle);
            if (Parent is GlassCard)
            {
                var state = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Left, -Top);
                    Glass.Surface(e.Graphics, Parent.ClientRectangle, Color.FromArgb(235, 255, 255, 255), Color.FromArgb(195, 241, 246, 255), 18 * DeviceDpi / 96F);
                }
                finally { e.Graphics.Restore(state); }
            }
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
}
