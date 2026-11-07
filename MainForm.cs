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

namespace TempMonitor
{
    internal sealed class MainForm : Form
    {
        private const int RefreshMs = 1000;
        private const float WarmC = 70f;
        private const float HotC = 85f;

        private readonly float _scale;
        private readonly Settings _settings = Settings.Load();
        private readonly bool _isAdmin = IsRunningAsAdmin();

        private readonly ListView _list;
        private readonly ToolStripStatusLabel _status;
        private readonly Panel _banner;
        private readonly Label _bannerText;
        private readonly Button _bannerButton;
        private readonly ToolStripMenuItem _fahrenheitItem;
        private readonly ToolStripMenuItem _topMostItem;

        private readonly Dictionary<string, ListViewItem> _rows = new Dictionary<string, ListViewItem>();
        private readonly Dictionary<string, ListViewGroup> _groups = new Dictionary<string, ListViewGroup>();
        private List<Reading> _lastReadings = new List<Reading>();

        private readonly Computer _computer = Sensors.CreateComputer();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _worker;
        private volatile bool _resetRequested;
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

            Text = "Temp Monitor " + AppVersion;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = Px(600, 560);
            MinimumSize = Px(380, 260);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // --- menu ---
            var menu = new MenuStrip();
            _fahrenheitItem = new ToolStripMenuItem("Show in &Fahrenheit (°F)", null, (s, e) => ToggleFahrenheit()) { Checked = _settings.Fahrenheit };
            _topMostItem = new ToolStripMenuItem("Always on &top", null, (s, e) => ToggleTopMost()) { Checked = _settings.TopMost };
            menu.Items.Add(new ToolStripMenuItem("&View", null, _fahrenheitItem, _topMostItem));
            menu.Items.Add(new ToolStripMenuItem("&Sensors", null,
                new ToolStripMenuItem("&Reset min/max", null, (s, e) => _resetRequested = true),
                new ToolStripMenuItem("&Copy readings", null, (s, e) => CopyReadings()) { ShortcutKeys = Keys.Control | Keys.C }));
            menu.Items.Add(new ToolStripMenuItem("&Help", null,
                new ToolStripMenuItem("&PawnIO driver website", null, (s, e) => OpenUrl(PawnIo.Website)),
                new ToolStripMenuItem("&About Temp Monitor", null, (s, e) => ShowAbout())));
            MainMenuStrip = menu;

            // --- banner shown when CPU sensors can't be read ---
            _banner = new Panel
            {
                Dock = DockStyle.Top,
                Height = Px(44),
                BackColor = SystemColors.Info,
                Padding = new Padding(Px(8), Px(6), Px(8), Px(6)),
                Visible = false,
            };
            _bannerButton = new Button { Text = "Install PawnIO…", AutoSize = true, Dock = DockStyle.Right, UseVisualStyleBackColor = true };
            _bannerButton.Click += async (s, e) => await InstallPawnIoAsync();
            _bannerText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.InfoText, AutoEllipsis = true };
            _banner.Controls.Add(_bannerText);
            _banner.Controls.Add(_bannerButton);

