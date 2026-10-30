using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Hotkeys;
using Muter.UI.Controls;

namespace Muter.UI
{
    // Picks a new global hotkey. Listens as soon as it opens: the pressed combination appears as keycaps, problems
    // are shown inline, and Save registers it (staying open with a message if another app already uses it).
    public partial class HotkeyForm : Form
    {
        #region Native

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        #endregion

        #region Fields

        // Tries to register and save the hotkey; returns null on success or an error message.
        private readonly Func<Hotkey, string> applyHotkey;
        private readonly Hotkey currentHotkey;
        private Hotkey? candidate;

        #endregion

        #region Initialization

        // Required by the Windows Forms designer.
        public HotkeyForm() : this(Hotkey.Default, _ => null)
        {
        }

        public HotkeyForm(Hotkey currentHotkey, Func<Hotkey, string> applyHotkey)
        {
            InitializeComponent();
            this.applyHotkey = applyHotkey ?? throw new ArgumentNullException(nameof(applyHotkey));
            this.currentHotkey = currentHotkey;
            Icon = AppResources.AppIcon;
            using (var icon = new Icon(AppResources.AppIcon, 32, 32))
                iconBox.Image = icon.ToBitmap();
            FormClosed += (s, e) => iconBox.Image?.Dispose();
            closeButton.Click += (s, e) => Close();
            cancelButton.Click += (s, e) => Close();
            ShowCurrent();
        }

        // Test access.
        internal Hotkey? Candidate => candidate;

        internal string StatusText => statusLabel.Text;

        internal bool CanSave => saveButton.Enabled;

        internal KeyCaptureState CaptureState => captureBox.State;

        internal string CapturedText => string.Join(" + ", captureBox.Keycaps);

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
            // Rounded corners on Windows 11; ignored elsewhere.
            int preference = DWMWCP_ROUND;
            try
            {
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            captureBox.Focus();
        }

        #endregion

        #region Hotkey Logic

        // Every key goes to the hotkey, except the plain keys that operate the dialog itself.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        // Returns true when the key was taken for the hotkey.
        internal bool HandleKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys modifierKeys = keyData & Keys.Modifiers;
            bool ctrlOrAlt = (modifierKeys & (Keys.Control | Keys.Alt)) != 0;

            if (!ctrlOrAlt && (key == Keys.Tab || key == Keys.Return || key == Keys.Escape || key == Keys.Space))
                return false; // move focus, Save, Cancel, press a button
            if (key == Keys.F4 && modifierKeys == Keys.Alt)
                return false; // close the dialog

            if (!Hotkey.IsValidKey(key))
            {
                ShowPending(ToModifiers(modifierKeys));
                return true;
            }

            SetCandidate(new Hotkey(key, ToModifiers(modifierKeys)));
            return true;
        }

        // Releasing all modifiers without pressing a key goes back to the last combination.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (captureBox.State == KeyCaptureState.Pending)
                ShowPending(ToModifiers(ModifierKeys));
        }

        internal void SetCandidate(Hotkey hotkey)
        {
            candidate = hotkey;
            captureBox.Keycaps = HotkeyText.Parts(hotkey);

            string problem = HotkeyText.Problem(hotkey);
            if (problem != null)
                ShowResult(KeyCaptureState.Invalid, problem, canSave: false);
            else if (hotkey.Equals(currentHotkey))
                ShowResult(KeyCaptureState.Idle, "This is your current hotkey. Press another combination to change it.", canSave: false);
            else
                ShowResult(KeyCaptureState.Valid, $"Press Save to use {HotkeyText.Format(hotkey)}.", canSave: true);
        }

        internal void Save()
        {
            if (!candidate.HasValue || !saveButton.Enabled)
                return;

            string error = applyHotkey(candidate.Value);
            if (error != null)
            {
                ShowResult(KeyCaptureState.Invalid, error, canSave: false);
                captureBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowCurrent()
        {
            candidate = null;
            if (currentHotkey.IsValid)
            {
                captureBox.Keycaps = HotkeyText.Parts(currentHotkey);
                ShowResult(KeyCaptureState.Idle, "Current hotkey. Press a new combination to change it.", canSave: false);
            }
            else
            {
                captureBox.Keycaps = null;
                ShowResult(KeyCaptureState.Idle, "Press a key combination, for example Ctrl + Shift + F9.", canSave: false);
            }
        }

        private void ShowPending(HotkeyModifiers modifiers)
        {
            if (modifiers == HotkeyModifiers.None)
            {
                if (candidate.HasValue)
                    SetCandidate(candidate.Value);
                else
                    ShowCurrent();
                return;
            }
            captureBox.Keycaps = HotkeyText.ModifierParts(modifiers);
            ShowResult(KeyCaptureState.Pending, "Now press the key to combine with it.", canSave: false);
        }

        private void ShowResult(KeyCaptureState state, string message, bool canSave)
        {
            captureBox.State = state;
            statusLabel.Text = message;
            statusLabel.ForeColor = state == KeyCaptureState.Invalid ? Theme.Error : Theme.TextSecondary;
            saveButton.Enabled = canSave;
        }

        private static HotkeyModifiers ToModifiers(Keys modifierKeys)
        {
            var modifiers = HotkeyModifiers.None;
            if ((modifierKeys & Keys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
            if ((modifierKeys & Keys.Control) != 0) modifiers |= HotkeyModifiers.Control;
            if ((modifierKeys & Keys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
            return modifiers;
        }

        #endregion

        #region UI Event Handlers

        private void SaveButton_Click(object sender, EventArgs e) => Save();

        private void ResetButton_Click(object sender, EventArgs e)
        {
            SetCandidate(Hotkey.Default);
            captureBox.Focus();
        }

        // The title bar (its empty area, the icon and the title) moves the borderless window.
        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCLIENT)
            {
                Point point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                if (point.Y < descriptionLabel.Top)
                    m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        #endregion
    }
}
