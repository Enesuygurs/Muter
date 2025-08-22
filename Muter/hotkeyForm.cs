using System;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Muter
{
    public partial class hotkeyForm : Form
    {
        #region P/Invoke for Window Dragging

        // Imports and constants for making the form draggable.
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        #endregion

        #region Constants & Fields

        // Constants for modifier keys.
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;

        // Variables to hold the new hotkey combination.
        private Keys currentKey = Keys.None;
        private uint currentModifiers = 0;
        private bool isListeningForKey = false;

        #endregion

        #region Initialization

        // Form constructor.
        public hotkeyForm()
        {
            InitializeComponent();
        }

        // Handles the form's load event.
        private void hotkeyForm_Load(object sender, EventArgs e)
        {
            key.Text = Properties.Settings.Default.shortcut;
            CenterToScreen();

            // Make the title and top panel draggable.
            this.title.MouseDown += new MouseEventHandler(DraggableControl_MouseDown);
            this.topPanel.MouseDown += new MouseEventHandler(DraggableControl_MouseDown);
        }

        #endregion

        #region Hotkey Logic

        // Handles the key down event to capture the new hotkey.
        private void key_KeyDown(object sender, KeyEventArgs e)
        {
            if (!isListeningForKey) return;

            // Capture the key only if it's not a standalone modifier key.
            if (e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu)
            {
                currentKey = e.KeyCode;
                currentModifiers = 0;
                if (e.Shift) currentModifiers |= MOD_SHIFT;
                if (e.Control) currentModifiers |= MOD_CONTROL;
                if (e.Alt) currentModifiers |= MOD_ALT;

                UpdateHotkeyDisplay();
                isListeningForKey = false;
            }
        }

        // Updates the text in the textbox to show the selected hotkey.
        private void UpdateHotkeyDisplay()
        {
            if (currentKey == Keys.None) return;

            var displayText = new StringBuilder();
            if ((currentModifiers & MOD_CONTROL) != 0) displayText.Append("Control + ");
            if ((currentModifiers & MOD_ALT) != 0) displayText.Append("Alt + ");
            if ((currentModifiers & MOD_SHIFT) != 0) displayText.Append("Shift + ");
            displayText.Append(currentKey);

            key.Text = displayText.ToString();
        }

        #endregion

        #region UI Event Handlers

        // Starts listening for a new hotkey when the textbox is clicked.
        private void key_Click(object sender, EventArgs e)
        {
            isListeningForKey = true;
            key.Text = "Press a key...";
        }

        // Saves the new hotkey and updates the main form.
        private void setButton_Click(object sender, EventArgs e)
        {
            if (currentKey == Keys.None)
            {
                MessageBox.Show("Please select a valid key combination.");
                return;
            }

            if (this.Owner is Form1 mainForm)
            {
                mainForm.UpdateHotkey(currentModifiers, currentKey);
                Properties.Settings.Default.shortcut = key.Text;
                Properties.Settings.Default.Save();
                MessageBox.Show("Shortcut key changed successfully.");
                this.Close();
            }
            else
            {
                MessageBox.Show("Error: Main form could not be found.");
            }
        }

        // Closes the form.
        private void closeButton_Click(object sender, EventArgs e) => this.Close();

        // Allows the form to be dragged by clicking on specific controls.
        private void DraggableControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        #endregion
    }
}