using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Localization;
using Muter.Overlay;
using Muter.Properties;
using Muter.UI.Controls;

namespace Muter.UI
{
    // All settings in one window: a side navigation with General, Hotkey, Overlay and Mini indicator pages. Every change
    // is applied immediately through the TrayApplicationContext, and the window follows changes made elsewhere (e.g. an
    // overlay dragged to a new position). It is built in the current language; changing the language opens it again.
    // Pages are identified by fixed keys (PageGeneral etc.), whatever the language.
    public class SettingsForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int TitleBarHeight = 40;
        private const int NavWidth = 184;
        private const int WindowWidth = 720;
        private const int ContentLeft = 204;
        private const int ContentWidth = 492;

        internal const string PageGeneral = "General";
        internal const string PageHotkey = "Hotkey";
        internal const string PageOverlay = "Overlay";
        internal const string PageMiniIndicator = "Mini indicator";

        private static readonly OverlaySize[] Sizes = { OverlaySize.Small, OverlaySize.Medium, OverlaySize.Large };
        private static Strings Lang => Strings.Current;

        private readonly TrayApplicationContext app;
        private readonly Settings settings;
        private readonly Dictionary<string, SettingsPage> pages = new Dictionary<string, SettingsPage>();
        private readonly Font ControlFont = new Font(Theme.FontFamily, 9F);
        private readonly Font ButtonMeasureFont = new Font(Theme.SemiboldFontFamily, 9F);
        private readonly Dictionary<string, NavButton> navButtons = new Dictionary<string, NavButton>();
        private readonly Panel content;
        private SettingRow positionsRow;
        private Label versionLabel;
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
            ClientSize = new Size(WindowWidth, 736);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Lang.SettingsWindowTitle;
            Icon = AppResources.AppIcon;
            KeyPreview = true;

            BuildTitleBar();
            BuildNavigation();
            content = new Panel { Location = new Point(ContentLeft, TitleBarHeight), Size = new Size(ContentWidth, 736 - TitleBarHeight), BackColor = Theme.Background };
            Controls.Add(content);
            BuildGeneralPage();
            BuildHotkeyPage();
            BuildOverlayPage();
            BuildMiniIndicatorPage();
            ResumeLayout(false);
            FormClosed += (s, e) =>
            {
                ControlFont.Dispose();
                ButtonMeasureFont.Dispose();
            };

