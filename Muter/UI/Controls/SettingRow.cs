using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // A settings "card": title and optional description on the left, the control on the right.
    public class SettingRow : Panel
    {
        private string title = string.Empty;
        private string description = string.Empty;
        private Control setting;

        public SettingRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Size = new Size(440, 56);
        }

        [DefaultValue("")]
        public string Title
        {
            get => title;
            set
            {
                title = value ?? string.Empty;
                Invalidate();
            }
        }

        [DefaultValue("")]
        public string Description
        {
            get => description;
            set
            {
                description = value ?? string.Empty;
                Invalidate();
            }
        }

        // The control shown on the right, vertically centered.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Control Setting
        {
            get => setting;
            set
            {
                if (setting != null)
                    Controls.Remove(setting);
                setting = value;
                if (setting != null)
                {
                    setting.BackColor = Theme.Surface;
                    Controls.Add(setting);
                    PlaceSetting();
                }
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PlaceSetting();
        }

        private void PlaceSetting()
        {
            if (setting == null)
                return;
            int margin = (int)(16 * DeviceDpi / 96f);
            setting.Location = new Point(Width - margin - setting.Width, (Height - setting.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 6 * s))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Color.FromArgb(52, 52, 52), s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            int left = (int)(16 * s);
            int right = setting != null ? setting.Left - (int)(12 * s) : Width - left;
            using (var titleFont = new Font(Font.FontFamily, Font.SizeInPoints, FontStyle.Regular))
            using (var descriptionFont = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f))
            {
                const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                if (description.Length == 0)
                {
                    TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, 0, right - left, Height), Theme.TextPrimary, flags | TextFormatFlags.VerticalCenter);
                    return;
                }
                int titleHeight = TextRenderer.MeasureText(g, title, titleFont, Size.Empty, flags).Height;
                int descriptionHeight = TextRenderer.MeasureText(g, description, descriptionFont, Size.Empty, flags).Height;
                int top = (Height - titleHeight - descriptionHeight - (int)(2 * s)) / 2;
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, top, right - left, titleHeight), Theme.TextPrimary, flags);
                TextRenderer.DrawText(g, description, descriptionFont, new Rectangle(left, top + titleHeight + (int)(2 * s), right - left, descriptionHeight), Theme.TextSecondary, flags);
            }
        }
    }
}
