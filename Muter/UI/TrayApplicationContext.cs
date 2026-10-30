using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Overlay;
using Muter.Properties;
using Timer = System.Windows.Forms.Timer;

namespace Muter.UI
{
    // The application: tray icon and menu, global hotkey and overlay windows. The microphones are handled by an
    // AudioWorker on its own thread and its results are posted here, so a stuck audio call cannot freeze the UI; it is
    // shown as UNKNOWN instead of leaving the last state on screen.
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        // Safety net in case a Windows notification is missed. Each poll re-reads every targeted device.
        public const int PollIntervalMs = 1000;

        // An audio call running longer than this is shown as "not responding" (UNKNOWN).
        public const int NotRespondingMs = 2000;

        // Every this many polls the tray icon is re-sent to Explorer, so a lost update cannot stay on screen.
        private const int TrayResendPolls = 10;
        private static readonly int[] DisplayTimeChoicesMs = { 500, 1000, 2000, 3000, 5000, StatusOverlayForm.AlwaysVisible };
        private static readonly OverlayAnchor[] MiniIndicatorAnchors =
        {
            OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.TopRight,
            OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.BottomRight, OverlayAnchor.Custom,
        };

        private readonly Settings settings;
        private readonly AudioWorker audio;
        private readonly IGlobalHotkey hotkey;
        private readonly StartupRegistration startup;
        private readonly StatusOverlayForm overlay;
        private readonly MiniIndicatorForm miniIndicator;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly Font menuFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        private readonly Timer pollTimer = new Timer { Interval = PollIntervalMs };
        private readonly SynchronizationContext uiContext;
        private readonly CloseRequestFilter closeRequestFilter;
        // A plain hidden top-level window, so "taskkill" always finds a window of ours to send its close request to.
        private readonly NativeWindow closeTarget = new NativeWindow();
        private MicrophoneStatus status = MicrophoneStatus.ReadFailure;
        private bool audioNotResponding;
        private Hotkey configuredHotkey;
        private MicState? lastShownState;
        private int pollCount;
        private bool alternateTrayIcons;
        private bool moveMode;
        private bool hotkeyDialogOpen;
        private bool shutDown;

        public TrayApplicationContext(Settings settings, Func<IAudioEndpointProvider> providerFactory, IGlobalHotkey hotkey, StartupRegistration startup)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.hotkey = hotkey ?? throw new ArgumentNullException(nameof(hotkey));
            this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
            if (providerFactory == null)
                throw new ArgumentNullException(nameof(providerFactory));

            // Creating the first control installs the WindowsFormsSynchronizationContext for this thread. The window
            // handles are created right away so close requests (taskkill, sign-out) always find them.
            overlay = new StatusOverlayForm();
            miniIndicator = new MiniIndicatorForm();
            GC.KeepAlive(overlay.Handle);
            GC.KeepAlive(miniIndicator.Handle);
            closeTarget.CreateHandle(new CreateParams { Caption = "Muter" });
            uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            overlay.Dragged += (s, e) => SaveCustomPosition(forOverlay: true, e);
            miniIndicator.Dragged += (s, e) => SaveCustomPosition(forOverlay: false, e);
            overlay.FormClosing += OnOverlayClosing;
            miniIndicator.FormClosing += OnOverlayClosing;

            menu = new ContextMenuStrip { Font = menuFont };
            menu.Opening += (s, e) =>
            {
                RebuildMenu();
                e.Cancel = false;
            };