            // --- sensor list ---
            _list = new DoubleBufferedListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowGroups = true,
                BorderStyle = BorderStyle.None,
            };
            _list.Columns.Add("Sensor", Px(280));
            _list.Columns.Add("Current", Px(85), HorizontalAlignment.Right);
            _list.Columns.Add("Min", Px(85), HorizontalAlignment.Right);
            _list.Columns.Add("Max", Px(85), HorizontalAlignment.Right);
            _list.Resize += (s, e) => FitFirstColumn();

            // --- status bar ---
            var statusStrip = new StatusStrip();
            _status = new ToolStripStatusLabel("Detecting sensors…") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.Add(_status);

            // Dock order: the first control added fills what the later ones leave.
            Controls.AddRange(new Control[] { _list, _banner, menu, statusStrip });

            TopMost = _settings.TopMost;
            RestoreWindowBounds();
            UpdateBanner();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitFirstColumn();
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SensorPoller" };
            _worker.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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

        // ---------- worker thread ----------

        private void WorkerLoop()
        {
            try
            {
                _computer.Open();
            }
            catch (Exception ex)
            {
                PostStatus("Could not open hardware sensors: " + ex.Message);
                return;
            }

            do
            {
                try
                {
                    if (_resetRequested)
                    {
                        _resetRequested = false;
                        Sensors.ResetMinMax(_computer);
                    }

                    List<Reading> readings = Sensors.ReadAll(_computer);
                    if (_stop.WaitOne(0)) break;
                    PostReadings(readings);
                }
                catch (Exception ex)
                {
                    PostStatus("Sensor error: " + ex.Message);
                }
            }
            while (!_stop.WaitOne(RefreshMs));
        }

        private void PostReadings(List<Reading> readings)
        {
            try { BeginInvoke((Action)(() => ShowReadings(readings))); }
            catch (InvalidOperationException) { } // window is closing
        }

        private void PostStatus(string text)
        {
            try { BeginInvoke((Action)(() => _status.Text = text)); }
            catch (InvalidOperationException) { }
        }

        // ---------- UI thread ----------

        private void ShowReadings(List<Reading> readings)
        {
            if (IsDisposed) return;
            _lastReadings = readings;

            _list.BeginUpdate();
            try
            {
                foreach (Reading r in readings)
                {
                    ListViewGroup group;
                    if (!_groups.TryGetValue(r.GroupKey, out group))
                    {
                        group = new ListViewGroup(r.GroupKey, r.GroupName);
                        _groups[r.GroupKey] = group;
                        _list.Groups.Add(group);
                    }

                    ListViewItem row;
                    if (!_rows.TryGetValue(r.Id, out row))
                    {
                        row = new ListViewItem(new[] { r.Name, "", "", "" }, group) { UseItemStyleForSubItems = true };
                        _rows[r.Id] = row;
                        _list.Items.Add(row);
                    }

                    SetText(row.SubItems[1], Format(r.Value));
                    SetText(row.SubItems[2], Format(r.Min));
                    SetText(row.SubItems[3], Format(r.Max));

                    Color color = !r.Value.HasValue ? SystemColors.GrayText
                                : r.Value.Value >= HotC ? Color.Firebrick
                                : r.Value.Value >= WarmC ? Color.DarkOrange
                                : SystemColors.WindowText;
                    if (row.ForeColor != color) row.ForeColor = color;
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            if (readings.Count == 0)
                _status.Text = "No temperature sensors found.";
            else
                _status.Text = readings.Count + " temperature sensors · updated " + DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        }

        private void UpdateBanner()
        {
            if (!_isAdmin)
            {
                _bannerText.Text = "Not running as administrator, so CPU and motherboard temperatures are unavailable.";
                _bannerButton.Visible = false;
                _banner.Visible = true;
            }
            else if (!PawnIo.IsInstalled)
            {
                _bannerText.Text = "CPU and motherboard temperatures need the free PawnIO driver.";
                _bannerButton.Visible = true;
                _banner.Visible = true;
            }
            else
            {
                _banner.Visible = false;
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

            _bannerButton.Enabled = false;
            _bannerText.Text = "Downloading and running the PawnIO installer…";
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
            _bannerButton.Enabled = true;
            UpdateBanner();
        }

        private void ToggleFahrenheit()
        {
            _settings.Fahrenheit = _fahrenheitItem.Checked = !_settings.Fahrenheit;
            _settings.Save();
            if (_lastReadings.Count > 0) ShowReadings(_lastReadings);
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
            string lastGroup = null;
            foreach (Reading r in _lastReadings)
            {
                if (r.GroupName != lastGroup) { sb.AppendLine(); sb.AppendLine(r.GroupName); lastGroup = r.GroupName; }
                sb.AppendLine("  " + r.Name + ": " + Format(r.Value) + "  (min " + Format(r.Min) + ", max " + Format(r.Max) + ")");
            }
            try { Clipboard.SetText(sb.ToString()); _status.Text = "Readings copied to the clipboard."; }
            catch (Exception ex) { _status.Text = "Could not copy: " + ex.Message; }
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

        private string Format(float? celsius)
        {
            if (!celsius.HasValue) return "—";
            float v = celsius.Value;
            return _settings.Fahrenheit
                ? (v * 9f / 5f + 32f).ToString("0.0", CultureInfo.CurrentCulture) + " °F"
                : v.ToString("0.0", CultureInfo.CurrentCulture) + " °C";
        }

        private static void SetText(ListViewItem.ListViewSubItem item, string text)
        {
            if (item.Text != text) item.Text = text;
        }

        private void FitFirstColumn()
        {
            if (_list.Columns.Count < 4) return;
            int others = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width;
            int w = _list.ClientSize.Width - others - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth;
            if (w > Px(120)) _list.Columns[0].Width = w;
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

        private sealed class DoubleBufferedListView : ListView
        {
            public DoubleBufferedListView()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            }
        }
    }
}
