using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace TempMonitor
{
    /// <summary>A highlighted message card shown above the device cards (e.g. "install PawnIO").</summary>
    internal sealed class Notice
    {
        public string Title, Body, Button;
        public bool ButtonEnabled = true;
    }

    /// <summary>
    /// Scrollable, custom-drawn list of device cards. Each card shows a large headline
    /// temperature with a status label and a 2-minute trend line, plus a bar per extra sensor.
    /// </summary>
    internal sealed class DashboardView : ScrollableControl
    {
        public event Action<string> CardToggled;   // card key
        public event Action NoticeClicked;

        private const float BarMinC = 20f, BarMaxC = 100f;

        private readonly float _scale;
        private readonly HashSet<string> _collapsed;
        private Theme _theme;
        private UiFonts _fonts;
        private List<DeviceCard> _cards = new List<DeviceCard>();
        private Notice _notice;
        private bool _fahrenheit;
        private string _hover;
        private readonly List<Hit> _hits = new List<Hit>();

        public DashboardView(float scale, HashSet<string> collapsed)
        {
            _scale = scale;
            _collapsed = collapsed;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
        }

        public void SetTheme(Theme theme, UiFonts fonts)
        {
            _theme = theme;
            _fonts = fonts;
            BackColor = theme.Background;
            if (IsHandleCreated) Theme.ApplyScrollbars(Handle, theme.Dark);
            Relayout();
        }

        public void SetCards(List<DeviceCard> cards) { _cards = cards; Relayout(); }

        /// <summary>Shown on cards whose sensors all have no value, e.g. "Needs administrator rights".</summary>
        public string UnavailableReason = "No readings from this device";

        /// <summary>Total height of the content at a given width, for rendering everything at once.</summary>
        public int ContentHeight(int width) { return _theme == null ? 0 : Arrange(null, width); }

        /// <summary>Draws the full content (not just the visible part) at the graphics origin.</summary>
        public void RenderAll(Graphics g, int width)
        {
            g.SmoothingMode = SmoothingMode.None; // a crisp background edge, no anti-aliased seam
            using (var b = new SolidBrush(_theme.Background))
                g.FillRectangle(b, 0, 0, width, Arrange(null, width));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Arrange(g, width);
        }
        public void SetNotice(Notice notice) { _notice = notice; Relayout(); }
        public bool Fahrenheit { set { _fahrenheit = value; Invalidate(); } }

        public void ToggleCollapsed(string key)
        {
            if (!_collapsed.Remove(key)) _collapsed.Add(key);
            Relayout();
        }

        private void Relayout()
        {
            if (_theme == null) return;
            var size = new Size(0, Arrange(null, ClientSize.Width));
            if (AutoScrollMinSize != size) AutoScrollMinSize = size;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_theme != null) Theme.ApplyScrollbars(Handle, _theme.Dark);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_theme == null) return;
            Graphics g = e.Graphics;
            g.Clear(_theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            _hits.Clear();
            Arrange(g, ClientSize.Width);
        }

        // ---------- layout + drawing (g == null measures only) ----------

        private int Arrange(Graphics g, int width)
        {
            int m = Px(16);
            int x = Px(16), w = Math.Max(Px(260), width - 2 * x);
            int y = Px(4);

            if (_notice != null)
                y += DrawNotice(g, _notice, x, y, w) + Px(12);

            if (_cards.Count == 0)
            {
                if (g != null)
                    Draw.Text(g, "Looking for temperature sensors…", _fonts.Body, new Rectangle(x, y + Px(24), w, Px(24)),
                        _theme.SubText, TextFormatFlags.HorizontalCenter);
                y += Px(72);
            }

            foreach (DeviceCard card in _cards)
                y += DrawCard(g, card, x, y, w) + Px(12);

            return y + m;
        }

        private int DrawCard(Graphics g, DeviceCard c, int x, int y, int w)
        {
            int p = Px(18);
            int headerH = Px(36), heroH = Px(66), rowH = Px(30);
            bool hasData = c.Primary.Value.HasValue || c.Others.Exists(s => s.Value.HasValue);
            bool expandable = hasData && c.Others.Count > 0;
            bool expanded = expandable && !_collapsed.Contains(c.Key);
            int detailsH = expanded ? Px(12) + Px(6) + rowH * c.Others.Count : 0;
            int h = hasData
                ? p + headerH + Px(16) + heroH + detailsH + p - Px(4)
                : p + headerH + Px(12) + Px(20) + p;
            if (g == null) return h;

            var card = new Rectangle(x, y, w, h);
            Draw.FillRounded(g, card, Px(10), _theme.Card, _theme.CardBorder);

            // header: icon tile, kind + name, collapse chevron
            var tile = new Rectangle(x + p, y + p, headerH, headerH);
            Draw.FillRounded(g, tile, Px(8), _theme.Track);
            Draw.KindIcon(g, c.Kind, tile, _theme.Text, _scale);

            int tx = tile.Right + Px(12);
            int tw = x + w - p - tx - (expandable ? Px(36) : 0);
            Draw.Text(g, c.KindLabel, _fonts.Caption, new Rectangle(tx, y + p - Px(1), tw, Px(16)), _theme.SubText, TextFormatFlags.EndEllipsis);
            Draw.Text(g, c.Name, _fonts.BodyStrong, new Rectangle(tx, y + p + Px(16), tw, Px(20)), _theme.Text, TextFormatFlags.EndEllipsis);

            if (expandable)
            {
                string id = "card:" + c.Key;
                var chevronBox = new Rectangle(x + w - p - Px(32), y + p + (headerH - Px(32)) / 2, Px(32), Px(32));
                if (_hover == id) Draw.FillRounded(g, chevronBox, Px(6), _theme.Hover);
                DrawChevron(g, chevronBox, expanded);
                _hits.Add(new Hit(new Rectangle(x, y, w, p + headerH + Px(8)), id, () => { if (CardToggled != null) CardToggled(c.Key); }));
            }

            if (!hasData)
            {
                Draw.Text(g, UnavailableReason, _fonts.Body, new Rectangle(x + p, y + p + headerH + Px(12), w - 2 * p, Px(20)),
                    _theme.SubText, TextFormatFlags.EndEllipsis);
                return h;
            }

            // hero: big number, unit, status pill, sensor name with min/max, trend line
            int hy = y + p + headerH + Px(16);
            SensorView pr = c.Primary;
            Color col = _theme.For(pr.Status);
            int heroLeft = x + p;

            bool showSpark = w >= Px(380);
            int sparkW = showSpark ? Math.Min(Px(230), (int)(w * 0.40f)) : 0;
            int leftW = (showSpark ? x + w - p - sparkW - Px(16) : x + w - p) - heroLeft;

            // Large text is drawn with GDI+ typographic metrics: GDI's measurement pads big fonts,
            // which leaves a visible gap before the unit.
            string num = pr.Value.HasValue ? Number(pr.Value.Value) : "—";
            TextRenderingHint oldHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            StringFormat typo = StringFormat.GenericTypographic;
            SizeF ns = g.MeasureString(num, _fonts.Hero, PointF.Empty, typo);
            using (var b = new SolidBrush(col))
                g.DrawString(num, _fonts.Hero, b, heroLeft - Px(1), hy - Px(5), typo);
            int after = heroLeft + (int)Math.Ceiling(ns.Width) + Px(3);

            if (pr.Value.HasValue)
            {
                string unit = _fahrenheit ? "°F" : "°C";
                SizeF us = g.MeasureString(unit, _fonts.HeroUnit, PointF.Empty, typo);
                using (var b = new SolidBrush(col))
                    g.DrawString(unit, _fonts.HeroUnit, b, after, hy + Px(3), typo);
                after += (int)Math.Ceiling(us.Width) + Px(14);

                string label = StatusLabel(pr.Status);
                Size ls = Draw.Measure(label, _fonts.BodyStrong);
                var pill = new Rectangle(after, hy + Px(12), ls.Width + Px(20), Px(26));
                if (pill.Right <= heroLeft + leftW)
                {
                    Draw.FillRounded(g, pill, Px(13), _theme.Tint(col));
                    Draw.Text(g, label, _fonts.BodyStrong, pill, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            g.TextRenderingHint = oldHint;

            string caption = !pr.Value.HasValue ? "Temperature not available"
                : pr.Min.HasValue && pr.Max.HasValue ? pr.Name + "  ·  " + Number(pr.Min.Value) + "–" + Number(pr.Max.Value) + "°"
                : pr.Name;
            Draw.Text(g, caption, _fonts.Caption, new Rectangle(heroLeft, hy + Px(48), leftW, Px(18)), _theme.SubText, TextFormatFlags.EndEllipsis);

            if (showSpark)
                DrawSparkline(g, c.History, new Rectangle(x + w - p - sparkW, hy + Px(2), sparkW, Px(58)), col);

            // details: one row with a bar per remaining sensor
            if (expanded)
            {
                int sepY = hy + heroH + Px(12);
                using (var pen = new Pen(_theme.CardBorder))
                    g.DrawLine(pen, x + p, sepY, x + w - p, sepY);

                int ry = sepY + Px(6);
                foreach (SensorView s in c.Others)
                {
                    DrawRow(g, s, x + p, ry, w - 2 * p, rowH);
                    ry += rowH;
                }
            }
            return h;
        }

        private void DrawRow(Graphics g, SensorView s, int x, int y, int w, int h)
        {
            int nameW = Math.Min(Px(190), (int)(w * 0.42f));
            int valueW = Px(56);
            var vcenter = TextFormatFlags.VerticalCenter;

            Draw.Text(g, s.Name, _fonts.Body, new Rectangle(x, y, nameW, h), _theme.Text, vcenter | TextFormatFlags.EndEllipsis);

            int barX = x + nameW + Px(12);
            int barW = w - nameW - Px(12) - valueW - Px(12);
            int barH = Px(6);
            int barY = y + (h - barH) / 2;
            if (barW > Px(20))
            {
                Draw.FillRounded(g, new Rectangle(barX, barY, barW, barH), barH / 2f, _theme.Track);
                if (s.Value.HasValue)
                {
                    float frac = Math.Max(0.04f, Math.Min(1f, (s.Value.Value - BarMinC) / (BarMaxC - BarMinC)));
                    int fillW = Math.Max(barH, (int)(barW * frac));
                    Draw.FillRounded(g, new Rectangle(barX, barY, fillW, barH), barH / 2f, _theme.For(s.Status));
                }
            }

            string value = s.Value.HasValue ? Degrees(s.Value) : "—";
            Draw.Text(g, value, _fonts.Body, new Rectangle(x + w - valueW, y, valueW, h),
                s.Value.HasValue ? _theme.Text : _theme.Unknown, vcenter | TextFormatFlags.Right);
        }

        private void DrawSparkline(Graphics g, float[] history, Rectangle r, Color color)
        {
            if (history == null) return;
            int n = history.Length;
            float lo = float.MaxValue, hi = float.MinValue;
            int valid = 0;
            foreach (float v in history)
                if (!float.IsNaN(v)) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); valid++; }

            // faint baseline so the area reads as a chart even before data arrives
            using (var pen = new Pen(_theme.Track) { DashStyle = DashStyle.Dot })
                g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
            if (valid < 2) return;

            float span = Math.Max(hi - lo, 8f);
            float mid = (hi + lo) / 2f;
            lo = mid - span / 2f - 1f;
            hi = mid + span / 2f + 1f;
            // Spread whatever history exists across the full width; once it holds
            // HistoryLength samples it scrolls.
            float dx = r.Width / (float)(n - 1);

            var pts = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(history[i])) continue;
                float px = r.Right - (n - 1 - i) * dx;
                float py = r.Bottom - (history[i] - lo) / (hi - lo) * r.Height;
                pts.Add(new PointF(px, py));
            }
            if (pts.Count < 2) return;

            var area = new List<PointF>(pts);
            area.Add(new PointF(pts[pts.Count - 1].X, r.Bottom));
            area.Add(new PointF(pts[0].X, r.Bottom));
            using (var brush = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2),
                       Color.FromArgb(_theme.Dark ? 90 : 70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical))
                g.FillPolygon(brush, area.ToArray());

            using (var pen = new Pen(color, Px(2)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, pts.ToArray());

            PointF last = pts[pts.Count - 1];
            float d = Px(7);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, last.X - d / 2, last.Y - d / 2, d, d);
        }

        private int DrawNotice(Graphics g, Notice n, int x, int y, int w)
        {
            int p = Px(18);
            int icon = Px(22);
            int tx = x + p + icon + Px(12);
            int tw = x + w - p - tx;
            int bodyH = TextRenderer.MeasureText(n.Body, _fonts.Body, new Size(tw, int.MaxValue),
                Draw.Flags | TextFormatFlags.WordBreak).Height;
            Size bs = n.Button != null ? Draw.Measure(n.Button, _fonts.BodyStrong) : Size.Empty;
            int btnH = Px(34);
            int h = p + Px(22) + Px(4) + bodyH + (n.Button != null ? Px(14) + btnH : 0) + p;
            if (g == null) return h;

            Draw.FillRounded(g, new Rectangle(x, y, w, h), Px(10), _theme.NoticeBack, _theme.NoticeBorder);

            var iconRect = new Rectangle(x + p, y + p, icon, icon);
            using (var b = new SolidBrush(_theme.Warm)) g.FillEllipse(b, iconRect);
            Draw.Text(g, "!", _fonts.BodyStrong, iconRect, _theme.Dark ? _theme.NoticeBack : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Draw.Text(g, n.Title, _fonts.BodyStrong, new Rectangle(tx, y + p, tw, Px(22)), _theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Draw.Text(g, n.Body, _fonts.Body, new Rectangle(tx, y + p + Px(26), tw, bodyH), _theme.Text, TextFormatFlags.WordBreak);

            if (n.Button != null)
            {
                var btn = new Rectangle(tx, y + p + Px(26) + bodyH + Px(14), bs.Width + Px(32), btnH);
                Color back = !n.ButtonEnabled ? _theme.Track
                           : _hover == "notice" ? Theme.Blend(_theme.Accent, _theme.Card, 0.85f)
                           : _theme.Accent;
                Draw.FillRounded(g, btn, Px(6), back);
                Draw.Text(g, n.Button, _fonts.BodyStrong, btn, n.ButtonEnabled ? _theme.AccentText : _theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (n.ButtonEnabled)
                    _hits.Add(new Hit(btn, "notice", () => { if (NoticeClicked != null) NoticeClicked(); }));
            }
            return h;
        }

        private void DrawChevron(Graphics g, Rectangle box, bool up)
        {
            float cx = box.Left + box.Width / 2f, cy = box.Top + box.Height / 2f, s = Px(5);
            using (var pen = new Pen(_theme.SubText, Math.Max(1.5f, 1.6f * _scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float dir = up ? -1 : 1;
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - s, cy - dir * s / 2),
                    new PointF(cx, cy + dir * s / 2),
                    new PointF(cx + s, cy - dir * s / 2),
                });
            }
        }

        // ---------- input ----------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Focused) Focus(); // so the mouse wheel scrolls this view
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit hit = Find(e.Location);
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            string id = hit != null ? hit.Id : null;
            if (id != _hover) { _hover = id; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Invalidate(); }
        }

        // Button semantics: the click fires on release, if press and release are on the same element.
        private string _pressed;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Hit hit = e.Button == MouseButtons.Left ? Find(e.Location) : null;
            _pressed = hit != null ? hit.Id : null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Hit hit = Find(e.Location);
            string pressed = _pressed;
            _pressed = null;
            if (hit != null && hit.Id == pressed) hit.Click();
        }

        private Hit Find(Point client)
        {
            var p = new Point(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);
            foreach (Hit h in _hits) if (h.Rect.Contains(p)) return h;
            return null;
        }

        // ---------- formatting ----------

        private float Convert(float celsius) { return _fahrenheit ? celsius * 9f / 5f + 32f : celsius; }

        private string Number(float celsius)
        {
            return Math.Round(Convert(celsius)).ToString("0", CultureInfo.CurrentCulture);
        }

        private string Degrees(float? celsius)
        {
            return celsius.HasValue ? Number(celsius.Value) + "°" : "—";
        }

        public static string StatusLabel(Status s)
        {
            switch (s)
            {
                case Status.Normal: return "Normal";
                case Status.Warm: return "Warm";
                case Status.Hot: return "Hot";
                default: return "No data";
            }
        }

        private int Px(float logical) { return (int)Math.Round(logical * _scale); }
    }
}
