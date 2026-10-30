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

    // A themed dropdown: shows the selected option and opens a dark menu with all options.
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
            Size = new Size(200, 30);
            menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
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
            // The list is exactly as wide as the button and every item spans it (a minimum size alone would leave an
            // empty strip on the right that the highlight does not reach). Rows are a bit taller than the default.
            float scale = DeviceDpi / 96f;
            int vertical = (int)(4 * scale);
            menu.AutoSize = true;
            foreach (ToolStripItem item in menu.Items)
                item.Padding = new Padding(0, vertical, 0, vertical);
            menu.PerformLayout();
            Size natural = menu.GetPreferredSize(Size.Empty);
            int extra = Width - natural.Width;
            if (extra > 0)
            {
                // WinForms does not grow the list for item padding, so the list width is set explicitly.
                foreach (ToolStripItem item in menu.Items)
                    item.Padding = new Padding(0, vertical, extra, vertical);
                menu.AutoSize = false;
                menu.Size = new Size(Width, natural.Height);
            }
            menu.Show(this, new Point(0, Height + (int)(2 * scale)));
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
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            bool open = menu.Visible;
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 4 * s))
            using (var fill = new SolidBrush(hover || open ? Theme.ButtonHover : Theme.ButtonFill))
            using (var pen = new Pen(Focused && ShowFocusCues ? Theme.TextPrimary : open ? Theme.Accent : Theme.ButtonBorder, s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            int chevronWidth = (int)(28 * s);
            var textBounds = new Rectangle((int)(10 * s), 0, Width - chevronWidth - (int)(10 * s), Height);
            TextRenderer.DrawText(g, SelectedText, Font, textBounds, Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            using (var glyphFont = new Font(Theme.IconFontFamily, 7f * Font.SizeInPoints / 9f))
            {
                TextRenderer.DrawText(g, "", glyphFont, new Rectangle(Width - chevronWidth, 0, chevronWidth, Height), Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }
}
