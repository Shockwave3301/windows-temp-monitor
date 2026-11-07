using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TempMonitor
{
    /// <summary>
    /// Top bar: a one-sentence summary ("All temperatures are normal"), the hottest reading,
    /// a °C/°F switch and the "…" menu button.
    /// </summary>
    internal sealed class HeaderView : Control
    {
        public event Action<bool> UnitChanged;      // true = Fahrenheit
        public event Action<Point> MenuRequested;   // screen point to show the menu at

        private readonly float _scale;
        private Theme _theme;
        private UiFonts _fonts;
        private string _title = "Detecting sensors…";
        private string _subtitle = "";
        private Status _status = Status.Unknown;
        private bool _fahrenheit;
        private string _hover;
        private readonly List<Hit> _hits = new List<Hit>();

        public HeaderView(float scale)
        {
            _scale = scale;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Px(88);
        }

        public void SetTheme(Theme theme, UiFonts fonts)
        {
            _theme = theme;
            _fonts = fonts;
            BackColor = theme.Background;
            Invalidate();
        }

        public void SetSummary(string title, string subtitle, Status status)
        {
            if (title == _title && subtitle == _subtitle && status == _status) return;
            _title = title;
            _subtitle = subtitle;
            _status = status;
            Invalidate();
        }

        public bool Fahrenheit
        {
            get { return _fahrenheit; }
            set { if (_fahrenheit != value) { _fahrenheit = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_theme == null) return;
            Graphics g = e.Graphics;
            g.Clear(_theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            _hits.Clear();

            int m = Px(20);
            int top = Px(20);

            // --- right side: menu button and unit switch ---
            var menuBtn = new Rectangle(Width - m - Px(36), top, Px(36), Px(32));
            if (_hover == "menu") Draw.FillRounded(g, menuBtn, Px(6), _theme.Hover);
            float cy = menuBtn.Top + menuBtn.Height / 2f;
            using (var b = new SolidBrush(_theme.Text))
                for (int i = -1; i <= 1; i++)
                {
                    float cx = menuBtn.Left + menuBtn.Width / 2f + i * Px(6);
                    float d = Px(3) + 0.5f;
                    g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
                }
            _hits.Add(new Hit(menuBtn, "menu", () =>
            {
                if (MenuRequested != null) MenuRequested(PointToScreen(new Point(menuBtn.Right, menuBtn.Bottom + Px(4))));
            }));

            var seg = new Rectangle(menuBtn.Left - Px(10) - Px(96), top, Px(96), Px(32));
            Draw.FillRounded(g, seg, Px(7), _theme.Track);
            var segC = new Rectangle(seg.X + Px(3), seg.Y + Px(3), seg.Width / 2 - Px(3), seg.Height - Px(6));
            var segF = new Rectangle(seg.X + seg.Width / 2, seg.Y + Px(3), seg.Width / 2 - Px(3), seg.Height - Px(6));
            Rectangle selected = _fahrenheit ? segF : segC;
            Draw.FillRounded(g, selected, Px(5), _theme.Card, _theme.CardBorder);
            Draw.Text(g, "°C", _fonts.BodyStrong, segC, _fahrenheit ? _theme.SubText : _theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Draw.Text(g, "°F", _fonts.BodyStrong, segF, _fahrenheit ? _theme.Text : _theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _hits.Add(new Hit(segC, "c", () => SetUnit(false)));
            _hits.Add(new Hit(segF, "f", () => SetUnit(true)));

            // --- left side: status dot, summary and hottest reading ---
            Color dot = _theme.For(_status);
            int dotD = Px(10);
            int titleH = Px(26);
            using (var b = new SolidBrush(dot))
                g.FillEllipse(b, m, top + (titleH - dotD) / 2 + Px(3), dotD, dotD);

            int tx = m + dotD + Px(10);
            int tw = Math.Max(Px(60), seg.Left - Px(12) - tx);
            Draw.Text(g, _title, _fonts.Title, new Rectangle(tx, top, tw, titleH + Px(4)), _theme.Text, TextFormatFlags.EndEllipsis);
            Draw.Text(g, _subtitle, _fonts.Caption, new Rectangle(tx, top + titleH + Px(6), Width - m - tx, Px(18)), _theme.SubText, TextFormatFlags.EndEllipsis);
        }

        private void SetUnit(bool fahrenheit)
        {
            if (_fahrenheit == fahrenheit) return;
            Fahrenheit = fahrenheit;
            if (UnitChanged != null) UnitChanged(fahrenheit);
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

        private Hit Find(Point p)
        {
            foreach (Hit h in _hits) if (h.Rect.Contains(p)) return h;
            return null;
        }

        private int Px(float logical) { return (int)Math.Round(logical * _scale); }
    }
}
