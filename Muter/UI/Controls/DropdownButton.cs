using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    public sealed class DropdownOption
    {
        public DropdownOption(string text, object value, bool enabled = true)
        {
            Text = text;
            Value = value;
            Enabled = enabled;
        }

        public string Text { get; }

        public object Value { get; }

        public bool Enabled { get; }
    }

    // A themed dropdown: shows the selected option with a chevron and opens a list of all options under it.
    public class DropdownButton : Control
    {
        private readonly ContextMenuStrip menu;
        private List<DropdownOption> options = new List<DropdownOption>();
        private object selectedValue;
        private bool hover;

        public DropdownButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            BackColor = Theme.Surface;
            ForeColor = Theme.TextPrimary;
            Size = new Size(208, 32);
            menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, DropShadowEnabled = true };
            menu.Closed += (s, e) => Invalidate();
        }

        // Raised when the user picks an option (not when SelectedValue is set from code).
        public event EventHandler SelectionChanged;

        // Called right before the menu opens, e.g. to refresh a device list.
        public event EventHandler DropDownOpening;

        public IReadOnlyList<DropdownOption> Options => options;

        public object SelectedValue
        {
            get => selectedValue;
            set
            {
                selectedValue = value;
                Invalidate();
            }
        }

        public string SelectedText => options.FirstOrDefault(o => Equals(o.Value, selectedValue))?.Text ?? string.Empty;

        public void SetOptions(IEnumerable<DropdownOption> newOptions, object selected)
        {
            options = newOptions.ToList();
            selectedValue = selected;
            Invalidate();
        }

        // What picking an option in the menu does.
        public void Choose(object value)
        {
            DropdownOption option = options.FirstOrDefault(o => Equals(o.Value, value));
            if (option == null || !option.Enabled || !Enabled)
                return;
            SelectedValue = option.Value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ShowDropDown()
        {
            DropDownOpening?.Invoke(this, EventArgs.Empty);
            foreach (ToolStripItem old in menu.Items.Cast<ToolStripItem>().ToList())
                old.Dispose();
            menu.Items.Clear();
            menu.Font = Font;
            foreach (DropdownOption option in options)
            {
                DropdownOption chosen = option;
                var item = new ToolStripMenuItem(option.Text) { Checked = Equals(option.Value, selectedValue), Enabled = option.Enabled };
                item.Click += (s, e) => Choose(chosen.Value);
                menu.Items.Add(item);
            }
            // Rows are 32 px; the list is at least 300 px (and the button) wide, right-aligned under the button. Every row
            // spans the whole list so the highlight reaches the right edge.
            float scale = DeviceDpi / 96f;
            menu.AutoSize = true;
            DarkMenuRenderer.SizeItems(menu, scale, 0);
            menu.PerformLayout();
            Size natural = menu.GetPreferredSize(Size.Empty);
            int width = Math.Max(natural.Width, Math.Max(Width, (int)(300 * scale)));
            if (width > natural.Width)
            {
                DarkMenuRenderer.SizeItems(menu, scale, width - natural.Width);
                menu.AutoSize = false;
                menu.Size = new Size(width, natural.Height);
            }
            menu.Show(this, new Point(Width - width, Height + (int)(4 * scale)));
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            ShowDropDown();
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Down || (e.Alt && e.KeyCode == Keys.Down))
            {
                ShowDropDown();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                menu.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; // crisp, equal 1 px edges on every side
            float s = DeviceDpi / 96f;
            bool open = menu.Visible;
            Color border = !Enabled ? Theme.DisabledFill : open ? Theme.Accent : Focused && ShowFocusCues ? Theme.TextPrimary : Theme.ButtonBorder;
            Theme.FillBox(g, ClientRectangle, 6 * s, !Enabled ? Theme.DisabledFill : hover || open ? Theme.ButtonHover : Theme.ButtonFill, border, s);

            int chevronWidth = (int)(32 * s);
            var textBounds = new Rectangle((int)(12 * s), 0, Width - chevronWidth - (int)(12 * s), Height);
            TextRenderer.DrawText(g, SelectedText, Font, textBounds, Enabled ? Theme.TextPrimary : Theme.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            using (var glyphFont = new Font(Theme.IconFontFamily, 8f))
            {
                TextRenderer.DrawText(g, open ? "" : "", glyphFont, new Rectangle(Width - chevronWidth, 0, chevronWidth - (int)(8 * s), Height), Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }
}
