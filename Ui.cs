using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TempMonitor
{
    /// <summary>The app's fonts, sized in pixels for the current DPI.</summary>
    internal sealed class UiFonts : IDisposable
    {
        public readonly Font Caption, Body, BodyStrong, Title, Hero, HeroUnit;

        public UiFonts(float scale)
        {
            Caption = Make("Segoe UI", 12, scale);
            Body = Make("Segoe UI", 14, scale);
            BodyStrong = Make("Segoe UI Semibold", 14, scale);
            Title = Make("Segoe UI Semibold", 19, scale);
            Hero = Make("Segoe UI Semibold", 40, scale);
            HeroUnit = Make("Segoe UI", 17, scale);
        }

        private static Font Make(string family, float px, float scale)
        {
            return new Font(family, px * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            foreach (Font f in new[] { Caption, Body, BodyStrong, Title, Hero, HeroUnit }) f.Dispose();
        }
    }

    /// <summary>A clickable area in a custom-drawn control.</summary>
    internal sealed class Hit
    {
        public readonly Rectangle Rect;
        public readonly string Id;
        public readonly Action Click;
        public Hit(Rectangle rect, string id, Action click) { Rect = rect; Id = id; Click = click; }
    }

    internal static class Draw
    {
        public const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.NoPrefix;

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Color fill)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Color fill, Color border)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, text, font, r, color, Flags | extra);
        }

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Flags);
        }

        /// <summary>Simple line icons for each kind of device, drawn on a 24-unit grid inside <paramref name="r"/>.</summary>
        public static void KindIcon(Graphics g, DeviceKind kind, Rectangle r, Color color, float scale)
        {
            float u = r.Width / 36f;                       // tile is 36 units; icon uses the centre 24
            float ox = r.X + 6 * u, oy = r.Y + 6 * u;
            Func<float, float, PointF> P = (x, y) => new PointF(ox + x * u, oy + y * u);
            Func<float, float, float, float, RectangleF> R = (x, y, w, h) => new RectangleF(ox + x * u, oy + y * u, w * u, h * u);

            using (var pen = new Pen(color, Math.Max(1f, 1.6f * scale)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (kind)
                {
                    case DeviceKind.Processor:
                        using (GraphicsPath p = Rounded(R(5, 5, 14, 14), 2 * u)) g.DrawPath(pen, p);
                        g.FillRectangle(brush, R(9, 9, 6, 6));
                        foreach (float t in new[] { 8.5f, 12f, 15.5f })
                        {
                            g.DrawLine(pen, P(t, 1.5f), P(t, 5));
                            g.DrawLine(pen, P(t, 19), P(t, 22.5f));
                            g.DrawLine(pen, P(1.5f, t), P(5, t));
                            g.DrawLine(pen, P(19, t), P(22.5f, t));
                        }
                        break;

                    case DeviceKind.Graphics:
                        using (GraphicsPath p = Rounded(R(2, 6, 20, 12), 2 * u)) g.DrawPath(pen, p);
                        g.DrawEllipse(pen, R(10.5f, 8.5f, 7, 7));
                        g.FillEllipse(brush, R(13, 11, 2, 2));
                        g.DrawLine(pen, P(5, 9.5f), P(5, 14.5f));
                        g.DrawLine(pen, P(5, 18), P(5, 21));
                        g.DrawLine(pen, P(5, 21), P(10, 21));
                        break;

                    case DeviceKind.Drive:
                        using (GraphicsPath p = Rounded(R(2, 6, 20, 12), 3 * u)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(6, 12), P(13, 12));
                        g.FillEllipse(brush, R(16.5f, 10.5f, 3, 3));
                        break;

                    case DeviceKind.Memory:
                        using (GraphicsPath p = Rounded(R(1.5f, 6, 21, 10), 1.5f * u)) g.DrawPath(pen, p);
                        foreach (float t in new[] { 4.5f, 10.5f, 16.5f }) g.FillRectangle(brush, R(t, 8.5f, 3, 5));
                        foreach (float t in new[] { 4f, 8f, 12f, 16f, 20f }) g.DrawLine(pen, P(t, 16), P(t, 19));
                        break;

                    case DeviceKind.Motherboard:
                        using (GraphicsPath p = Rounded(R(3, 3, 18, 18), 2 * u)) g.DrawPath(pen, p);
                        RectangleF chip = R(6.5f, 6.5f, 6, 6);
                        g.DrawRectangle(pen, chip.X, chip.Y, chip.Width, chip.Height);
                        g.DrawLine(pen, P(15, 7), P(18, 7));
                        g.DrawLine(pen, P(15, 10), P(18, 10));
                        g.DrawLine(pen, P(6.5f, 16), P(17.5f, 16));
                        break;

                    default:
                        g.DrawEllipse(pen, R(4, 4, 16, 16));
                        g.DrawLine(pen, P(12, 8), P(12, 13));
                        g.FillEllipse(brush, R(11, 15, 2, 2));
                        break;
                }
            }
        }
    }

    /// <summary>Menu colours that follow the app theme.</summary>
    internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme _t;

        public ThemedMenuRenderer(Theme t) : base(new Colors(t))
        {
            _t = t;
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // Drawn here rather than via the colour table: on .NET Framework the professional
            // renderer can fall back to its built-in light-blue selection colours.
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(Point.Empty, e.Item.Size);
            r.Inflate(-3, -1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRounded(e.Graphics, r, 4, _t.Hover);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _t.Text : _t.SubText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(_t.Text, Math.Max(1.5f, r.Height / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
                    new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
                    new PointF(r.X + r.Width * 0.80f, r.Y + r.Height * 0.30f),
                });
            }
        }

        private sealed class Colors : ProfessionalColorTable
        {
            private readonly Theme _t;
            public Colors(Theme t) { _t = t; UseSystemColors = false; }

            public override Color ToolStripDropDownBackground { get { return _t.Card; } }
            public override Color ImageMarginGradientBegin { get { return _t.Card; } }
            public override Color ImageMarginGradientMiddle { get { return _t.Card; } }
            public override Color ImageMarginGradientEnd { get { return _t.Card; } }
            public override Color MenuBorder { get { return _t.CardBorder; } }
            public override Color MenuItemBorder { get { return _t.Hover; } }
            public override Color MenuItemSelected { get { return _t.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return _t.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return _t.Hover; } }
            public override Color SeparatorDark { get { return _t.CardBorder; } }
            public override Color SeparatorLight { get { return _t.CardBorder; } }
            public override Color CheckBackground { get { return Color.Transparent; } }
            public override Color CheckSelectedBackground { get { return Color.Transparent; } }
            public override Color CheckPressedBackground { get { return Color.Transparent; } }
        }
    }
}
