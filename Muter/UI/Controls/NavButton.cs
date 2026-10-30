using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // An entry of the settings window's side navigation: icon, text and an accent bar when selected.
    public class NavButton : Control
    {
        private bool selected;
        private bool hover;

        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            BackColor = Theme.Background;
            Cursor = Cursors.Hand;
            Size = new Size(160, 36);
        }

        [DefaultValue("")]
        public string Glyph { get; set; } = string.Empty;

        [DefaultValue(false)]
        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            base.OnClick(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Return)
                OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            if (selected || hover)
            {
                using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 5 * s))
                using (var fill = new SolidBrush(selected ? Theme.Surface : Color.FromArgb(38, 38, 38)))
                    g.FillPath(fill, path);
            }
            if (selected)
            {
                using (GraphicsPath bar = Theme.RoundedRectangle(new RectangleF(0, Height * 0.28f, 3 * s, Height * 0.44f), 1.5f * s))
                using (var fill = new SolidBrush(Theme.Accent))
                    g.FillPath(fill, bar);
            }
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Theme.RoundedRectangle(new RectangleF(1, 1, Width - 2, Height - 2), 5 * s))
                using (var pen = new Pen(Theme.TextPrimary, s))
                    g.DrawPath(pen, ring);
            }

            int iconWidth = (int)(40 * s);
            using (var glyphFont = new Font(Theme.IconFontFamily, 11f * Font.SizeInPoints / 9.75f))
            {
                TextRenderer.DrawText(g, Glyph, glyphFont, new Rectangle((int)(4 * s), 0, iconWidth, Height), selected ? Theme.Accent : Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(iconWidth + (int)(4 * s), 0, Width - iconWidth, Height), Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
