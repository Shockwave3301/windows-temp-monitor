using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace TempMonitor
{
    internal sealed class MainForm : Form
    {
        private const int RefreshMs = 1000;

        /// <summary>Set by "--theme dark|light" to override the Windows setting.</summary>
        public static bool? ForcedDark;

        /// <summary>Set by "--snapshot file.png": render the window to an image after a few seconds, then exit.</summary>
        public static string SnapshotPath;

        private readonly float _scale;
        private readonly Settings _settings = Settings.Load();
        private readonly bool _isAdmin = IsRunningAsAdmin();
        private readonly UiFonts _fonts;
        private Theme _theme;

        private readonly HeaderView _header;
        private readonly DashboardView _dashboard;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _topMostItem;

        private List<Reading> _lastReadings = new List<Reading>();
        private List<DeviceCard> _lastCards = new List<DeviceCard>();
        private bool _installing;

        private readonly Computer _computer = Sensors.CreateComputer();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _worker;
        private bool _restartOnClose;

        public static string AppVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public MainForm()
        {
            // Read DPI from the screen: CreateGraphics() would create the window handle early
            // and Windows would then ignore StartPosition.
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                _scale = g.DpiX / 96f;
            _fonts = new UiFonts(_scale);

            Text = "Temp Monitor";
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = Px(480, 720);
            MinimumSize = Px(360, 320);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _header = new HeaderView(_scale) { Dock = DockStyle.Top, Fahrenheit = _settings.Fahrenheit };
            _header.UnitChanged += f => { _settings.Fahrenheit = f; _settings.Save(); _dashboard.Fahrenheit = f; Render(false); };
            _header.MenuRequested += pt => _menu.Show(pt, ToolStripDropDownDirection.BelowLeft);

            _dashboard = new DashboardView(_scale, _settings.Collapsed) { Dock = DockStyle.Fill, Fahrenheit = _settings.Fahrenheit };
            _dashboard.CardToggled += key => { _dashboard.ToggleCollapsed(key); _settings.Save(); };
            _dashboard.NoticeClicked += async () => await InstallPawnIoAsync();

            _topMostItem = new ToolStripMenuItem("Always on top", null, (s, e) => ToggleTopMost()) { Checked = _settings.TopMost };
            _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Font = _fonts.Body };
            _menu.Items.Add(_topMostItem);
            _menu.Items.Add(new ToolStripMenuItem("Reset min/max", null, (s, e) => { Cards.ResetMinMax(); Render(false); }));
            _menu.Items.Add(new ToolStripMenuItem("Copy readings", null, (s, e) => CopyReadings()) { ShortcutKeyDisplayString = "Ctrl+C" });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("PawnIO driver website", null, (s, e) => OpenUrl(PawnIo.Website)));
            _menu.Items.Add(new ToolStripMenuItem("About Temp Monitor", null, (s, e) => ShowAbout()));
            foreach (ToolStripItem item in _menu.Items) item.Padding = new Padding(Px(4), Px(4), Px(4), Px(4));

            // Dock order: the first control added fills what the later ones leave.
            Controls.Add(_dashboard);
            Controls.Add(_header);

            TopMost = _settings.TopMost;
            RestoreWindowBounds();
            ApplyTheme();
            UpdateNotice();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_theme != null) Theme.ApplyTitleBar(Handle, _theme.Dark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SensorPoller" };
            _worker.Start();

            if (SnapshotPath != null)
            {
                var timer = new System.Windows.Forms.Timer { Interval = 8000 };
                timer.Tick += (s, ev) => { timer.Stop(); SaveSnapshot(SnapshotPath); Close(); };
                timer.Start();
            }
        }

        private void SaveSnapshot(string path)
        {
            try
            {
                // Full content, not just what fits in the window.
                // Opaque bitmap: ClearType text renders badly onto a transparent one.
                int w = ClientSize.Width;
                using (var bmp = new Bitmap(w, _header.Height + _dashboard.ContentHeight(w), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    _header.DrawToBitmap(bmp, new Rectangle(0, 0, w, _header.Height));
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.TranslateTransform(0, _header.Height);
                        _dashboard.RenderAll(g, w);
                    }
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C) { CopyReadings(); e.Handled = true; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _settings.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.Save();

            _stop.Set();
            if (_worker != null) _worker.Join(3000);
            try { _computer.Close(); } catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_restartOnClose)
            {
                // Already elevated, so the new instance starts without another UAC prompt.
                try { Process.Start(Application.ExecutablePath); } catch { }
            }
        }

        // ---------- theme ----------

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.Accessibility) return;
            try { BeginInvoke((Action)ApplyTheme); } catch (InvalidOperationException) { }
        }

        private void ApplyTheme()
        {
            Theme theme = Theme.Current(ForcedDark);
            _theme = theme;

            BackColor = theme.Background;
            _header.SetTheme(theme, _fonts);
            _dashboard.SetTheme(theme, _fonts);
            _menu.Renderer = new ThemedMenuRenderer(theme);
            _menu.BackColor = theme.Card;
            if (IsHandleCreated) Theme.ApplyTitleBar(Handle, theme.Dark);
        }

        // ---------- worker thread ----------

        private void WorkerLoop()
        {
            try
            {
                _computer.Open();
            }
            catch (Exception ex)
            {
                PostSummary("Could not read hardware sensors", ex.Message);
                return;
            }

            do
            {
                try
                {
                    List<Reading> readings = Sensors.ReadAll(_computer);
                    if (_stop.WaitOne(0)) break;
                    try { BeginInvoke((Action)(() => OnReadings(readings))); }
                    catch (InvalidOperationException) { } // window is closing
                }
                catch (Exception ex)
                {
                    PostSummary("Sensor error", ex.Message);
                }
            }
            while (!_stop.WaitOne(RefreshMs));
        }

        private void PostSummary(string title, string subtitle)
        {
            try { BeginInvoke((Action)(() => _header.SetSummary(title, subtitle, Status.Unknown))); }
            catch (InvalidOperationException) { }
        }

        // ---------- UI thread ----------

        private void OnReadings(List<Reading> readings)
        {
            if (IsDisposed) return;
            _lastReadings = readings;
            Render(true);
        }

        /// <param name="newSample">True only for a fresh poll; false just redraws the last one.</param>
        private void Render(bool newSample)
        {
            _lastCards = Cards.Build(_lastReadings, newSample);
            _dashboard.SetCards(_lastCards);
            UpdateSummary(_lastCards);
        }

        private void UpdateSummary(List<DeviceCard> cards)
        {
            string time = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
            if (cards.Count == 0)
            {
                _header.SetSummary(_lastReadings.Count == 0 ? "Detecting sensors…" : "No temperature sensors found", "", Status.Unknown);
                return;
            }

            Status worst = Status.Unknown;
            foreach (DeviceCard c in cards) if (c.Status > worst) worst = c.Status;
            var worstCards = cards.FindAll(c => c.Status == worst);

            string title;
            if (worst == Status.Hot || worst == Status.Warm)
            {
                string word = worst == Status.Hot ? "hot" : "warm";
                title = worstCards.Count == 1
                    ? worstCards[0].KindLabel + " is running " + word
                    : worstCards.Count + " devices are running " + word;
            }
            else if (worst == Status.Normal)
            {
                title = "All temperatures are normal";
            }
            else
            {
                title = "No temperature readings yet";
            }

            SensorView hottest = null;
            foreach (DeviceCard c in cards)
            {
                var all = new List<SensorView>(c.Others) { c.Primary };
                foreach (SensorView s in all)
                    if (s.Value.HasValue && (hottest == null || s.Value > hottest.Value)) hottest = s;
            }

            string subtitle = hottest != null
                ? "Hottest: " + hottest.Name + " " + Rounded(hottest.Value.Value) + "  ·  Updated " + time
                : "Updated " + time;
            _header.SetSummary(title, subtitle, worst);
        }

        private void UpdateNotice()
        {
            _dashboard.UnavailableReason = !_isAdmin ? "Needs administrator rights to read this device."
                                         : !PawnIo.IsInstalled ? "Needs the PawnIO driver to read this device."
                                         : "No readings from this device.";
            if (!_isAdmin)
            {
                _dashboard.SetNotice(new Notice
                {
                    Title = "Running without administrator rights",
                    Body = "CPU and motherboard temperatures need administrator access. Close the app and open it again, then choose Yes when Windows asks.",
                });
            }
            else if (!PawnIo.IsInstalled)
            {
                _dashboard.SetNotice(new Notice
                {
                    Title = "CPU temperatures need a driver",
                    Body = _installing
                        ? "Installing PawnIO… follow the steps in the installer window."
                        : "Temp Monitor uses PawnIO, a free, signed driver, to read CPU and motherboard sensors. GPU and drive temperatures work without it.",
                    Button = _installing ? "Installing…" : "Install PawnIO",
                    ButtonEnabled = !_installing,
                });
            }
            else
            {
                _dashboard.SetNotice(null);
            }
        }

        private async Task InstallPawnIoAsync()
        {
            DialogResult answer = MessageBox.Show(this,
                "Temp Monitor will download the PawnIO " + PawnIo.Version + " installer from its official GitHub release " +
                "(github.com/namazso/PawnIO.Setup), check that it matches the expected checksum, and run it.\n\n" +
                "PawnIO is a signed, open-source driver that lets programs read CPU and motherboard sensors. " +
                "You can uninstall it any time from Windows Settings → Apps.\n\nContinue?",
                "Install PawnIO", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            _installing = true;
            UpdateNotice();
            try
            {
                int exitCode = await Task.Run(() => PawnIo.DownloadAndInstall());
                if (PawnIo.IsInstalled)
                {
                    MessageBox.Show(this, "PawnIO is installed. Temp Monitor will now restart to load CPU sensors.",
                        "Install PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _restartOnClose = true;
                    Close();
                    return;
                }
                MessageBox.Show(this, "PawnIO was not installed (installer exit code " + exitCode + ").",
                    "Install PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not install PawnIO:\n\n" + ex.Message +
                    "\n\nYou can install it manually from " + PawnIo.Website + ".",
                    "Install PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _installing = false;
            UpdateNotice();
        }

        private void ToggleTopMost()
        {
            _settings.TopMost = _topMostItem.Checked = !_settings.TopMost;
            TopMost = _settings.TopMost;
            _settings.Save();
        }

        private void CopyReadings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Temp Monitor " + AppVersion + " · " + DateTime.Now.ToString("g", CultureInfo.CurrentCulture));
            foreach (DeviceCard c in _lastCards)
            {
                sb.AppendLine();
                sb.AppendLine(c.KindLabel + ": " + c.Name);
                var all = new List<SensorView> { c.Primary };
                all.AddRange(c.Others);
                foreach (SensorView s in all)
                    sb.AppendLine("  " + s.Name + ": " + FormatTemp(s.Value) + "  (min " + FormatTemp(s.Min) + ", max " + FormatTemp(s.Max) + ")");
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                _header.SetSummary("Readings copied to the clipboard", "", Status.Unknown);
            }
            catch { }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "Temp Monitor " + AppVersion + "\n\n" +
                "Shows live temperatures from all hardware sensors.\n\n" +
                "Sensor access: LibreHardwareMonitorLib (MPL-2.0)\n" +
                "CPU/motherboard driver: PawnIO (" + PawnIo.Website + ")\n\n" +
                "Released under the MIT License. Third-party licenses are listed in THIRD-PARTY-NOTICES.md.",
                "About Temp Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestoreWindowBounds()
        {
            if (!_settings.WindowBounds.HasValue) return;
            Rectangle r = _settings.WindowBounds.Value;
            foreach (Screen screen in Screen.AllScreens)
            {
                // Only restore if the title bar would be visible on some monitor.
                if (screen.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, Px(30))))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = r;
                    return;
                }
            }
        }

        private string FormatTemp(float? celsius)
        {
            if (!celsius.HasValue) return "—";
            float v = celsius.Value;
            return _settings.Fahrenheit
                ? (v * 9f / 5f + 32f).ToString("0.0", CultureInfo.CurrentCulture) + " °F"
                : v.ToString("0.0", CultureInfo.CurrentCulture) + " °C";
        }

        private string Rounded(float celsius)
        {
            float v = _settings.Fahrenheit ? celsius * 9f / 5f + 32f : celsius;
            return Math.Round(v).ToString("0", CultureInfo.CurrentCulture) + (_settings.Fahrenheit ? " °F" : " °C");
        }

        private int Px(int logical) { return (int)Math.Round(logical * _scale); }
        private Size Px(int w, int h) { return new Size(Px(w), Px(h)); }

        private static void OpenUrl(string url)
        {
            // Going through explorer.exe opens the browser as the normal user, not elevated like this app.
            try { Process.Start("explorer.exe", "\"" + url + "\""); } catch { }
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
