using Microsoft.Win32;
using NAudio.CoreAudioApi;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Muter
{
    public partial class Form1 : Form
    {
        #region Windows API Imports & Constants

        // Imports for registering and unregistering global hotkeys.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Constants for hotkey modifiers.
        private const int HOTKEY_ID = 1;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        // Constant for non-focusable window style.
        private const int WS_EX_NOACTIVATE = 0x08000000;

        #endregion

        #region Fields and Properties

        private MMDeviceEnumerator deviceEnumerator;
        private bool isMuted = false;
        private bool isLoaded = false;

        private readonly AudioDeviceWatcher watcher;

        private MMDeviceEnumerator GetDeviceEnumerator()
        {
            if (deviceEnumerator == null)
            {
                try { deviceEnumerator = new MMDeviceEnumerator(); } catch { }
            }
            return deviceEnumerator;
        }

        // UI Resources
        private readonly Bitmap mutedBackground = Properties.Resources.muteroffwhite;
        private readonly Bitmap openedBackground = Properties.Resources.muteronwhite;
        private readonly Bitmap noDeviceImage = Properties.Resources.nodevicewhite;
        private readonly Icon muteIcon = Properties.Resources.muteroff;
        private readonly Icon openIcon = Properties.Resources.muteron;
        private readonly Icon noDeviceIcon = Properties.Resources.nodevice;

        #endregion

        #region Form Initialization

        // Constructor for the main form.
        public Form1()
        {
            InitializeComponent();
            if (!Properties.Settings.Default.showGui)
            {
                this.Opacity = 0;
                fadeOutTimer.Stop();
            }
            string savedDeviceId = Properties.Settings.Default.selectedDeviceId;
            watcher = new AudioDeviceWatcher(savedDeviceId);
            watcher.AudioDeviceChanged += OnAudioDeviceChanged;
            watcher.StartWatching();
            notifyIcon1.MouseClick += new MouseEventHandler(notifyIcon1_MouseClick);
        }

        // Handles the form's load event.
        private void Form1_Load(object sender, EventArgs e)
        {
            CenterOverlay();
            LoadShortcutFromSettings();
            CheckAndToggleStartupStatus(isToggle: false);
            showGuiMenuItem.Checked = Properties.Settings.Default.showGui;
            if (!Properties.Settings.Default.showGui)
            {
                this.Opacity = 0;
                this.Hide();
                fadeOutTimer.Stop();
            }

            watcher.RefreshDevice();
            UpdateMicrophoneStatus();

            // Start periodic microphone checker to automatically detect devices after boot/sleep/hotplug
            microphoneChecker.Interval = 1500;
            microphoneChecker.Tick += MicrophoneChecker_Tick;
            microphoneChecker.Start();

            isLoaded = true;
        }

        // Periodically checks if audio devices have become available (especially after system boot or sleep).
        private void MicrophoneChecker_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!IsMicrophoneAvailable())
                {
                    watcher.RefreshDevice();
                    UpdateMicrophoneStatus();
                }
            }
            catch { }
        }

        // Prevents the form from gaining focus when shown.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // Centers the overlay window on the primary screen.
        private void CenterOverlay()
        {
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;
            this.Location = new Point(screenWidth / 2 - (this.Width - 75), screenHeight / 2 - (this.Height - 450));
        }

        #endregion

        #region Core Microphone Logic

        // Toggles the microphone's mute state.
        private void ToggleMute()
        {
            if (!IsMicrophoneAvailable())
            {
                watcher.RefreshDevice();
                if (!IsMicrophoneAvailable())
                {
                    UpdateUINoDevice();
                    return;
                }
            }

            try
            {
                bool newMuteState = watcher.ToggleMute();
                ApplyMuteStateToUI(newMuteState);
                RestartFadeOut();
            }
            catch
            {
                if (IsMicrophoneAvailable())
                {
                    ApplyMuteStateToUI(watcher.IsMuted);
                    RestartFadeOut();
                }
                else
                {
                    UpdateUINoDevice();
                }
            }
        }

        // Updates the microphone status and UI.
        public void UpdateMicrophoneStatus()
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateMicrophoneStatus));
                return;
            }

            if (IsMicrophoneAvailable())
                UpdateUIForMuteState();
            else
                UpdateUINoDevice();
        }

        // Checks if the target capture device is available.
        private bool IsMicrophoneAvailable()
        {
            return watcher != null && watcher.HasActiveDevices;
        }

        // Handles audio device change events.
        private void OnAudioDeviceChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateMicrophoneStatus));
                return;
            }

            UpdateMicrophoneStatus();
        }

        #endregion

        #region UI Update Logic

        // Applies mute state directly to UI elements without delay.
        private void ApplyMuteStateToUI(bool muted)
        {
            isMuted = muted;
            pictureBox1.BackgroundImage = isMuted ? mutedBackground : openedBackground;
            toggleText.Text = isMuted ? "OFF" : "ON";
            toggleText.Location = new Point(isMuted ? 44 : 46, 2);
            notifyIcon1.Icon = isMuted ? muteIcon : openIcon;

            string devName = watcher.DeviceDisplayName;
            title.Text = watcher.IsAllDevicesMode ? "All Devices" : "Microphone";
            title.Location = new Point(Math.Max(0, (topBar.Width - title.Width) / 2), 2);

            string tip = $"Muter - {devName} ({(isMuted ? "OFF" : "ON")})";
            notifyIcon1.Text = tip.Length > 63 ? tip.Substring(0, 60) + "..." : tip;
        }

        // Updates the UI based on the current mute state.
        private void UpdateUIForMuteState()
        {
            if (!IsMicrophoneAvailable())
            {
                UpdateUINoDevice();
                return;
            }

            try
            {
                bool previousMuted = isMuted;
                bool currentMuted = watcher.IsMuted;
                ApplyMuteStateToUI(currentMuted);

                if (isLoaded && previousMuted != currentMuted)
                {
                    RestartFadeOut();
                }
            }
            catch
            {
                // Never wipe UI state to No Device if microphone is available!
            }
        }

        // Updates the UI when no microphone device is found.
        private void UpdateUINoDevice()
        {
            pictureBox1.BackgroundImage = noDeviceImage;
            notifyIcon1.Icon = noDeviceIcon;
            toggleText.Text = "No Device";
            toggleText.Location = new Point(24, 2);
            title.Text = (watcher != null && watcher.IsAllDevicesMode) ? "All Devices" : "Microphone";
            title.Location = new Point(Math.Max(0, (topBar.Width - title.Width) / 2), 2);
            notifyIcon1.Text = "Muter - No Device";
            RestartFadeOut();
        }

        // Restarts the fade-out animation for the overlay.
        private void RestartFadeOut()
        {
            if (!Properties.Settings.Default.showGui)
            {
                this.Opacity = 0;
                this.Hide();
                fadeOutTimer.Stop();
                return;
            }

            this.Opacity = 1;
            this.Show();
            fadeOutTimer.Stop();
            fadeOutTimer.Start();
        }

        #endregion

        #region Hotkey and System Integration

        // Unregisters the old hotkey and registers a new one.
        public void UpdateHotkey(uint modifiers, Keys key)
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            RegisterHotKey(this.Handle, HOTKEY_ID, modifiers, (uint)key);
        }

        private void LoadShortcutFromSettings()
        {
            string savedShortcut = Properties.Settings.Default.shortcut;
            if (string.IsNullOrEmpty(savedShortcut)) return;

            string[] parts = savedShortcut.Split('+');
            uint modifiers = 0;
            Keys key = Keys.None;

            if (parts.Length > 0)
            {
                string keyPart = parts[parts.Length - 1].Trim();
                if (Enum.TryParse(keyPart, out Keys parsedKey))
                {
                    key = parsedKey;
                }
                else
                {
                    return;
                }

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string modifierPart = parts[i].Trim();
                    if (modifierPart.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                        modifiers |= MOD_SHIFT;
                    if (modifierPart.Equals("Control", StringComparison.OrdinalIgnoreCase))
                        modifiers |= MOD_CONTROL;
                    if (modifierPart.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                        modifiers |= MOD_ALT;
                }
            }

            if (key != Keys.None)
            {
                UpdateHotkey(modifiers, key);
            }
        }


        // Checks or toggles the application's startup status in the registry.
        private void CheckAndToggleStartupStatus(bool isToggle)
        {
            const string AppName = "Muter";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                bool isEnabled = key.GetValue(AppName) != null;

                if (isToggle)
                {
                    if (isEnabled)
                        key.DeleteValue(AppName);
                    else
                        key.SetValue(AppName, Application.ExecutablePath);

                    isEnabled = !isEnabled; // Update status after toggle
                }

                runStartup.Checked = isEnabled;
            }
        }

        // Overrides the window procedure to process hotkey messages.
        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleMute();
            }
            base.WndProc(ref m);
        }

        #endregion

        #region Event Handlers

        // Toggles mute on left-clicking the tray icon.
        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleMute();
        }

        // Shows the hotkey settings form.
        private void hotkeyForm_Click(object sender, EventArgs e)
        {
            using (hotkeyForm hotkeySettingsForm = new hotkeyForm())
            {
                hotkeySettingsForm.Owner = this;
                hotkeySettingsForm.ShowDialog();
            }
        }

        // Handles the fade-out timer tick event.
        private void fadeOutTimer_Tick_1(object sender, EventArgs e)
        {
            if (this.Opacity > 0)
            {
                this.Opacity -= 0.025;
            }
            else
            {
                fadeOutTimer.Stop();
                this.Hide();
            }
        }

        // Handles the click event for the "Run at Startup" menu item.
        private void runStartupToolStripMenuItem_Click(object sender, EventArgs e) => CheckAndToggleStartupStatus(isToggle: true);

        // Handles the click event for the "Exit" menu item.
        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Application.Exit();

        // Unregisters hotkey and stops the device watcher when the form is closing.
        private void Form1_FormClosing_1(object sender, FormClosingEventArgs e)
        {
            microphoneChecker?.Stop();
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            watcher?.StopWatching();
            try { deviceEnumerator?.Dispose(); } catch { }
        }

        // Handles the click event for the "Show GUI" menu item.
        private void showGuiMenuItem_Click(object sender, EventArgs e)
        {
            bool newStatus = !Properties.Settings.Default.showGui;
            Properties.Settings.Default.showGui = newStatus;
            Properties.Settings.Default.Save();
            showGuiMenuItem.Checked = newStatus;

            if (!newStatus)
            {
                this.Opacity = 0;
                this.Hide();
                fadeOutTimer.Stop();
            }
        }

        // Populates the "Devices" submenu dynamically with current recording devices.
        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            showGuiMenuItem.Checked = Properties.Settings.Default.showGui;
            PopulateDeviceMenu();
        }

        private void PopulateDeviceMenu()
        {
            defaultDeviceMenuItem.DropDownItems.Clear();

            string currentSelectedId = Properties.Settings.Default.selectedDeviceId;

            // 1. "Default (System)" item
            ToolStripMenuItem systemDefaultItem = new ToolStripMenuItem("Default (System)");
            systemDefaultItem.Checked = string.IsNullOrEmpty(currentSelectedId);
            systemDefaultItem.Click += (s, ev) => SelectAudioDevice(null, "Default (System)");
            defaultDeviceMenuItem.DropDownItems.Add(systemDefaultItem);

            // 2. "All Devices" item
            ToolStripMenuItem allDevicesItem = new ToolStripMenuItem("All Devices");
            allDevicesItem.Checked = (currentSelectedId == AudioDeviceWatcher.ALL_DEVICES_ID);
            allDevicesItem.Click += (s, ev) => SelectAudioDevice(AudioDeviceWatcher.ALL_DEVICES_ID, "All Devices");
            defaultDeviceMenuItem.DropDownItems.Add(allDevicesItem);

            defaultDeviceMenuItem.DropDownItems.Add(new ToolStripSeparator());

            // 3. Active capture devices
            bool selectedDeviceFound = (string.IsNullOrEmpty(currentSelectedId) || currentSelectedId == AudioDeviceWatcher.ALL_DEVICES_ID);
            try
            {
                var enumerator = GetDeviceEnumerator();
                MMDeviceCollection endpoints = null;
                try
                {
                    endpoints = enumerator?.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                }
                catch
                {
                    try
                    {
                        deviceEnumerator = new MMDeviceEnumerator();
                        endpoints = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                    }
                    catch { }
                }

                if (endpoints != null && endpoints.Count > 0)
                {
                    for (int i = 0; i < endpoints.Count; i++)
                    {
                        MMDevice endpoint = null;
                        try
                        {
                            endpoint = endpoints[i];
                            string id = endpoint.ID;
                            string friendlyName = endpoint.FriendlyName;
                            bool isCurrent = (!string.IsNullOrEmpty(currentSelectedId) && currentSelectedId == id);
                            if (isCurrent) selectedDeviceFound = true;

                            ToolStripMenuItem devItem = new ToolStripMenuItem(friendlyName);
                            devItem.Checked = isCurrent;
                            devItem.Click += (s, ev) => SelectAudioDevice(id, friendlyName);
                            defaultDeviceMenuItem.DropDownItems.Add(devItem);
                        }
                        catch
                        {
                            // Skip any device that was disconnected during enumeration
                        }
                        finally
                        {
                            try { endpoint?.Dispose(); } catch { }
                        }
                    }
                }
                else
                {
                    ToolStripMenuItem noDevicesItem = new ToolStripMenuItem("(No active devices found)") { Enabled = false };
                    defaultDeviceMenuItem.DropDownItems.Add(noDevicesItem);
                }
            }
            catch
            {
                ToolStripMenuItem errorItem = new ToolStripMenuItem("(Failed to list devices)") { Enabled = false };
                defaultDeviceMenuItem.DropDownItems.Add(errorItem);
            }

            // 4. If a specific device was previously chosen but is currently not connected
            if (!string.IsNullOrEmpty(currentSelectedId) && !selectedDeviceFound)
            {
                string savedName = Properties.Settings.Default.selectedDeviceName;
                string label = string.IsNullOrEmpty(savedName) ? "Device" : savedName;
                ToolStripMenuItem disconnectedItem = new ToolStripMenuItem($"{label} (Disconnected)")
                {
                    Checked = true,
                    Enabled = false
                };
                defaultDeviceMenuItem.DropDownItems.Add(disconnectedItem);
            }
        }

        // Sets the audio capture device to be muted/unmuted.
        private void SelectAudioDevice(string deviceId, string deviceName)
        {
            try
            {
                Properties.Settings.Default.selectedDeviceId = deviceId ?? string.Empty;
                Properties.Settings.Default.selectedDeviceName = deviceName ?? string.Empty;
                Properties.Settings.Default.Save();

                watcher.SetTargetDevice(deviceId);
                UpdateMicrophoneStatus();
            }
            catch (Exception)
            {
                UpdateUINoDevice();
            }
        }

        #endregion
    }
}