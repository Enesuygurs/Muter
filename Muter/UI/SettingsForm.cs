using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Overlay;
using Muter.Properties;
using Muter.UI.Controls;

namespace Muter.UI
{
    // All settings in one window: a side navigation with General, Overlay and Mini indicator pages. Every change is
    // applied immediately through the TrayApplicationContext, and the window follows changes made elsewhere (e.g. an
    // overlay dragged to a new position).
    public class SettingsForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int TitleBarHeight = 44;
        private const int RowHeight = 52;
        private const int RowGap = 6;
        private const int WindowHeight = 580;

        private static readonly OverlaySize[] Sizes = { OverlaySize.Small, OverlaySize.Medium, OverlaySize.Large };

        private readonly TrayApplicationContext app;
        private readonly Settings settings;
        private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        private readonly Dictionary<string, NavButton> navButtons = new Dictionary<string, NavButton>();
        private readonly Panel content;
        private SettingRow positionsRow;
        private bool updating;

        internal SettingsForm(TrayApplicationContext app)
        {
            this.app = app ?? throw new ArgumentNullException(nameof(app));
            settings = app.Settings;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = new Font(Theme.FontFamily, 9.75F);
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(720, WindowHeight);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Muter Settings";
            Icon = AppResources.AppIcon;
            KeyPreview = true;

            BuildTitleBar();
            BuildNavigation();
            content = new Panel { Location = new Point(196, TitleBarHeight), Size = new Size(508, WindowHeight - TitleBarHeight - 16), BackColor = Theme.Background };
            Controls.Add(content);
            BuildGeneralPage();
            BuildOverlayPage();
            BuildMiniIndicatorPage();
            ResumeLayout(false);

            ShowPage("General");
            RefreshDeviceOptions();
            RefreshFromSettings();
            app.SettingsChanged += OnSettingsChanged;
            FormClosed += (s, e) => app.SettingsChanged -= OnSettingsChanged;
        }

        #region Controls (test access)

        internal DropdownButton DevicesDropdown { get; private set; }

        internal ToggleSwitch SyncToggle { get; private set; }

        internal KeyCaptureBox HotkeyKeys { get; private set; }

        internal FlatButton ChangeHotkeyButton { get; private set; }

        internal ToggleSwitch StartupToggle { get; private set; }

        internal ToggleSwitch ShowOverlayToggle { get; private set; }

        internal DropdownButton PositionDropdown { get; private set; }

        internal SegmentedControl SizeSegments { get; private set; }

        internal DropdownButton DisplayTimeDropdown { get; private set; }

        internal DropdownButton MonitorDropdown { get; private set; }

        internal FlatButton MovePositionsButton { get; private set; }

        internal FlatButton ResetPositionsButton { get; private set; }

        internal ToggleSwitch MiniIndicatorToggle { get; private set; }

        internal DropdownButton MiniPositionDropdown { get; private set; }

        internal SegmentedControl MiniSizeSegments { get; private set; }

        internal string CurrentPage => pages.First(p => p.Value.Visible).Key;

        #endregion

