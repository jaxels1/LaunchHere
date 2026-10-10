using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class IconBuilder
{
    static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    static void Main(string[] args)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var data = new List<byte[]>();
        foreach (int size in sizes)
        using (var bitmap = new Bitmap(size, size))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var memory = new MemoryStream())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 256F, size / 256F);
            using (var path = Rounded(6, 6, 244, 244, 52))
            using (var fill = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(111, 172, 255), Color.FromArgb(37, 77, 190), 65F))
            using (var edge = new Pen(Color.FromArgb(185, 224, 255), 3))
            { graphics.FillPath(fill, path); graphics.DrawPath(edge, path); }
            using (var path = Rounded(42, 52, 172, 119, 15))
            using (var fill = new LinearGradientBrush(new Rectangle(42, 52, 172, 119), Color.White, Color.FromArgb(197, 225, 255), 85F))
            using (var edge = new Pen(Color.White, 5))
            { graphics.FillPath(fill, path); graphics.DrawPath(edge, path); }
            using (var white = new Pen(Color.FromArgb(236, 248, 255), 11))
            {
                white.StartCap = white.EndCap = LineCap.Round;
                graphics.DrawLine(white, 128, 179, 128, 200);
                graphics.DrawLine(white, 94, 205, 162, 205);
            }
            using (var arrow = new Pen(Color.FromArgb(47, 100, 216), 12))
            {
                arrow.StartCap = arrow.EndCap = LineCap.Round;
                graphics.DrawLine(arrow, 85, 111, 169, 111);
                graphics.DrawLine(arrow, 146, 87, 171, 111);
                graphics.DrawLine(arrow, 146, 136, 171, 111);
            }
            bitmap.Save(memory, ImageFormat.Png); data.Add(memory.ToArray());
        }
        using (var stream = File.Create(args[0]))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(data[i].Length); writer.Write(offset); offset += data[i].Length;
            }
            foreach (var item in data) writer.Write(item);
        }
    }
}
