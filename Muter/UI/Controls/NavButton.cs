using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    public enum NavIcon
    {
        // The Glyph (Segoe MDL2 Assets).
        Glyph,

        // A small monitor with the overlay in its centre.
        OverlayScreen,

        // A small monitor with the mini indicator in its corner.
        MiniScreen,
    }

    // An entry of the settings window's side navigation: icon and text; when selected a fill, an accent pill on the
    // left and semibold text.
    public class NavButton : Control
    {
        private static readonly Color HoverFill = Color.FromArgb(0x1B, 0x1E, 0x23);
        private Font selectedFont;
        private bool selected;
        private bool hover;

        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            BackColor = Theme.Background;
            Size = new Size(168, 36);
        }

        [DefaultValue("")]
        public string Glyph { get; set; } = string.Empty;

        [DefaultValue(NavIcon.Glyph)]
        public NavIcon Icon { get; set; }

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

        protected override void OnFontChanged(EventArgs e)
        {
            selectedFont?.Dispose();
            selectedFont = null;
            base.OnFontChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                selectedFont?.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; // crisp, equal 1 px edges on every side
            float s = DeviceDpi / 96f;
            if (selected || hover)
                Theme.Fill(g, ClientRectangle, 6 * s, selected ? Theme.NavSelected : HoverFill);
            if (selected)
                Theme.Fill(g, new RectangleF(0, (Height - 16 * s) / 2, 3 * s, 16 * s), 1.5f * s, Theme.Accent);
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Theme.RoundedRectangle(new RectangleF(0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s), 6 * s))
                using (var pen = new Pen(Theme.TextPrimary, s))
                    g.DrawPath(pen, ring);
            }

            Color color = selected ? Theme.TextPrimary : Theme.TextSecondary;
            if (Icon == NavIcon.Glyph)
            {
                using (var glyphFont = new Font(Theme.IconFontFamily, 12f))
                {
                    TextRenderer.DrawText(g, Glyph, glyphFont, new Rectangle((int)(10 * s), 0, (int)(24 * s), Height), color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            else
            {
                DrawScreenIcon(g, 14 * s, (Height - 16 * s) / 2, s, color, Icon == NavIcon.OverlayScreen);
            }

            if (selectedFont == null)
                selectedFont = new Font(Theme.SemiboldFontFamily, Font.SizeInPoints);
            int left = (int)(44 * s);
            TextRenderer.DrawText(g, Text, selected ? selectedFont : Font, new Rectangle(left, 0, Width - left - (int)(4 * s), Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        // 16 px monitor: 1 px outline, a stand, and the overlay (centre) or the mini indicator (top right corner).
        private static void DrawScreenIcon(Graphics g, float x, float y, float s, Color color, bool overlay)
        {
            using (var pen = new Pen(color, s))
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(x + 0.5f * s, y + 1.5f * s, 15 * s, 11 * s), 1.5f * s))
                g.DrawPath(pen, path);
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, x + 5 * s, y + 14 * s, 6 * s, s);
            if (overlay)
                Theme.Fill(g, new RectangleF(x + 6 * s, y + 7 * s, 4 * s, 4 * s), s, color);
            else
                Theme.Fill(g, new RectangleF(x + 10 * s, y + 4 * s, 3 * s, 3 * s), 0.8f * s, color);
        }
    }
}
