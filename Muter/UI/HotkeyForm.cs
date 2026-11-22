using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Hotkeys;
using Muter.Localization;
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
        private readonly Hotkey defaultHotkey;
        private Hotkey? candidate;
        private readonly Label statusGlyph = new Label
        {
            AutoSize = false,
            Size = new Size(16, 20),
            Font = new Font(Theme.IconFontFamily, 10F),
            TextAlign = ContentAlignment.TopCenter,
            UseMnemonic = false,
        };
        private int footerTop;

        #endregion

        #region Initialization

        // Required by the Windows Forms designer.
        public HotkeyForm() : this(Hotkey.Default, _ => null)
        {
        }

        // title, description and defaultHotkey: the microphone hotkey's unless given (the app hotkey uses its own).
        public HotkeyForm(Hotkey currentHotkey, Func<Hotkey, string> applyHotkey, string title = null, string description = null, Hotkey? defaultHotkey = null)
        {
            InitializeComponent();
            this.defaultHotkey = defaultHotkey ?? Hotkey.Default;
            ApplyLanguage();
            if (title != null)
                titleLabel.Text = title;
            if (description != null)
                descriptionLabel.Text = description;
            Controls.Add(statusGlyph);
            FormClosed += (s, e) => statusGlyph.Font.Dispose();
            LayoutDialog();
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

        private void ApplyLanguage()
        {
            Strings text = Strings.Current;
            Text = text.HotkeyWindowTitle;
            titleLabel.Text = text.MuteHotkey;
            descriptionLabel.Text = text.HotkeyDescription;
            captureBox.Placeholder = text.HotkeyPlaceholder;
            cancelButton.Text = text.Cancel;
            saveButton.Text = text.Save;
            resetButton.Text = text.ResetToDefault;

            // Buttons grow for longer translations, keeping their right edge; the link may use the space up to them.
            int right = saveButton.Right;
            saveButton.Width = Math.Max(saveButton.Width, TextRenderer.MeasureText(saveButton.Text, saveButton.Font).Width + 28);
            saveButton.Left = right - saveButton.Width;
            cancelButton.Width = Math.Max(cancelButton.Width, TextRenderer.MeasureText(cancelButton.Text, cancelButton.Font).Width + 28);
            cancelButton.Left = saveButton.Left - 8 - cancelButton.Width;
            int needed = TextRenderer.MeasureText(resetButton.Text, resetButton.Font).Width + 12;
            resetButton.Width = Math.Min(cancelButton.Left - resetButton.Left - 8, Math.Max(resetButton.Width, needed));
        }

        // Height of the longest status message in this language, with a long combination in it.
        private int LongestStatusHeight()
        {
            Strings text = Strings.Current;
            string longKey = HotkeyText.Format(new Hotkey(Keys.PageDown, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
            int height = 0;
            foreach (string message in new[]
            {
                text.HotkeyIsCurrent, text.HotkeyCurrent, text.HotkeyPending, text.HotkeyInvalidKey, text.HotkeyChooseValid,
                string.Format(text.HotkeyPressSave, longKey), string.Format(text.HotkeyExample, longKey),
                string.Format(text.HotkeyReserved, longKey), string.Format(text.HotkeyNeedsCtrlOrAlt, longKey),
                string.Format(text.HotkeyTaken, longKey),
            })
            {
                height = Math.Max(height, TextRenderer.MeasureText(message, statusLabel.Font, new Size(statusLabel.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + statusLabel.Padding.Vertical);
            }
            return height;
        }

        // Stacks the dialog for the description's height: description, capture area (96), status line, footer band (64).
        private void LayoutDialog()
        {
            float s = DeviceDpi / 96f;
            int descriptionHeight = TextRenderer.MeasureText(descriptionLabel.Text, descriptionLabel.Font, new Size(descriptionLabel.Width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            descriptionLabel.Height = descriptionHeight;
            statusLabel.Height = Math.Max((int)(34 * s), LongestStatusHeight());
            captureBox.Top = descriptionLabel.Bottom + (int)(16 * s);
            statusLabel.Top = captureBox.Bottom + (int)(12 * s);
            statusGlyph.Location = new Point(captureBox.Left, statusLabel.Top + (int)(1 * s));
            footerTop = statusLabel.Bottom + (int)(20 * s);
            int footerHeight = (int)(64 * s);
            int buttonTop = footerTop + (footerHeight - saveButton.Height) / 2;
            saveButton.Top = cancelButton.Top = buttonTop;
            resetButton.Top = buttonTop + (saveButton.Height - resetButton.Height) / 2;
            foreach (FlatButton button in new[] { saveButton, cancelButton, resetButton })
                button.BackColor = Theme.DialogFooter;
            ClientSize = new Size(ClientSize.Width, footerTop + footerHeight);
            Invalidate();
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
                ShowResult(KeyCaptureState.Idle, Strings.Current.HotkeyIsCurrent, canSave: false);
            else
                ShowResult(KeyCaptureState.Valid, string.Format(Strings.Current.HotkeyPressSave, HotkeyText.Format(hotkey)), canSave: true);
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
                ShowResult(KeyCaptureState.Idle, Strings.Current.HotkeyCurrent, canSave: false);
            }
            else
            {
                captureBox.Keycaps = null;
                ShowResult(KeyCaptureState.Idle, string.Format(Strings.Current.HotkeyExample, HotkeyText.Format(defaultHotkey)), canSave: false);
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
            ShowResult(KeyCaptureState.Pending, Strings.Current.HotkeyPending, canSave: false);
        }

        private void ShowResult(KeyCaptureState state, string message, bool canSave)
        {
            captureBox.State = state;
            statusLabel.Text = message;
            statusLabel.ForeColor = state == KeyCaptureState.Invalid ? Theme.Error : state == KeyCaptureState.Valid ? Theme.TextPrimary : Theme.TextSecondary;
            statusGlyph.Text = state == KeyCaptureState.Invalid ? "\uE783" : state == KeyCaptureState.Valid ? "\uE73E" : "\uE946";
            statusGlyph.ForeColor = state == KeyCaptureState.Invalid ? Theme.Error : state == KeyCaptureState.Valid ? Theme.Success : Theme.TextSecondary;
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
            SetCandidate(defaultHotkey);
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
            using (var footer = new SolidBrush(Theme.DialogFooter))
                e.Graphics.FillRectangle(footer, 0, footerTop, ClientSize.Width, ClientSize.Height - footerTop);
            using (var line = new SolidBrush(Theme.Border))
                e.Graphics.FillRectangle(line, 0, footerTop, ClientSize.Width, 1);
            using (var pen = new Pen(Theme.WindowBorder))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        #endregion
    }
}