        internal void ShowPage(string name)
        {
            foreach (var pair in pages)
                pair.Value.Visible = pair.Key == name;
            foreach (var pair in navButtons)
                pair.Value.Selected = pair.Key == name;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWCP_ROUND: rounded corners on Windows 11, ignored elsewhere
            try
            {
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCLIENT)
            {
                Point point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                if (point.Y < TitleBarHeight * DeviceDpi / 96)
                    m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        #region Layout

        private void BuildTitleBar()
        {
            var icon = new PictureBox { Location = new Point(16, 13), Size = new Size(20, 20), SizeMode = PictureBoxSizeMode.Zoom };
            using (var small = new Icon(AppResources.AppIcon, 32, 32))
                icon.Image = small.ToBitmap();
            FormClosed += (s, e) => icon.Image?.Dispose();
            var title = new Label
            {
                Text = "Settings",
                AutoSize = true,
                Location = new Point(42, 11),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
            };
            var close = new FlatButton
            {
                ButtonStyle = FlatButtonStyle.Close,
                Text = "",
                Font = new Font(Theme.IconFontFamily, 8F),
                Location = new Point(673, 1),
                Size = new Size(46, 32),
                TabStop = false,
            };
            close.Click += (s, e) => Close();
            icon.MouseDown += TitleBar_MouseDown;
            title.MouseDown += TitleBar_MouseDown;
            Controls.AddRange(new Control[] { icon, title, close });
        }

        private void BuildNavigation()
        {
            int y = TitleBarHeight + 4;
            foreach (var entry in new[] { ("General", ""), ("Overlay", ""), ("Mini indicator", "") })
            {
                string page = entry.Item1;
                var button = new NavButton { Text = page, Glyph = entry.Item2, Location = new Point(12, y), Size = new Size(172, 38) };
                button.Click += (s, e) => ShowPage(page);
                navButtons[page] = button;
                Controls.Add(button);
                y += 42;
            }

            var version = new Label
            {
                Text = "Muter " + VersionText(),
                AutoSize = true,
                Location = new Point(24, WindowHeight - 32),
                ForeColor = Theme.TextDisabled,
                Font = new Font(Theme.FontFamily, 8.25F),
            };
            Controls.Add(version);
        }

        // "1.1" or "1.1.2" from this assembly (Application.ProductVersion would be the host process when tested).
        internal static string VersionText()
        {
            Version version = typeof(SettingsForm).Assembly.GetName().Version;
            return version.Build > 0 ? version.ToString(3) : version.ToString(2);
        }

        private Panel NewPage(string name, out int y)
        {
            var page = new Panel { Location = Point.Empty, Size = content.Size, BackColor = Theme.Background, Visible = false };
            page.Controls.Add(new Label
            {
                Text = name,
                AutoSize = true,
                Location = new Point(0, 6),
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
            });
            pages[name] = page;
            content.Controls.Add(page);
            y = 52;
            return page;
        }

        private static void AddSection(Panel page, string text, ref int y)
        {
            page.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(2, y),
                Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold),
                ForeColor = Theme.TextSecondary,
            });
            y += 24;
        }

        private SettingRow AddRow(Panel page, string title, string description, Control setting, ref int y)
        {
            var row = new SettingRow
            {
                Title = title,
                Description = description ?? string.Empty,
                Location = new Point(0, y),
                Size = new Size(page.Width - 12, RowHeight),
                Font = Font,
            };
            row.Setting = setting;
            page.Controls.Add(row);
            y += RowHeight + RowGap;
            return row;
        }

        private void BuildGeneralPage()
        {
            Panel page = NewPage("General", out int y);

            AddSection(page, "Microphone", ref y);
            DevicesDropdown = new DropdownButton { Size = new Size(230, 30), Font = Font };
            DevicesDropdown.DropDownOpening += (s, e) => RefreshDeviceOptions();
            DevicesDropdown.SelectionChanged += (s, e) =>
            {
                string id = DevicesDropdown.SelectedValue as string;
                string name = DevicesDropdown.SelectedText;
                Apply(() => app.SelectDevice(string.IsNullOrEmpty(id) ? null : id, name));
            };
            AddRow(page, "Microphones", "Which microphones the hotkey mutes", DevicesDropdown, ref y);

            SyncToggle = new ToggleSwitch();
            SyncToggle.Toggled += (s, e) => Apply(() => app.SetSyncDevices(SyncToggle.Checked));
            AddRow(page, "Keep in sync", "Controlled microphones always share the same state", SyncToggle, ref y);

            y += 6;
            AddSection(page, "Hotkey", ref y);
            var hotkeyPanel = new Panel { Size = new Size(260, 32), BackColor = Theme.Surface };
            HotkeyKeys = new KeyCaptureBox { Compact = true, Location = new Point(0, 2), Size = new Size(170, 28), BackColor = Theme.Surface, TabStop = false, Cursor = Cursors.Default, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };
            ChangeHotkeyButton = new FlatButton { Text = "Change", Location = new Point(180, 0), Size = new Size(80, 32), BackColor = Theme.Surface, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };
            ChangeHotkeyButton.Click += (s, e) => app.OpenHotkeyDialog(this);
            hotkeyPanel.Controls.Add(HotkeyKeys);
            hotkeyPanel.Controls.Add(ChangeHotkeyButton);
            AddRow(page, "Mute hotkey", "Mutes and unmutes from any app", hotkeyPanel, ref y);

            // Settings shared by the overlay and the mini indicator.
            y += 6;
            AddSection(page, "Screen", ref y);
            MonitorDropdown = new DropdownButton { Size = new Size(230, 30), Font = Font };
            MonitorDropdown.DropDownOpening += (s, e) => RefreshMonitorOptions();
            MonitorDropdown.SelectionChanged += (s, e) => Apply(() => app.SetMonitor((string)MonitorDropdown.SelectedValue));
            AddRow(page, "Monitor", "For the overlay and mini indicator", MonitorDropdown, ref y);

            var positionsPanel = new Panel { Size = new Size(168, 32), BackColor = Theme.Surface };
            MovePositionsButton = new FlatButton { Text = "Move", Location = new Point(0, 0), Size = new Size(80, 32), BackColor = Theme.Surface, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };
            MovePositionsButton.Click += (s, e) => Apply(() => app.SetPositionsUnlocked(!app.PositionsUnlocked));
            ResetPositionsButton = new FlatButton { Text = "Reset", Location = new Point(88, 0), Size = new Size(80, 32), BackColor = Theme.Surface, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };
            ResetPositionsButton.Click += (s, e) => Apply(app.ResetPositions);
            positionsPanel.Controls.Add(MovePositionsButton);
            positionsPanel.Controls.Add(ResetPositionsButton);
            positionsRow = AddRow(page, "Positions", string.Empty, positionsPanel, ref y);

            y += 6;
            AddSection(page, "Startup", ref y);
            StartupToggle = new ToggleSwitch();
            StartupToggle.Toggled += (s, e) => Apply(() => app.SetStartup(StartupToggle.Checked));
            AddRow(page, "Start with Windows", "Run Muter when you sign in", StartupToggle, ref y);
        }

        private void BuildOverlayPage()
        {
            Panel page = NewPage("Overlay", out int y);

            ShowOverlayToggle = new ToggleSwitch();
            ShowOverlayToggle.Toggled += (s, e) => Apply(() => app.SetShowOverlay(ShowOverlayToggle.Checked));
            AddRow(page, "Show overlay", "Appears when the microphone is turned on or off", ShowOverlayToggle, ref y);

            PositionDropdown = new DropdownButton { Size = new Size(230, 30), Font = Font };
            PositionDropdown.SetOptions(Enum.GetValues(typeof(OverlayAnchor)).Cast<OverlayAnchor>()
                .Select(a => new DropdownOption(TrayApplicationContext.AnchorLabel(a), a)), null);
            PositionDropdown.SelectionChanged += (s, e) => Apply(() => app.SetOverlayAnchor((OverlayAnchor)PositionDropdown.SelectedValue));
            AddRow(page, "Position", "Choose Custom to drag it anywhere", PositionDropdown, ref y);

            SizeSegments = new SegmentedControl { Size = new Size(230, 30), Font = Font, Segments = Sizes.Select(s => s.ToString()).ToArray() };
            SizeSegments.SelectionChanged += (s, e) => Apply(() => app.SetOverlaySize(Sizes[SizeSegments.SelectedIndex]));
            AddRow(page, "Size", null, SizeSegments, ref y);

            DisplayTimeDropdown = new DropdownButton { Size = new Size(230, 30), Font = Font };
            DisplayTimeDropdown.SetOptions(TrayApplicationContext.DisplayTimeChoices.Select(ms => new DropdownOption(TrayApplicationContext.DisplayTimeLabel(ms), ms)), null);
            DisplayTimeDropdown.SelectionChanged += (s, e) => Apply(() => app.SetDisplayTime((int)DisplayTimeDropdown.SelectedValue));
            AddRow(page, "Display time", "How long it stays before fading out", DisplayTimeDropdown, ref y);
        }

        private void BuildMiniIndicatorPage()
        {
            Panel page = NewPage("Mini indicator", out int y);

            MiniIndicatorToggle = new ToggleSwitch();
            MiniIndicatorToggle.Toggled += (s, e) => Apply(() => app.SetMiniIndicatorEnabled(MiniIndicatorToggle.Checked));
            AddRow(page, "Show mini indicator", "A small, always-visible status square", MiniIndicatorToggle, ref y);

            MiniPositionDropdown = new DropdownButton { Size = new Size(230, 30), Font = Font };
            MiniPositionDropdown.SetOptions(TrayApplicationContext.MiniIndicatorAnchorChoices.Select(a => new DropdownOption(TrayApplicationContext.AnchorLabel(a), a)), null);
            MiniPositionDropdown.SelectionChanged += (s, e) => Apply(() => app.SetMiniIndicatorAnchor((OverlayAnchor)MiniPositionDropdown.SelectedValue));
            AddRow(page, "Position", "Choose Custom to drag it anywhere", MiniPositionDropdown, ref y);

            MiniSizeSegments = new SegmentedControl { Size = new Size(230, 30), Font = Font, Segments = Sizes.Select(s => s.ToString()).ToArray() };
            MiniSizeSegments.SelectionChanged += (s, e) => Apply(() => app.SetMiniIndicatorSize(Sizes[MiniSizeSegments.SelectedIndex]));
            AddRow(page, "Size", null, MiniSizeSegments, ref y);

            var legend = new Label
            {
                Text = "Green: microphone on    Red: muted    Gray: no microphone    Amber: unknown (treat as on)",
                Location = new Point(2, y + 4),
                Size = new Size(page.Width - 14, 40),
                ForeColor = Theme.TextSecondary,
                Font = new Font(Theme.FontFamily, 8.75F),
            };
            page.Controls.Add(legend);
        }

        #endregion

        #region State

        // Runs a change and keeps the window consistent even if it fails.
        private void Apply(Action change)
        {
            if (updating)
                return;
            try
            {
                change();
            }
            catch (Exception ex)
            {
                Log.Error("Applying a setting failed", ex);
            }
            RefreshFromSettings();
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed)
                RefreshFromSettings();
        }

        // Shows the current settings in every control (without triggering their change events).
        internal void RefreshFromSettings()
        {
            updating = true;
            try
            {
                string deviceValue = app.SelectedDeviceId ?? string.Empty;
                if (!DevicesDropdown.Options.Any(o => string.Equals((string)o.Value, deviceValue, StringComparison.OrdinalIgnoreCase)))
                    RefreshDeviceOptions();
                DevicesDropdown.SelectedValue = DevicesDropdown.Options
                    .Select(o => o.Value).FirstOrDefault(v => string.Equals((string)v, deviceValue, StringComparison.OrdinalIgnoreCase)) ?? deviceValue;
                SyncToggle.Checked = settings.syncDevices;
                HotkeyKeys.Keycaps = HotkeyText.Parts(app.ConfiguredHotkey);
                StartupToggle.Checked = app.IsStartupEnabled();

                ShowOverlayToggle.Checked = settings.showGui;
                PositionDropdown.SelectedValue = OverlayPlacement.ParseEnum(settings.overlayPosition, OverlayAnchor.LowerCenter);
                SizeSegments.SelectedIndex = Array.IndexOf(Sizes, OverlayPlacement.ParseEnum(settings.overlaySize, OverlaySize.Medium));
                DisplayTimeDropdown.SelectedValue = TrayApplicationContext.DisplayTimeChoices.Contains(settings.overlayDurationMs) ? settings.overlayDurationMs : 1000;
                RefreshMonitorOptions();
                RefreshPositionsRow();

                MiniIndicatorToggle.Checked = settings.miniIndicatorEnabled;
                MiniPositionDropdown.SelectedValue = OverlayPlacement.ParseEnum(settings.miniIndicatorPosition, OverlayAnchor.BottomRight);
                MiniSizeSegments.SelectedIndex = Array.IndexOf(Sizes, OverlayPlacement.ParseEnum(settings.miniIndicatorSize, OverlaySize.Medium));
            }
            finally
            {
                updating = false;
            }
        }

        private void RefreshDeviceOptions()
        {
            IReadOnlyList<TrayApplicationContext.DeviceChoice> choices = app.GetDeviceChoices(out bool listFailed);
            var options = choices.Select(c => new DropdownOption(c.Label, c.Id ?? string.Empty, c.Available)).ToList();
            if (listFailed)
                options.Add(new DropdownOption("(Could not list the microphones)", "\0failed", enabled: false));
            DevicesDropdown.SetOptions(options, app.SelectedDeviceId ?? string.Empty);
        }

        // Move turns into a highlighted Done while the overlays can be dragged; it is unavailable when nothing is shown.
        private void RefreshPositionsRow()
        {
            bool unlocked = app.PositionsUnlocked;
            bool anythingShown = settings.showGui || settings.miniIndicatorEnabled;
            MovePositionsButton.Text = unlocked ? "Done" : "Move";
            MovePositionsButton.ButtonStyle = unlocked ? FlatButtonStyle.Primary : FlatButtonStyle.Secondary;
            MovePositionsButton.Enabled = unlocked || anythingShown;
            positionsRow.Description = unlocked ? "Drag them now, then click Done"
                : anythingShown ? "Drag the overlay and the mini indicator anywhere"
                : "Turn on the overlay or the mini indicator first";
        }

        private void RefreshMonitorOptions()
        {
            MonitorDropdown.SetOptions(TrayApplicationContext.MonitorChoices().Select(c => new DropdownOption(c.Value, c.Key)), app.CurrentMonitorChoice());
        }

        #endregion

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
