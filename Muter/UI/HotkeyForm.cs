using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Hotkeys;

namespace Muter.UI
{
    public partial class HotkeyForm : Form
    {
        #region P/Invoke for Window Dragging

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        #endregion

        #region Fields

        // Tries to register and save the hotkey; returns null on success or an error message.
        private readonly Func<Hotkey, string> applyHotkey;
        private Hotkey selectedHotkey;
        private bool isListeningForKey;

        #endregion

        #region Initialization

        // Required by the Windows Forms designer.
        public HotkeyForm() : this(Hotkey.Default, _ => null)
        {
        }

        public HotkeyForm(Hotkey currentHotkey, Func<Hotkey, string> applyHotkey)
        {
            InitializeComponent();
            Icon = AppResources.AppIcon;
            this.applyHotkey = applyHotkey ?? throw new ArgumentNullException(nameof(applyHotkey));
            selectedHotkey = currentHotkey;
            key.Text = currentHotkey.IsValid ? currentHotkey.ToString() : string.Empty;
        }

        private void HotkeyForm_Load(object sender, EventArgs e) => CenterToScreen();

        #endregion

        #region Hotkey Logic

        // Captures every key (including Tab, Enter, Esc and Alt combinations) while waiting for a new hotkey.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!isListeningForKey)
                return base.ProcessCmdKey(ref msg, keyData);

            Keys keyCode = keyData & Keys.KeyCode;
            if (!Hotkey.IsValidKey(keyCode))
                return true; // a modifier on its own: keep waiting for the actual key

            var modifiers = HotkeyModifiers.None;
            if ((keyData & Keys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
            if ((keyData & Keys.Control) != 0) modifiers |= HotkeyModifiers.Control;
            if ((keyData & Keys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;

            selectedHotkey = new Hotkey(keyCode, modifiers);
            key.Text = selectedHotkey.ToString();
            isListeningForKey = false;
            return true;
        }

        #endregion

        #region UI Event Handlers

        // Starts listening for a new hotkey when the textbox is clicked.
        private void key_Click(object sender, EventArgs e)
        {
            isListeningForKey = true;
            key.Text = "Press a key...";
        }

        // Registers the new hotkey; the dialog stays open if it is taken by another application.
        private void setButton_Click(object sender, EventArgs e)
        {
            if (isListeningForKey || !selectedHotkey.IsValid)
            {
                MessageBox.Show(this, "Please click the box and press a key combination first.", "Muter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string error = applyHotkey(selectedHotkey);
            if (error != null)
            {
                MessageBox.Show(this, error, "Muter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(this, $"Shortcut key changed to {selectedHotkey}.", "Muter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void closeButton_Click(object sender, EventArgs e) => Close();

        // Allows the borderless form to be dragged by its title bar.
        private void DraggableControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
        }

        #endregion
    }
}