            ShowPage(PageGeneral);
            RefreshDeviceOptions();
            RefreshFromSettings();
            FitHeight();
            app.SettingsChanged += OnSettingsChanged;
            FormClosed += (s, e) => app.SettingsChanged -= OnSettingsChanged;
        }

        #region Controls (test access)

        internal DropdownButton DevicesDropdown { get; private set; }

        internal ToggleSwitch SyncToggle { get; private set; }

        internal KeyCaptureBox HotkeyKeys { get; private set; }

        internal FlatButton ChangeHotkeyButton { get; private set; }

        internal ToggleSwitch StartupToggle { get; private set; }

        internal ToggleSwitch SoundToggle { get; private set; }

        internal ToggleSwitch PushToTalkToggle { get; private set; }

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

        internal DropdownButton LanguageDropdown { get; private set; }

        internal ToggleSwitch DiscreetTrayToggle { get; private set; }

        internal ToggleSwitch AppMuteToggle { get; private set; }

        internal KeyCaptureBox AppHotkeyKeys { get; private set; }

        internal FlatButton ChangeAppHotkeyButton { get; private set; }

        internal PreviewPanel OverlayPreview { get; private set; }

        internal FlatButton ShowNowButton { get; private set; }

        internal PreviewPanel MiniPreview { get; private set; }

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
            using (var pen = new Pen(Theme.WindowBorder))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        #region Layout

        private void BuildTitleBar()
        {
            var icon = new PictureBox { Location = new Point(14, 12), Size = new Size(16, 16), SizeMode = PictureBoxSizeMode.Zoom };
            using (var small = new Icon(AppResources.AppIcon, 32, 32))
                icon.Image = small.ToBitmap();
            FormClosed += (s, e) => icon.Image?.Dispose();
            var title = new Label
            {
                Text = Lang.SettingsTitle,
                AutoSize = true,
                Location = new Point(38, 12),
                Font = new Font(Theme.FontFamily, 9F),
                ForeColor = Theme.TextPrimary,
            };
            var close = new FlatButton
            {
                ButtonStyle = FlatButtonStyle.Close,
                Text = "",
                Font = new Font(Theme.IconFontFamily, 8F),
                Location = new Point(WindowWidth - 47, 1),
                Size = new Size(46, TitleBarHeight - 1),
                TabStop = false,
            };
            close.Click += (s, e) => Close();
            icon.MouseDown += TitleBar_MouseDown;
            title.MouseDown += TitleBar_MouseDown;
            Controls.AddRange(new Control[] { icon, title, close });
        }

        private void BuildNavigation()
        {
            int y = TitleBarHeight + 12;
            foreach (var entry in new[]
            {
                (PageGeneral, Lang.PageGeneral, NavIcon.Glyph, ""),
                (PageHotkey, Lang.PageHotkey, NavIcon.Glyph, ""),
                (PageOverlay, Lang.PageOverlay, NavIcon.OverlayScreen, ""),
                (PageMiniIndicator, Lang.PageMiniIndicator, NavIcon.MiniScreen, ""),
            })
            {
                string page = entry.Item1;
                var button = new NavButton { Text = entry.Item2, Icon = entry.Item3, Glyph = entry.Item4, Location = new Point(8, y), Size = new Size(NavWidth - 16, 36) };
                button.Click += (s, e) => ShowPage(page);
                navButtons[page] = button;
                Controls.Add(button);
                y += 40;
            }

            versionLabel = new Label
            {
                Text = "Muter " + VersionText(),
                AutoSize = true,
                Location = new Point(20, 0),
                ForeColor = Theme.TextDisabled,
                Font = new Font(Theme.FontFamily, 8.25F),
            };
            Controls.Add(versionLabel);
        }

        // "1.1" or "1.1.2" from this assembly (Application.ProductVersion would be the host process when tested).
        internal static string VersionText()
        {
            Version version = typeof(SettingsForm).Assembly.GetName().Version;
            return version.Build > 0 ? version.ToString(3) : version.ToString(2);
        }

        private SettingsPage NewPage(string key, string title)
        {
            var page = new SettingsPage { Location = Point.Empty, Size = content.Size, Visible = false };
            page.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Theme.SemiboldFontFamily, 15F),
                ForeColor = Theme.TextPrimary,
            });
            pages[key] = page;
            content.Controls.Add(page);
            return page;
        }

        // A section: an optional header and its card.
        private static SettingsCard AddSection(SettingsPage page, string header)
        {
            if (header != null)
            {
                page.Controls.Add(new Label
                {
                    Text = header,
                    AutoSize = true,
                    Font = new Font(Theme.SemiboldFontFamily, 9F),
                    ForeColor = Theme.TextSecondary,
                });
            }
            var card = new SettingsCard { Width = page.Width };
            page.Controls.Add(card);
            return card;
        }

        private SettingRow AddRow(SettingsCard card, string title, string description, Control setting)
        {
            var row = new SettingRow
            {
                Title = title,
                Description = description ?? string.Empty,
                Width = card.Width - 2 * SettingRow.Inset,
                Font = Font,
            };
            row.Setting = setting;
            card.AddRow(row);
            return row;
        }

        private DropdownButton NewDropdown() => new DropdownButton { Size = new Size(208, 32), Font = ControlFont };

        private SegmentedControl NewSizeSegments() => new SegmentedControl { Size = new Size(228, 32), Font = ControlFont, Segments = Sizes.Select(TrayApplicationContext.SizeLabel).ToArray() };

        private FlatButton NewButton(string text, params string[] alternatives)
        {
            int width = alternatives.Concat(new[] { text }).Max(t => TextRenderer.MeasureText(t, ButtonMeasureFont).Width) + 32;
            return new FlatButton { Text = text, Size = new Size(Math.Max(72, width), 32), Font = ControlFont, BackColor = Theme.Surface };
        }

        // The keycaps of a hotkey followed by a Change button.
        private Panel NewHotkeyControl(out KeyCaptureBox keys, out FlatButton change)
        {
            change = NewButton(Lang.Change);
            keys = new KeyCaptureBox { Compact = true, Location = new Point(0, 2), Size = new Size(184, 28), TabStop = false, Font = new Font(Theme.SemiboldFontFamily, 9F) };
            change.Location = new Point(keys.Width + 12, 0);
            var panel = new Panel { Size = new Size(change.Right, 32), BackColor = Theme.Surface };
            keys.BackColor = Theme.Surface;
            change.BackColor = Theme.Surface;
            panel.Controls.Add(keys);
            panel.Controls.Add(change);
            return panel;
        }

        private void BuildGeneralPage()
        {
            SettingsPage page = NewPage(PageGeneral, Lang.PageGeneral);

            SettingsCard card = AddSection(page, Lang.SectionStartup);
            StartupToggle = new ToggleSwitch();
            StartupToggle.Toggled += (s, e) => Apply(() => app.SetStartup(StartupToggle.Checked));
            AddRow(card, Lang.StartWithWindows, Lang.StartWithWindowsHint, StartupToggle);

            card = AddSection(page, Lang.SectionMicrophone);
            DevicesDropdown = NewDropdown();
            DevicesDropdown.DropDownOpening += (s, e) => RefreshDeviceOptions();
            DevicesDropdown.SelectionChanged += (s, e) =>
            {
                string id = DevicesDropdown.SelectedValue as string;
                string name = DevicesDropdown.SelectedText;
                Apply(() => app.SelectDevice(string.IsNullOrEmpty(id) ? null : id, name));
            };
            AddRow(card, Lang.Microphones, Lang.MicrophonesHint, DevicesDropdown);

            SyncToggle = new ToggleSwitch();
            SyncToggle.Toggled += (s, e) => Apply(() => app.SetSyncDevices(SyncToggle.Checked));
            AddRow(card, Lang.KeepInSync, Lang.KeepInSyncHint, SyncToggle);

            // Settings shared by the overlay and the mini indicator.
            card = AddSection(page, Lang.SectionScreen);
            MonitorDropdown = NewDropdown();
            MonitorDropdown.DropDownOpening += (s, e) => RefreshMonitorOptions();
            MonitorDropdown.SelectionChanged += (s, e) => Apply(() => app.SetMonitor((string)MonitorDropdown.SelectedValue));
            AddRow(card, Lang.Monitor, Lang.MonitorHint, MonitorDropdown);

            // Move turns into Done, so its width fits both words.
            MovePositionsButton = NewButton(Lang.PositionsMove, Lang.PositionsDone);
            MovePositionsButton.Click += (s, e) => Apply(() => app.SetPositionsUnlocked(!app.PositionsUnlocked));
            ResetPositionsButton = NewButton(Lang.PositionsReset);
            ResetPositionsButton.Click += (s, e) => Apply(app.ResetPositions);
            ResetPositionsButton.Location = new Point(MovePositionsButton.Width + 8, 0);
            var positionsPanel = new Panel { Size = new Size(ResetPositionsButton.Right, 32), BackColor = Theme.Surface };
            positionsPanel.Controls.Add(MovePositionsButton);
            positionsPanel.Controls.Add(ResetPositionsButton);
            positionsRow = AddRow(card, Lang.Positions, string.Empty, positionsPanel);

            card = AddSection(page, Lang.SectionTrayIcon);
            DiscreetTrayToggle = new ToggleSwitch();
            DiscreetTrayToggle.Toggled += (s, e) => Apply(() => app.SetDiscreetTrayIcon(DiscreetTrayToggle.Checked));
            AddRow(card, Lang.DiscreetTrayIcon, Lang.DiscreetTrayIconHint, DiscreetTrayToggle);

            card = AddSection(page, Lang.SectionLanguage);
            LanguageDropdown = NewDropdown();
            LanguageDropdown.SetOptions(new[] { new DropdownOption(Lang.LanguageSystem, Languages.System) }
                .Concat(Languages.All.Select(l => new DropdownOption(l.NativeName, l.Code))), null);
            LanguageDropdown.SelectionChanged += (s, e) => Apply(() => app.SetLanguage((string)LanguageDropdown.SelectedValue));
            AddRow(card, Lang.Language, Lang.LanguageHint, LanguageDropdown);
        }

        private void BuildHotkeyPage()
        {
            SettingsPage page = NewPage(PageHotkey, Lang.PageHotkey);

            SettingsCard card = AddSection(page, Lang.SectionHotkey);
            Panel hotkeyPanel = NewHotkeyControl(out KeyCaptureBox hotkeyKeys, out FlatButton changeHotkey);
            HotkeyKeys = hotkeyKeys;
            ChangeHotkeyButton = changeHotkey;
            ChangeHotkeyButton.Click += (s, e) => app.OpenHotkeyDialog(this);
            AddRow(card, Lang.MuteHotkey, Lang.MuteHotkeyHint, hotkeyPanel);

            PushToTalkToggle = new ToggleSwitch();
            PushToTalkToggle.Toggled += (s, e) => Apply(() => app.SetPushToTalk(PushToTalkToggle.Checked));
            AddRow(card, Lang.PushToTalk, Lang.PushToTalkHint, PushToTalkToggle);

            card = AddSection(page, Lang.SectionActiveApp);
            AppMuteToggle = new ToggleSwitch();
            AppMuteToggle.Toggled += (s, e) => Apply(() => app.SetAppMuteEnabled(AppMuteToggle.Checked));
            AddRow(card, Lang.MuteActiveApp, Lang.MuteActiveAppHint, AppMuteToggle);

            Panel appHotkeyPanel = NewHotkeyControl(out KeyCaptureBox appKeys, out FlatButton changeApp);
            AppHotkeyKeys = appKeys;
            ChangeAppHotkeyButton = changeApp;
            ChangeAppHotkeyButton.Click += (s, e) => app.OpenAppHotkeyDialog(this);
            AddRow(card, Lang.AppHotkey, Lang.AppHotkeyHint, appHotkeyPanel);

            card = AddSection(page, Lang.SectionSound);
            SoundToggle = new ToggleSwitch();
            SoundToggle.Toggled += (s, e) => Apply(() => app.SetSoundFeedback(SoundToggle.Checked));
            AddRow(card, Lang.SoundFeedback, Lang.SoundFeedbackHint, SoundToggle);
        }

        private void BuildOverlayPage()
        {
            SettingsPage page = NewPage(PageOverlay, Lang.PageOverlay);

            SettingsCard card = AddSection(page, null);
            ShowOverlayToggle = new ToggleSwitch();
            ShowOverlayToggle.Toggled += (s, e) => Apply(() => app.SetShowOverlay(ShowOverlayToggle.Checked));
            AddRow(card, Lang.ShowOverlay, Lang.ShowOverlayHint, ShowOverlayToggle);

            PositionDropdown = NewDropdown();
            PositionDropdown.SetOptions(Enum.GetValues(typeof(OverlayAnchor)).Cast<OverlayAnchor>()
                .Select(a => new DropdownOption(TrayApplicationContext.AnchorLabel(a), a)), null);
            PositionDropdown.SelectionChanged += (s, e) => Apply(() => app.SetOverlayAnchor((OverlayAnchor)PositionDropdown.SelectedValue));
            AddRow(card, Lang.Position, Lang.PositionHint, PositionDropdown);

            SizeSegments = NewSizeSegments();
            SizeSegments.SelectionChanged += (s, e) => Apply(() => app.SetOverlaySize(Sizes[SizeSegments.SelectedIndex]));
            AddRow(card, Lang.Size, null, SizeSegments);

            DisplayTimeDropdown = NewDropdown();
            DisplayTimeDropdown.SetOptions(TrayApplicationContext.DisplayTimeChoices.Select(ms => new DropdownOption(TrayApplicationContext.DisplayTimeLabel(ms), ms)), null);
            DisplayTimeDropdown.SelectionChanged += (s, e) => Apply(() => app.SetDisplayTime((int)DisplayTimeDropdown.SelectedValue));
            AddRow(card, Lang.DisplayTime, Lang.DisplayTimeHint, DisplayTimeDropdown);

            card = AddSection(page, Lang.SectionPreview);
            OverlayPreview = new PreviewPanel { ForOverlay = true, Size = new Size(card.Width - 2 * SettingRow.Inset, 156), Font = Font };
            ShowNowButton = NewButton(Lang.ShowNow);
            ShowNowButton.Click += (s, e) => Apply(app.ShowOverlayNow);
            OverlayPreview.AddButton(ShowNowButton);
            card.AddRow(OverlayPreview);
        }

        private void BuildMiniIndicatorPage()
        {
            SettingsPage page = NewPage(PageMiniIndicator, Lang.PageMiniIndicator);

            SettingsCard card = AddSection(page, null);
            MiniIndicatorToggle = new ToggleSwitch();
            MiniIndicatorToggle.Toggled += (s, e) => Apply(() => app.SetMiniIndicatorEnabled(MiniIndicatorToggle.Checked));
            AddRow(card, Lang.ShowMiniIndicator, Lang.ShowMiniIndicatorHint, MiniIndicatorToggle);

            MiniPositionDropdown = NewDropdown();
            MiniPositionDropdown.SetOptions(TrayApplicationContext.MiniIndicatorAnchorChoices.Select(a => new DropdownOption(TrayApplicationContext.AnchorLabel(a), a)), null);
            MiniPositionDropdown.SelectionChanged += (s, e) => Apply(() => app.SetMiniIndicatorAnchor((OverlayAnchor)MiniPositionDropdown.SelectedValue));
            AddRow(card, Lang.Position, Lang.PositionHint, MiniPositionDropdown);

            MiniSizeSegments = NewSizeSegments();
            MiniSizeSegments.SelectionChanged += (s, e) => Apply(() => app.SetMiniIndicatorSize(Sizes[MiniSizeSegments.SelectedIndex]));
            AddRow(card, Lang.Size, null, MiniSizeSegments);

            card = AddSection(page, Lang.SectionPreview);
            MiniPreview = new PreviewPanel { ForOverlay = false, Size = new Size(card.Width - 2 * SettingRow.Inset, 156), Font = Font };
            card.AddRow(MiniPreview);
        }

        // The window is as tall as its longest page.
        private void FitHeight()
        {
            float s = DeviceDpi / 96f;
            foreach (SettingsPage page in pages.Values)
                page.PerformLayout();
            int contentHeight = pages.Values.Max(p => p.ContentHeight);
            int height = content.Top + contentHeight + (int)(24 * s);
            ClientSize = new Size(ClientSize.Width, height);
            content.Height = height - content.Top;
            foreach (SettingsPage page in pages.Values)
                page.Height = content.Height;
            versionLabel.Top = height - (int)(36 * s);
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
                FitHotkeyControl(HotkeyKeys, ChangeHotkeyButton);
                AppMuteToggle.Checked = settings.appMuteEnabled;
                AppHotkeyKeys.Keycaps = HotkeyText.Parts(app.ConfiguredAppHotkey);
                FitHotkeyControl(AppHotkeyKeys, ChangeAppHotkeyButton);
                StartupToggle.Checked = app.IsStartupEnabled();
                DiscreetTrayToggle.Checked = settings.discreetTrayIcon;
                SoundToggle.Checked = settings.soundFeedback;
                PushToTalkToggle.Checked = settings.pushToTalk;

                ShowOverlayToggle.Checked = settings.showGui;
                PositionDropdown.SelectedValue = OverlayPlacement.ParseEnum(settings.overlayPosition, OverlayAnchor.LowerCenter);
                SizeSegments.SelectedIndex = Array.IndexOf(Sizes, OverlayPlacement.ParseEnum(settings.overlaySize, OverlaySize.Medium));
                DisplayTimeDropdown.SelectedValue = TrayApplicationContext.DisplayTimeChoices.Contains(settings.overlayDurationMs) ? settings.overlayDurationMs : 1000;
                RefreshMonitorOptions();
                RefreshPositionsRow();

                MiniIndicatorToggle.Checked = settings.miniIndicatorEnabled;
                MiniPositionDropdown.SelectedValue = OverlayPlacement.ParseEnum(settings.miniIndicatorPosition, OverlayAnchor.BottomRight);
                MiniSizeSegments.SelectedIndex = Array.IndexOf(Sizes, OverlayPlacement.ParseEnum(settings.miniIndicatorSize, OverlaySize.Medium));
                RefreshPreviews();

                LanguageDropdown.SelectedValue = Languages.All.Any(l => l.Code == settings.language) ? settings.language : Languages.System;
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
                options.Add(new DropdownOption(Lang.DevicesListFailed, "\0failed", enabled: false));
            DevicesDropdown.SetOptions(options, app.SelectedDeviceId ?? string.Empty);
        }

        // Move turns into a highlighted Done while the overlays can be dragged; it is unavailable when nothing is shown.
        private void RefreshPositionsRow()
        {
            bool unlocked = app.PositionsUnlocked;
            bool anythingShown = settings.showGui || settings.miniIndicatorEnabled;
            MovePositionsButton.Text = unlocked ? Lang.PositionsDone : Lang.PositionsMove;
            MovePositionsButton.ButtonStyle = unlocked ? FlatButtonStyle.Primary : FlatButtonStyle.Secondary;
            MovePositionsButton.Enabled = unlocked || anythingShown;
            positionsRow.DescriptionColor = unlocked ? Theme.Link : Color.Empty;
            positionsRow.Description = unlocked ? Lang.PositionsHintMoving
                : anythingShown ? Lang.PositionsHint
                : Lang.PositionsHintNothingShown;
        }

        // The keycaps take the width they need (up to a limit), the Change button follows them.
        private void FitHotkeyControl(KeyCaptureBox keys, FlatButton change)
        {
            float s = DeviceDpi / 96f;
            int width = Math.Min(keys.CompactWidth(), (int)(220 * s));
            if (keys.Width == width)
                return;
            keys.Width = width;
            change.Left = width + (int)(12 * s);
            Control panel = keys.Parent;
            panel.Width = change.Right;
            panel.Parent?.PerformLayout();
            panel.Parent?.Parent?.PerformLayout();
        }

        private void RefreshPreviews()
        {
            var anchor = (OverlayAnchor)PositionDropdown.SelectedValue;
            OverlaySize size = Sizes[Math.Max(0, SizeSegments.SelectedIndex)];
            OverlayPreview.SetPreview(anchor, size, CustomFraction(settings.overlayCustomMonitor, settings.overlayCustomX, settings.overlayCustomY), settings.showGui,
                TrayApplicationContext.AnchorLabel(anchor),
                TrayApplicationContext.SizeLabel(size) + ", " + TrayApplicationContext.DisplayTimeLabel((int)DisplayTimeDropdown.SelectedValue));
            ShowNowButton.Enabled = settings.showGui;

            var miniAnchor = (OverlayAnchor)MiniPositionDropdown.SelectedValue;
            OverlaySize miniSize = Sizes[Math.Max(0, MiniSizeSegments.SelectedIndex)];
            MiniPreview.SetPreview(miniAnchor, miniSize, CustomFraction(settings.miniIndicatorCustomMonitor, settings.miniIndicatorCustomX, settings.miniIndicatorCustomY),
                settings.miniIndicatorEnabled, TrayApplicationContext.AnchorLabel(miniAnchor), TrayApplicationContext.SizeLabel(miniSize));
        }

        // A dragged position as a fraction of its monitor's working area (for the preview).
        private static PointF CustomFraction(string monitor, int x, int y)
        {
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.All();
            ScreenInfo screen = screens.FirstOrDefault(sc => string.Equals(sc.DeviceName, monitor, StringComparison.OrdinalIgnoreCase))
                ?? screens.FirstOrDefault(sc => sc.IsPrimary) ?? screens.FirstOrDefault();
            if (screen == null || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
                return PointF.Empty;
            return new PointF((float)x / screen.WorkingArea.Width, (float)y / screen.WorkingArea.Height);
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