            trayIcon = new NotifyIcon { ContextMenuStrip = menu, Icon = AppResources.TrayIcon(StatusVisual.Unknown, false), Text = "Muter", Visible = true };
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ToggleMute();
            };

            audio = new AudioWorker(providerFactory, settings.selectedDeviceId, settings.syncDevices);
            audio.StatusAvailable += (newStatus, userAction) => Post(() => OnStatus(newStatus, userAction));

            hotkey.Pressed += (s, e) => ToggleMute();
            pollTimer.Tick += (s, e) => OnPoll();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Application.AddMessageFilter(closeRequestFilter = new CloseRequestFilter(this));

            ApplyOverlaySettings();
            RegisterConfiguredHotkey(notifyOnFailure: true);
            RepairStartupEntry();
            RebuildMenu();
            Log.Info($"DPI awareness: {DpiHelper.DescribeProcessAwareness()}; screens: "
                + string.Join(", ", ScreenInfo.All().Select(s => $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} at {DpiHelper.GetScale(s):0.##}x")));

            // Until the first reading arrives the tray shows "unknown"; the overlay appears with the first real state.
            UpdateUi(forceOverlay: false, initial: true);
            audio.RequestRefresh();
            pollTimer.Start();
        }

        // Latest state reported by the audio thread.
        internal MicrophoneStatus Status => status;

        internal bool AudioNotResponding => audioNotResponding;

        internal bool IsAllDevicesMode => settings.selectedDeviceId == MicrophoneController.AllDevicesId;

        internal StatusOverlayForm OverlayWindow => overlay;

        internal MiniIndicatorForm MiniIndicator => miniIndicator;

        internal NotifyIcon TrayIcon => trayIcon;

        internal ContextMenuStrip Menu => menu;

        internal bool PositionsUnlocked => moveMode;

        internal IntPtr CloseRequestWindow => closeTarget.Handle;

        // For tests: waits for queued audio work and delivers its results to the UI.
        internal bool WaitForAudio(int timeoutMs = 3000)
        {
            bool idle = audio.WaitIdle(timeoutMs);
            for (int i = 0; i < 5; i++)
                Application.DoEvents();
            return idle;
        }

        // Shared by the hotkey and a left click on the tray icon.
        internal void ToggleMute()
        {
            if (!shutDown)
                audio.Toggle();
        }

        internal void RefreshNow()
        {
            if (!shutDown)
                audio.RequestRefresh();
        }

        // Rebuilds the tray menu so every check mark reflects the current settings and devices.
        internal void RebuildMenu()
        {
            DisposeItems(menu.Items);

            menu.Items.Add(BuildDevicesMenu());
            menu.Items.Add(Item("Sync Devices", settings.syncDevices, ToggleSyncDevices,
                "Keep every controlled microphone in the same mute state (All Devices and Default modes)."));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildOverlayMenu());
            menu.Items.Add(Item("Run Startup", SafeIsStartupEnabled(), ToggleStartup));
            menu.Items.Add(Item($"Hotkey ({HotkeyText.Format(configuredHotkey)})...", false, OpenHotkeyDialog));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Exit", false, ExitThread));
        }

        protected override void ExitThreadCore()
        {
            Shutdown();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Shutdown();
            base.Dispose(disposing);
        }

        private void OnStatus(MicrophoneStatus newStatus, bool userAction)
        {
            status = newStatus;
            if (audioNotResponding)
                Log.Info("Audio system responding again");
            audioNotResponding = false;
            UpdateUi(forceOverlay: userAction);
        }

        // Puts the current state on every surface. Each surface is updated independently so a failure in one cannot
        // leave another showing an old state; it is called again on every poll.
        private void UpdateUi(bool forceOverlay, bool initial = false)
        {
            if (shutDown)
                return;

            StatusPresentation presentation = audioNotResponding
                ? StatusPresentation.NotResponding(IsAllDevicesMode)
                : StatusPresentation.From(status, IsAllDevicesMode);

            Safely("tray icon", () =>
            {
                trayIcon.Icon = AppResources.TrayIcon(presentation.Visual, alternateTrayIcons);
                trayIcon.Text = presentation.Tooltip;
            });
            Safely("overlay", () => overlay.SetPresentation(presentation));
            Safely("mini indicator", () => miniIndicator.SetPresentation(presentation));

            if (initial)
                return;
            bool stateChanged = lastShownState != presentation.Visual.ToState();
            lastShownState = presentation.Visual.ToState();
            if (forceOverlay || stateChanged)
                Safely("overlay", overlay.ShowTransient);
        }

        private void OnPoll()
        {
            if (shutDown)
                return;

            if (audio.BusyFor.TotalMilliseconds > NotRespondingMs)
            {
                if (!audioNotResponding)
                    Log.Warn($"The audio system has not answered for {audio.BusyFor.TotalSeconds:0.#} s; showing the state as unknown");
                audioNotResponding = true;
            }
            else
            {
                audio.RequestRefresh();
            }

            if (++pollCount % TrayResendPolls == 0)
                alternateTrayIcons = !alternateTrayIcons;
            UpdateUi(forceOverlay: false);

            // Keep always-visible windows above other topmost windows and on the monitor under the mouse if configured.
            Safely("mini indicator", miniIndicator.ApplyVisibility);
            if (overlay.Visible && (overlay.MoveMode || overlay.DisplayTimeMs == StatusOverlayForm.AlwaysVisible))
            {
                Safely("overlay", () =>
                {
                    overlay.UpdateLayout();
                    overlay.ShowOnTop();
                });
            }
        }

        private static void Safely(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Updating the {what} failed", ex);
            }
        }

        private bool Post(Action action)
        {
            try
            {
                uiContext.Post(_ =>
                {
                    if (shutDown)
                        return;
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Handling a background notification failed", ex);
                    }
                }, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not post a notification to the UI thread", ex);
                return false;
            }
        }

        private void OnOverlayClosing(object sender, FormClosingEventArgs e)
        {
            if (shutDown)
                return;
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                // Never block sign-out or shutdown: allow it and clean up.
                Log.Info("Windows is signing out or shutting down; exiting");
                Post(ExitThread);
                return;
            }
            e.Cancel = true;
            RequestExit(e.CloseReason.ToString());
        }

        private void RequestExit(string reason)
        {
            Log.Info($"Close requested ({reason}); exiting");
            Post(ExitThread);
        }

        // "taskkill" (without /F) posts WM_CLOSE to one hidden top-level window of the process that it picks itself
        // (measured: one of the WinForms helper windows, e.g. the tray icon's). A WM_CLOSE posted to any live window of
        // ours therefore means "please exit": it is swallowed (so the helper window is not destroyed, which would break
        // the tray icon) and the app exits cleanly. The hotkey dialog and other forms close normally. A window that is
        // no longer registered (a collected NativeWindow posts WM_CLOSE to itself for cleanup) is left alone.
        private sealed class CloseRequestFilter : IMessageFilter
        {
            private const int WM_CLOSE = 0x0010;
            private readonly TrayApplicationContext owner;

            public CloseRequestFilter(TrayApplicationContext owner) => this.owner = owner;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_CLOSE || owner.shutDown)
                    return false;

                Control control = Control.FromHandle(m.HWnd);
                if (control is Form form && form != owner.overlay && form != owner.miniIndicator)
                    return false;
                if (control == null && NativeWindow.FromHandle(m.HWnd) == null)
                    return false;

                owner.RequestExit("WM_CLOSE");
                return true;
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                overlay.UpdateLayout();
                miniIndicator.ApplyVisibility();
            });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume)
                return;
            Post(() =>
            {
                audio.NotifyDevicesChanged();
                RegisterConfiguredHotkey(notifyOnFailure: true);
            });
        }

        #region Devices

        private ToolStripMenuItem BuildDevicesMenu()
        {
            var devicesMenu = Submenu("Devices");
            string selectedId = settings.selectedDeviceId;
            bool isDefault = string.IsNullOrWhiteSpace(selectedId);

            devicesMenu.DropDownItems.Add(Item("Default (System)", isDefault, () => SelectDevice(null, "Default (System)"),
                "The default recording device and the default communications device."));
            devicesMenu.DropDownItems.Add(Item("All Devices", IsAllDevicesMode, () => SelectDevice(MicrophoneController.AllDevicesId, "All Devices")));
            devicesMenu.DropDownItems.Add(new ToolStripSeparator());

            bool selectedFound = isDefault || IsAllDevicesMode;
            try
            {
                IReadOnlyList<AudioEndpointInfo> devices = audio.GetDevices(timeoutMs: 1000);
                foreach (AudioEndpointInfo device in devices)
                {
                    bool isSelected = string.Equals(device.Id, selectedId, StringComparison.OrdinalIgnoreCase);
                    selectedFound |= isSelected;
                    string id = device.Id;
                    string name = device.Name;
                    devicesMenu.DropDownItems.Add(Item(name, isSelected, () => SelectDevice(id, name)));
                }
                if (devices.Count == 0)
                    devicesMenu.DropDownItems.Add(new ToolStripMenuItem("(No active devices found)") { Enabled = false });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list the microphones for the menu", ex);
                devicesMenu.DropDownItems.Add(new ToolStripMenuItem("(Failed to list devices)") { Enabled = false });
            }

            if (!selectedFound)
            {
                string label = string.IsNullOrEmpty(settings.selectedDeviceName) ? "Device" : settings.selectedDeviceName;
                devicesMenu.DropDownItems.Add(new ToolStripMenuItem($"{label} (Disconnected)") { Checked = true, Enabled = false });
            }
            return devicesMenu;
        }

        private void SelectDevice(string deviceId, string deviceName)
        {
            settings.selectedDeviceId = deviceId ?? string.Empty;
            settings.selectedDeviceName = deviceName ?? string.Empty;
            SaveSettings();
            audio.SetTarget(deviceId);
        }

        private void ToggleSyncDevices()
        {
            settings.syncDevices = !settings.syncDevices;
            SaveSettings();
            audio.SetSync(settings.syncDevices);
        }

        #endregion


        #region Overlay settings

        private ToolStripMenuItem BuildOverlayMenu()
        {
            var overlayMenu = Submenu("Overlay");
            OverlayAnchor overlayAnchor = OverlayPlacement.ParseEnum(settings.overlayPosition, OverlayAnchor.LowerCenter);
            OverlaySize overlaySize = OverlayPlacement.ParseEnum(settings.overlaySize, OverlaySize.Medium);

            overlayMenu.DropDownItems.Add(Item("Show Overlay", settings.showGui, () =>
            {
                settings.showGui = !settings.showGui;
                SaveAndApplyOverlaySettings();
            }));

            var position = Submenu("Position");
            foreach (OverlayAnchor anchor in Enum.GetValues(typeof(OverlayAnchor)))
            {
                OverlayAnchor value = anchor;
                position.DropDownItems.Add(Item(AnchorLabel(anchor), overlayAnchor == anchor, () => SetOverlayAnchor(value)));
            }
            overlayMenu.DropDownItems.Add(position);

            var size = Submenu("Size");
            foreach (OverlaySize option in Enum.GetValues(typeof(OverlaySize)))
            {
                OverlaySize value = option;
                size.DropDownItems.Add(Item(option.ToString(), overlaySize == option, () =>
                {
                    settings.overlaySize = value.ToString();
                    SaveAndApplyOverlaySettings();
                }));
            }
            overlayMenu.DropDownItems.Add(size);

            var displayTime = Submenu("Display Time");
            foreach (int ms in DisplayTimeChoicesMs)
            {
                int value = ms;
                string label = ms == StatusOverlayForm.AlwaysVisible ? "Always Visible" : (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";
                displayTime.DropDownItems.Add(Item(label, settings.overlayDurationMs == ms, () =>
                {
                    settings.overlayDurationMs = value;
                    SaveAndApplyOverlaySettings();
                }));
            }
            overlayMenu.DropDownItems.Add(displayTime);

            overlayMenu.DropDownItems.Add(BuildMonitorMenu());
            overlayMenu.DropDownItems.Add(new ToolStripSeparator());

            overlayMenu.DropDownItems.Add(Item("Mini Indicator", settings.miniIndicatorEnabled, () =>
            {
                settings.miniIndicatorEnabled = !settings.miniIndicatorEnabled;
                SaveAndApplyOverlaySettings();
            }, "A small always-visible status square."));

            OverlayAnchor miniAnchor = OverlayPlacement.ParseEnum(settings.miniIndicatorPosition, OverlayAnchor.BottomRight);
            var miniPosition = Submenu("Mini Indicator Position");
            foreach (OverlayAnchor anchor in MiniIndicatorAnchors)
            {
                OverlayAnchor value = anchor;
                miniPosition.DropDownItems.Add(Item(AnchorLabel(anchor), miniAnchor == anchor, () => SetMiniIndicatorAnchor(value)));
            }
            overlayMenu.DropDownItems.Add(miniPosition);

            OverlaySize miniSize = OverlayPlacement.ParseEnum(settings.miniIndicatorSize, OverlaySize.Medium);
            var miniSizeMenu = Submenu("Mini Indicator Size");
            foreach (OverlaySize option in Enum.GetValues(typeof(OverlaySize)))
            {
                OverlaySize value = option;
                miniSizeMenu.DropDownItems.Add(Item(option.ToString(), miniSize == option, () =>
                {
                    settings.miniIndicatorSize = value.ToString();
                    SaveAndApplyOverlaySettings();
                }));
            }
            overlayMenu.DropDownItems.Add(miniSizeMenu);

            overlayMenu.DropDownItems.Add(new ToolStripSeparator());
            overlayMenu.DropDownItems.Add(Item("Unlock Positions (drag to move)", moveMode, () => SetMoveMode(!moveMode)));
            overlayMenu.DropDownItems.Add(Item("Reset Positions", false, ResetPositions));
            return overlayMenu;
        }

        private ToolStripMenuItem BuildMonitorMenu()
        {
            var monitorMenu = Submenu("Monitor");
            string current = settings.overlayMonitor ?? OverlayPlacement.PrimaryMonitor;
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.All();
            bool knownMonitor = current == OverlayPlacement.PrimaryMonitor || current == OverlayPlacement.FollowMouseMonitor
                || screens.Any(s => string.Equals(s.DeviceName, current, StringComparison.OrdinalIgnoreCase));

            monitorMenu.DropDownItems.Add(Item("Primary", current == OverlayPlacement.PrimaryMonitor || !knownMonitor, () => SetMonitor(OverlayPlacement.PrimaryMonitor)));
            monitorMenu.DropDownItems.Add(Item("Follow Mouse", current == OverlayPlacement.FollowMouseMonitor, () => SetMonitor(OverlayPlacement.FollowMouseMonitor)));
            monitorMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (ScreenInfo screen in screens)
            {
                string deviceName = screen.DeviceName;
                string label = $"{DisplayLabel(deviceName)} ({screen.Bounds.Width}x{screen.Bounds.Height}{(screen.IsPrimary ? ", primary" : string.Empty)})";
                monitorMenu.DropDownItems.Add(Item(label, string.Equals(current, deviceName, StringComparison.OrdinalIgnoreCase), () => SetMonitor(deviceName)));
            }
            return monitorMenu;
        }

        private void SetOverlayAnchor(OverlayAnchor anchor)
        {
            settings.overlayPosition = anchor.ToString();
            SaveAndApplyOverlaySettings();
            if (anchor == OverlayAnchor.Custom)
                SetMoveMode(true);
            else
                overlay.ShowTransient();
        }

        private void SetMiniIndicatorAnchor(OverlayAnchor anchor)
        {
            settings.miniIndicatorPosition = anchor.ToString();
            if (!settings.miniIndicatorEnabled)
                settings.miniIndicatorEnabled = true;
            SaveAndApplyOverlaySettings();
            if (anchor == OverlayAnchor.Custom)
                SetMoveMode(true);
        }

        private void SetMonitor(string monitor)
        {
            settings.overlayMonitor = monitor;
            SaveAndApplyOverlaySettings();
            overlay.ShowTransient();
        }

        private void SetMoveMode(bool enabled)
        {
            moveMode = enabled;
            overlay.SetMoveMode(enabled);
            miniIndicator.SetMoveMode(enabled);
            if (enabled && !settings.showGui && !settings.miniIndicatorEnabled)
                ShowBalloon("Nothing to move", "Turn on \"Show Overlay\" or \"Mini Indicator\" first.", ToolTipIcon.Info);
            else if (!enabled)
                overlay.ShowTransient();
        }

        private void ResetPositions()
        {
            settings.overlayPosition = OverlayAnchor.LowerCenter.ToString();
            settings.overlayMonitor = OverlayPlacement.PrimaryMonitor;
            settings.overlayCustomMonitor = string.Empty;
            settings.overlayCustomX = 0;
            settings.overlayCustomY = 0;
            settings.miniIndicatorPosition = OverlayAnchor.BottomRight.ToString();
            settings.miniIndicatorCustomMonitor = string.Empty;
            settings.miniIndicatorCustomX = 0;
            settings.miniIndicatorCustomY = 0;
            SaveAndApplyOverlaySettings();
            overlay.ShowTransient();
        }

        private void SaveCustomPosition(bool forOverlay, OverlayDraggedEventArgs e)
        {
            if (forOverlay)
            {
                settings.overlayPosition = OverlayAnchor.Custom.ToString();
                settings.overlayCustomMonitor = e.Monitor;
                settings.overlayCustomX = e.Offset.X;
                settings.overlayCustomY = e.Offset.Y;
            }
            else
            {
                settings.miniIndicatorPosition = OverlayAnchor.Custom.ToString();
                settings.miniIndicatorCustomMonitor = e.Monitor;
                settings.miniIndicatorCustomX = e.Offset.X;
                settings.miniIndicatorCustomY = e.Offset.Y;
            }
            SaveAndApplyOverlaySettings();
        }

        private void SaveAndApplyOverlaySettings()
        {
            SaveSettings();
            ApplyOverlaySettings();
        }

        private void ApplyOverlaySettings()
        {
            overlay.SetPlacement(new OverlayPlacementOptions
            {
                Anchor = OverlayPlacement.ParseEnum(settings.overlayPosition, OverlayAnchor.LowerCenter),
                Size = OverlayPlacement.ParseEnum(settings.overlaySize, OverlaySize.Medium),
                Monitor = settings.overlayMonitor ?? OverlayPlacement.PrimaryMonitor,
                CustomMonitor = settings.overlayCustomMonitor ?? string.Empty,
                CustomOffset = new Point(settings.overlayCustomX, settings.overlayCustomY),
            });
            overlay.Configure(settings.showGui, settings.overlayDurationMs);

            miniIndicator.SetPlacement(new OverlayPlacementOptions
            {
                Anchor = OverlayPlacement.ParseEnum(settings.miniIndicatorPosition, OverlayAnchor.BottomRight),
                Size = OverlayPlacement.ParseEnum(settings.miniIndicatorSize, OverlaySize.Medium),
                Monitor = settings.overlayMonitor ?? OverlayPlacement.PrimaryMonitor,
                CustomMonitor = settings.miniIndicatorCustomMonitor ?? string.Empty,
                CustomOffset = new Point(settings.miniIndicatorCustomX, settings.miniIndicatorCustomY),
            });
            miniIndicator.Configure(settings.miniIndicatorEnabled);
        }

        private static string AnchorLabel(OverlayAnchor anchor)
        {
            switch (anchor)
            {
                case OverlayAnchor.LowerCenter: return "Lower Center (default)";
                case OverlayAnchor.Custom: return "Custom (drag to move)";
                default: return Regex.Replace(anchor.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
            }
        }

        private static string DisplayLabel(string deviceName)
        {
            Match number = Regex.Match(deviceName ?? string.Empty, @"(\d+)$");
            return number.Success ? "Display " + number.Groups[1].Value : deviceName;
        }

        #endregion

        #region Hotkey and startup

        private void RegisterConfiguredHotkey(bool notifyOnFailure)
        {
            if (shutDown)
                return;
            configuredHotkey =Hotkey.TryFromSettings(settings.hotkeyKey, settings.hotkeyModifiers, out Hotkey fromSettings)
                ? fromSettings
                : Hotkey.Default;
            if (hotkey.IsRegistered && hotkey.Current.Equals(configuredHotkey))
                return;
            if (!hotkey.TrySet(configuredHotkey, out string error) && notifyOnFailure)
                ShowBalloon("Hotkey is not active", error + " Right-click the tray icon and choose Hotkey to pick another.", ToolTipIcon.Warning);
        }

        private void OpenHotkeyDialog()
        {
            if (hotkeyDialogOpen)
                return;
            hotkeyDialogOpen = true;
            // Release the hotkey so pressing it while choosing a new one does not toggle the microphone.
            hotkey.Suspend();
            try
            {
                using (var form = new HotkeyForm(configuredHotkey, TryApplyHotkey))
                    form.ShowDialog();
            }
            finally
            {
                hotkeyDialogOpen = false;
                RegisterConfiguredHotkey(notifyOnFailure: true);
            }
        }

        private string TryApplyHotkey(Hotkey newHotkey)
        {
            if (!hotkey.TrySet(newHotkey, out string error))
                return error;
            configuredHotkey = newHotkey;
            settings.hotkeyKey = (int)newHotkey.Key;
            settings.hotkeyModifiers = (int)newHotkey.Modifiers;
            settings.shortcut = newHotkey.ToString();
            SaveSettings();
            return null;
        }

        private bool SafeIsStartupEnabled()
        {
            try
            {
                return startup.IsEnabled();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the startup setting", ex);
                return false;
            }
        }

        private void ToggleStartup()
        {
            try
            {
                startup.SetEnabled(!startup.IsEnabled());
            }
            catch (Exception ex)
            {
                Log.Error("Could not change the startup setting", ex);
                MessageBox.Show("Could not change the startup setting:\n" + ex.Message, "Muter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RepairStartupEntry()
        {
            try
            {
                startup.RepairIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not check the startup entry", ex);
            }
        }

        #endregion

        #region Helpers

        private ToolStripMenuItem Submenu(string text)
        {
            var item = new ToolStripMenuItem(text);
            item.DropDown.Font = menuFont;
            return item;
        }

        private static ToolStripMenuItem Item(string text, bool isChecked, Action onClick, string tooltip = null)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked, ToolTipText = tooltip };
            item.Click += (s, e) =>
            {
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    Log.Error($"Menu action '{text}' failed", ex);
                }
            };
            return item;
        }

        private static void DisposeItems(ToolStripItemCollection items)
        {
            ToolStripItem[] existing = items.Cast<ToolStripItem>().ToArray();
            items.Clear();
            foreach (ToolStripItem item in existing)
            {
                if (item is ToolStripDropDownItem dropDown)
                    DisposeItems(dropDown.DropDownItems);
                item.Dispose();
            }
        }

        private void SaveSettings()
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Could not save the settings", ex);
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (shutDown)
                return;
            try
            {
                trayIcon?.ShowBalloonTip(5000, title, text, icon);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not show a notification", ex);
            }
        }

        private void Shutdown()
        {
            if (shutDown)
                return;
            shutDown = true;

            pollTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Application.RemoveMessageFilter(closeRequestFilter);
            closeTarget.DestroyHandle();
            overlay.FormClosing -= OnOverlayClosing;
            miniIndicator.FormClosing -= OnOverlayClosing;

            try
            {
                hotkey.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not release the hotkey", ex);
            }

            trayIcon.Visible = false;
            trayIcon.Dispose();
            DisposeItems(menu.Items);
            menu.Dispose();
            overlay.Dispose();
            miniIndicator.Dispose();
            pollTimer.Dispose();
            menuFont.Dispose();

            // Stops the audio thread, which releases the devices; never blocks exit for long.
            audio.Dispose();
        }

        #endregion
    }
}
