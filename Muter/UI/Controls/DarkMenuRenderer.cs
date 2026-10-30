using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // Dark theme for menus (the tray menu and the settings dropdowns).
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors())
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
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle image = e.ImageRectangle;
            var box = new Rectangle(image.X, (e.Item.Height - image.Height) / 2, image.Width, image.Height); // centred like the text
            float s = box.Height / 16f;
            using (var pen = new Pen(Theme.Accent, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(box.Left + 3 * s, box.Top + 8.5f * s),
                    new PointF(box.Left + 6.5f * s, box.Top + 12 * s),
                    new PointF(box.Left + 13 * s, box.Top + 4.5f * s),
                });
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
