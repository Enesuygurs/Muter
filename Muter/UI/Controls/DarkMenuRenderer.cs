using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // Flat dark menus (the tray menu and the settings dropdowns): popup fill with a 1 px border, 32 px rows with a
    // rounded hover fill inset 4 px, and the checked row marked with a fill, an accent pill and a check mark.
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private const int RowHeight = 32;

        public DarkMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        // Makes every row of a dropdown 32 px tall and (with extraWidth) wider than its text needs.
        public static void SizeItems(ToolStripDropDown dropDown, float scale, int extraWidth)
        {
            foreach (ToolStripItem item in dropDown.Items)
            {
                if (item is ToolStripSeparator)
                    continue;
                int textHeight = TextRenderer.MeasureText("Ag", item.Font).Height;
                int vertical = System.Math.Max(0, ((int)(RowHeight * scale) - textHeight - 4) / 2);
                item.Padding = new Padding(0, vertical, extraWidth, vertical);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var brush = new SolidBrush(Theme.Popup))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                return;
            }
            base.OnRenderToolStripBackground(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.PopupBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                return;
            }
            base.OnRenderToolStripBorder(e);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // No separate margin colour.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.IsOnDropDown)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = e.ToolStrip.DeviceDpi / 96f;
            var row = new RectangleF(4 * s, 0, e.Item.Width - 8 * s, e.Item.Height);
            bool isChecked = e.Item is ToolStripMenuItem menuItem && menuItem.Checked;
            bool hover = e.Item.Selected && e.Item.Enabled;
            if (hover)
                Theme.Fill(g, row, 5 * s, Theme.MenuHover);
            else if (isChecked)
                Theme.Fill(g, row, 5 * s, Theme.NavSelected);
            if (isChecked)
                Theme.Fill(g, new RectangleF(row.X, row.Y + (row.Height - 14 * s) / 2, 3 * s, 14 * s), 1.5f * s, Theme.Accent);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            if (e.Item.IsOnDropDown)
            {
                // Vertically centred in the whole row, whatever padding the row has.
                Rectangle text = e.TextRectangle;
                // Never wider than the row (minus a little room on the right), so a long text ends with an ellipsis.
                int room = (int)(12 * e.ToolStrip.DeviceDpi / 96f);
                e.TextRectangle = new Rectangle(text.X, 0, Math.Min(text.Width, Math.Max(1, e.Item.Width - text.X - room)), e.Item.Height);
                e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom)) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null)
                return;
            // Centred in the row like the text.
            Rectangle image = e.ImageRectangle;
            var target = new Rectangle(image.X + (int)(4 * e.ToolStrip.DeviceDpi / 96f), (e.Item.Height - image.Height) / 2, image.Width, image.Height);
            e.Graphics.DrawImage(e.Image, target);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
            using (var font = new Font(Theme.IconFontFamily, 7f))
            {
                var box = new Rectangle(e.ArrowRectangle.X - (int)(6 * s), 0, e.ArrowRectangle.Width, e.Item.Height);
                TextRenderer.DrawText(g, "", font, box, e.Item.Enabled ? Theme.TextSecondary : Theme.TextDisabled,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float s = e.ToolStrip.DeviceDpi / 96f;
            int inset = (int)(12 * s);
            int y = e.Item.Height / 2;
            using (var brush = new SolidBrush(Theme.Border))
                e.Graphics.FillRectangle(brush, inset, y, e.Item.Width - 2 * inset, System.Math.Max(1, (int)s));
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The checked row is marked by its fill and accent pill only, without a check mark.
        }

        // An MDL2 glyph as a menu image (drawn with GDI+ anti-aliasing, which keeps the transparent background clean).
        public static Bitmap GlyphImage(string glyph, int size, Color color)
        {
            var bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var font = new Font(Theme.IconFontFamily, size * 0.75f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            }
            return bitmap;
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Popup;
            public override Color ImageMarginGradientBegin => Theme.Popup;
            public override Color ImageMarginGradientMiddle => Theme.Popup;
            public override Color ImageMarginGradientEnd => Theme.Popup;
            public override Color MenuBorder => Theme.PopupBorder;
            public override Color MenuItemBorder => Theme.MenuHover;
            public override Color MenuItemSelected => Theme.MenuHover;
            public override Color MenuItemSelectedGradientBegin => Theme.MenuHover;
            public override Color MenuItemSelectedGradientEnd => Theme.MenuHover;
            public override Color MenuItemPressedGradientBegin => Theme.MenuHover;
            public override Color MenuItemPressedGradientEnd => Theme.MenuHover;
            public override Color CheckBackground => Theme.Popup;
            public override Color CheckSelectedBackground => Theme.MenuHover;
            public override Color CheckPressedBackground => Theme.MenuHover;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Popup;
        }
    }
}
