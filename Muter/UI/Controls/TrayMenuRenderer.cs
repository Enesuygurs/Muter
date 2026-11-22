using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // Dark theme for menus (the tray menu and the settings dropdowns).
    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            if (e.Item.IsOnDropDown)
            {
                // Vertically centred in the whole row, whatever padding the row has.
                Rectangle text = e.TextRectangle;
                e.TextRectangle = new Rectangle(text.X, 0, text.Width, e.Item.Height);
                e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom)) | TextFormatFlags.VerticalCenter;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The checked entry is marked by the accent bar on its left edge instead of a check mark.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderMenuItemBackground(e);
            if (e.Item.IsOnDropDown && e.Item is ToolStripMenuItem item && item.Checked)
            {
                float s = e.ToolStrip.DeviceDpi / 96f;
                float height = 14 * s;
                var graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath bar = Theme.RoundedRectangle(new RectangleF(0, (e.Item.Height - height) / 2, 3 * s, height), 1.5f * s))
                using (var brush = new SolidBrush(Theme.Accent))
                    graphics.FillPath(brush, bar);
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            private static readonly Color Menu = Color.FromArgb(40, 40, 40);
            private static readonly Color Hover = Color.FromArgb(58, 58, 58);

            public override Color ToolStripDropDownBackground => Menu;
            public override Color ImageMarginGradientBegin => Menu;
            public override Color ImageMarginGradientMiddle => Menu;
            public override Color ImageMarginGradientEnd => Menu;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color CheckBackground => Menu;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Menu;
        }
    }
}
