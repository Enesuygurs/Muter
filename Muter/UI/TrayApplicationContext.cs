using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
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
    // The application: tray icon and menu, global hotkey, overlay windows and the microphone controller.
    // Everything runs on the UI thread; audio callbacks from other threads are posted here.
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        // Safety net in case a Windows notification is missed. Each poll re-reads every targeted device.
        public const int PollIntervalMs = 1000;
        private const int DeviceChangeDelayMs = 300;
        private static readonly int[] DisplayTimeChoicesMs = { 500, 1000, 2000, 3000, 5000, StatusOverlayForm.AlwaysVisible };
        private static readonly OverlayAnchor[] MiniIndicatorAnchors =
        {
            OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.TopRight,
            OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.BottomRight, OverlayAnchor.Custom,
        };

        private readonly Settings settings;
        private readonly IAudioEndpointProvider provider;
        private readonly IGlobalHotkey hotkey;
        private readonly StartupRegistration startup;
        private readonly MicrophoneController controller;
        private readonly StatusOverlayForm overlay;
        private readonly MiniIndicatorForm miniIndicator;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly Font menuFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        private readonly Timer pollTimer = new Timer { Interval = PollIntervalMs };
        private readonly Timer deviceChangeTimer = new Timer { Interval = DeviceChangeDelayMs };
        private readonly SynchronizationContext uiContext;
        private Hotkey configuredHotkey;
        private MicState? lastShownState;
        private int refreshQueued;
        private bool moveMode;
        private bool hotkeyDialogOpen;
        private bool shutDown;

        public TrayApplicationContext(Settings settings, IAudioEndpointProvider provider, IGlobalHotkey hotkey, StartupRegistration startup)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.hotkey = hotkey ?? throw new ArgumentNullException(nameof(hotkey));
            this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

            // Creating the first control installs the WindowsFormsSynchronizationContext for this thread.
            overlay = new StatusOverlayForm();
            miniIndicator = new MiniIndicatorForm();
            uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            overlay.Dragged += (s, e) => SaveCustomPosition(forOverlay: true, e);
            miniIndicator.Dragged += (s, e) => SaveCustomPosition(forOverlay: false, e);
            // A close request from outside (taskkill, Windows sign-out) exits the whole app cleanly instead of
            // leaving it running without its overlay windows.
            overlay.FormClosing += OnOverlayClosing;
            miniIndicator.FormClosing += OnOverlayClosing;

            controller = new MicrophoneController(provider, settings.selectedDeviceId, settings.syncDevices);
            controller.StatusChanged += (s, e) => UpdateUi(forceOverlay: false);
            provider.DevicesChanged += OnDevicesChanged;
            provider.MuteStateChanged += OnMuteStateChanged;

            menu = new ContextMenuStrip { Font = menuFont };
            menu.Opening += (s, e) =>
            {
                RebuildMenu();
                e.Cancel = false;
            };
            RebuildMenu();

            trayIcon = new NotifyIcon { ContextMenuStrip = menu, Icon = AppResources.TrayLive, Text = "Muter", Visible = true };
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ToggleMute();
            };

            hotkey.Pressed += (s, e) => ToggleMute();
            pollTimer.Tick += (s, e) => OnPoll();
            deviceChangeTimer.Tick += (s, e) =>
            {
                deviceChangeTimer.Stop();
                RefreshNow();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            ApplyOverlaySettings();
            RegisterConfiguredHotkey(notifyOnFailure: true);
            RepairStartupEntry();
            Log.Info($"DPI awareness: {DpiHelper.DescribeProcessAwareness()}; screens: "
                + string.Join(", ", ScreenInfo.All().Select(s => $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} at {DpiHelper.GetScale(s):0.##}x")));

            RefreshNow();
            UpdateUi(forceOverlay: true);
            pollTimer.Start();
        }

        internal MicrophoneController Controller => controller;

        internal StatusOverlayForm OverlayWindow => overlay;

        internal MiniIndicatorForm MiniIndicator => miniIndicator;

        internal NotifyIcon TrayIcon => trayIcon;

        internal ContextMenuStrip Menu => menu;

        internal bool PositionsUnlocked => moveMode;

        // Shared by the hotkey and a left click on the tray icon.
        internal void ToggleMute()
        {
            if (shutDown)
                return;
            try
            {
                controller.ToggleMute();
            }
            catch (Exception ex)
            {
                Log.Error("Toggling the microphone failed", ex);
            }
            UpdateUi(forceOverlay: true);
        }

        internal void RefreshNow()
        {
            if (shutDown)
                return;
            try
            {
                controller.Refresh();
            }
            catch (Exception ex)
            {
                Log.Error("Refreshing the microphone state failed", ex);
            }
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
            menu.Items.Add(Item($"Hotkey ({configuredHotkey})...", false, OpenHotkeyDialog));
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

        private void UpdateUi(bool forceOverlay)
        {
            if (shutDown)
                return;

            MicrophoneStatus status = controller.Status;
            StatusPresentation presentation = StatusPresentation.From(status, controller.IsAllDevicesMode);

            trayIcon.Icon = presentation.ShowsMuted ? AppResources.TrayMuted
                : presentation.Visual == StatusVisual.NoDevice ? AppResources.TrayNoDevice
                : AppResources.TrayLive;
            trayIcon.Text = presentation.Tooltip;
            overlay.SetPresentation(presentation);
            miniIndicator.SetPresentation(presentation);

            bool stateChanged = lastShownState != status.State;
            lastShownState = status.State;
            if (forceOverlay || stateChanged)
                overlay.ShowTransient();
        }

        private void OnPoll()
        {
            RefreshNow();
            if (shutDown)
                return;
            // Keep always-visible windows above other topmost windows and on the monitor under the mouse if configured.
            miniIndicator.ApplyVisibility();
            if (overlay.Visible && (overlay.MoveMode || overlay.DisplayTimeMs == StatusOverlayForm.AlwaysVisible))
            {
                overlay.UpdateLayout();
                overlay.ShowOnTop();
            }
        }

        // Called on a Windows audio thread.
        private void OnDevicesChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                controller.NotifyDevicesChanged();
                deviceChangeTimer.Stop();
                deviceChangeTimer.Start();
            });
        }

        // Called on a Windows audio thread. Coalesces bursts of notifications into one refresh.
        private void OnMuteStateChanged(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0)
                return;
            if (!Post(() =>
            {
                Interlocked.Exchange(ref refreshQueued, 0);
                RefreshNow();
            }))
            {
                Interlocked.Exchange(ref refreshQueued, 0);
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
                        Log.Error("Handling an audio notification failed", ex);
                    }
                }, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not post an audio notification to the UI thread", ex);
                return false;
            }
        }

        private void OnOverlayClosing(object sender, FormClosingEventArgs e)
        {
            if (shutDown)
                return;
            e.Cancel = true;
            Log.Info($"Close requested ({e.CloseReason}); exiting");
            Post(ExitThread);
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
                controller.NotifyDevicesChanged();
                RefreshNow();
                RegisterConfiguredHotkey(notifyOnFailure: true);
            });
        }

        #region Devices

        private ToolStripMenuItem BuildDevicesMenu()
        {
            var devicesMenu = Submenu("Devices");
            string selectedId = controller.TargetDeviceId;

            devicesMenu.DropDownItems.Add(Item("Default (System)", controller.IsDefaultMode, () => SelectDevice(null, "Default (System)"),
                "The default recording device and the default communications device."));
            devicesMenu.DropDownItems.Add(Item("All Devices", controller.IsAllDevicesMode, () => SelectDevice(MicrophoneController.AllDevicesId, "All Devices")));
            devicesMenu.DropDownItems.Add(new ToolStripSeparator());

            bool selectedFound = controller.IsDefaultMode || controller.IsAllDevicesMode;
            try
            {
                IReadOnlyList<AudioEndpointInfo> devices = controller.GetCaptureDevices();
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
            controller.SetTarget(deviceId);
            UpdateUi(forceOverlay: true);
        }

        private void ToggleSyncDevices()
        {
            settings.syncDevices = !settings.syncDevices;
            SaveSettings();
            controller.SyncDevices = settings.syncDevices;
            RefreshNow();
            UpdateUi(forceOverlay: true);
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
            configuredHotkey = Hotkey.TryFromSettings(settings.hotkeyKey, settings.hotkeyModifiers, out Hotkey fromSettings)
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
            deviceChangeTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            overlay.FormClosing -= OnOverlayClosing;
            miniIndicator.FormClosing -= OnOverlayClosing;
            provider.DevicesChanged -= OnDevicesChanged;
            provider.MuteStateChanged -= OnMuteStateChanged;

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
            deviceChangeTimer.Dispose();
            menuFont.Dispose();

            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not release the audio devices", ex);
            }
        }

        #endregion
    }
}
