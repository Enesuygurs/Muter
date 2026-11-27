using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // Renders the tray menu: flat surface, 1 px border, rounded hover fill, accent check mark.
    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color MenuBack = Theme.Surface;
        private static readonly Color Hover = Theme.ButtonHover;
        private static readonly Color Pressed = Theme.ButtonPressed;
        private static readonly Color MenuBorder = Theme.Border;
        private static readonly Color Divider = Theme.Border;

        public TrayMenuRenderer() : base(new Colors())
        {
            RoundedEdges = false;
        }

        // Fonts, margins and paddings of the menu and its submenus.
        public static void Apply(ContextMenuStrip menu, Font font, bool checkMargin = true)
        {
            int dpi = menu.DeviceDpi;
            menu.Renderer = new TrayMenuRenderer();
            menu.BackColor = MenuBack;
            menu.ForeColor = Theme.TextPrimary;
            menu.Font = font;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = checkMargin;
            menu.Padding = new Padding(2, 4, 2, 4);
            foreach (ToolStripItem item in menu.Items)
                ApplyItem(item, font, dpi);
        }

        private static void ApplyItem(ToolStripItem item, Font font, int dpi)
        {
            item.ForeColor = Theme.TextPrimary;
            item.Padding = new Padding(0, (int)(3 * dpi / 96f), 0, (int)(3 * dpi / 96f));
            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                if (menuItem.DropDown is ToolStripDropDownMenu dropDown)
                {
                    dropDown.Renderer = new TrayMenuRenderer();
                    dropDown.BackColor = MenuBack;
                    dropDown.ShowImageMargin = false;
                    dropDown.ShowCheckMargin = true;
                    dropDown.Font = font;
                }
                foreach (ToolStripItem child in menuItem.DropDownItems)
                    ApplyItem(child, font, dpi);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            if (e.ToolStrip is ToolStripDropDownMenu menu && !menu.ShowCheckMargin && !menu.ShowImageMargin)
            {
                int indent = (int)(10 * e.ToolStrip.DeviceDpi / 96f);
                var r = e.TextRectangle;
                e.TextRectangle = new Rectangle(r.X + indent, r.Y, r.Width, r.Height);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The checked entry is marked by the accent bar on its left edge instead of a check mark.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.IsOnDropDown && e.Item is ToolStripMenuItem checkedItem && checkedItem.Checked)
            {
                float s = e.ToolStrip.DeviceDpi / 96f;
                float height = 14 * s;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath bar = Theme.RoundedRectangle(new RectangleF(0, (e.Item.Height - height) / 2, 3 * s, height), 1.5f * s))
                using (var brush = new SolidBrush(Theme.Accent))
                    e.Graphics.FillPath(brush, bar);
            }
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var g = e.Graphics;
            var oldSmoothing = g.SmoothingMode;
            var oldOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 4))
            using (var brush = new SolidBrush(Hover))
                g.FillPath(brush, path);
            g.SmoothingMode = oldSmoothing;
            g.PixelOffsetMode = oldOffset;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(MenuBorder))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Divider))
                e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => MenuBack;
            public override Color ImageMarginGradientBegin => MenuBack;
            public override Color ImageMarginGradientMiddle => MenuBack;
            public override Color ImageMarginGradientEnd => MenuBack;
            public override Color MenuBorder => TrayMenuRenderer.MenuBorder;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color SeparatorDark => Divider;
            public override Color SeparatorLight => Divider;
            public override Color CheckBackground => MenuBack;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Pressed;
        }
    }
}
