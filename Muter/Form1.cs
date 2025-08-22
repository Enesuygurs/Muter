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

        private readonly MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
        private MMDevice microphone;
        private bool isMuted = false;

        private readonly AudioDeviceWatcher watcher;

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
            watcher = new AudioDeviceWatcher();
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
            UpdateMicrophoneStatus();
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
                UpdateUINoDevice();
                return;
            }

            microphone.AudioEndpointVolume.Mute = !microphone.AudioEndpointVolume.Mute;
            UpdateUIForMuteState();
            RestartFadeOut();
        }

        // Updates the microphone status and UI.
        public void UpdateMicrophoneStatus()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateMicrophoneStatus));
                return;
            }

            if (IsMicrophoneAvailable())
                UpdateUIForMuteState();
            else
                UpdateUINoDevice();
        }

        // Checks if a default capture device is available.
        private bool IsMicrophoneAvailable()
        {
            try
            {
                microphone = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                return microphone != null;
            }
            catch (COMException)
            {
                return false;
            }
        }

        // Handles audio device change events.
        private void OnAudioDeviceChanged(object sender, EventArgs e) => UpdateMicrophoneStatus();

        #endregion

        #region UI Update Logic

        // Updates the UI based on the current mute state.
        private void UpdateUIForMuteState()
        {
            if (microphone == null) return;

            isMuted = microphone.AudioEndpointVolume.Mute;
            pictureBox1.BackgroundImage = isMuted ? mutedBackground : openedBackground;
            toggleText.Text = isMuted ? "OFF" : "ON";
            toggleText.Location = new Point(isMuted ? 44 : 46, 2);
            notifyIcon1.Icon = isMuted ? muteIcon : openIcon;
        }

        // Updates the UI when no microphone device is found.
        private void UpdateUINoDevice()
        {
            pictureBox1.BackgroundImage = noDeviceImage;
            notifyIcon1.Icon = noDeviceIcon;
            toggleText.Text = "No Device";
            toggleText.Location = new Point(24, 2);
            RestartFadeOut();
        }

        // Restarts the fade-out animation for the overlay.
        private void RestartFadeOut()
        {
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
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            watcher?.StopWatching();
        }

        #endregion
    }
}