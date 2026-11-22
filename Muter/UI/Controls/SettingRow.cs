using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // A row of a settings card: the label and an optional hint (wrapped) on the left, the control on the right. The
    // card places rows 8 px in from its edges, so the row's own 8 px padding puts everything 16 px from the card edge.
    public class SettingRow : Panel
    {
        internal const int Inset = 8;
        private const int MinHeight = 56;
        private const int ControlGap = 16;
        private const TextFormatFlags TitleFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        private const TextFormatFlags HintFlags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private string title = string.Empty;
        private string description = string.Empty;
        private Color descriptionColor = Color.Empty;
        private Control setting;
        private Font hintFont;

        public SettingRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Size = new Size(476, MinHeight);
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
                value = value ?? string.Empty;
                if (description == value)
                    return;
                description = value;
                Invalidate();
                Parent?.PerformLayout(); // the height may change
            }
        }

        // The hint colour; Color.Empty is the normal secondary text colour.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DescriptionColor
        {
            get => descriptionColor;
            set
            {
                descriptionColor = value;
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

        internal Font HintFont => hintFont ?? (hintFont = new Font(Font.FontFamily, 9f));

        private float DpiScale => DeviceDpi / 96f;

        // The width the label and the hint may use.
        internal int TextWidth => (setting != null ? setting.Left - (int)(ControlGap * DpiScale) : Width - (int)(Inset * DpiScale)) - (int)(Inset * DpiScale);

        // Height for the current width: at least 56 px, more when the hint wraps.
        internal int PreferredRowHeight()
        {
            float s = DpiScale;
            PlaceSetting();
            int textHeight = TextHeight(out _, out _);
            int controlHeight = setting?.Height ?? 0;
            return Math.Max((int)(MinHeight * s), Math.Max(textHeight + (int)(22 * s), controlHeight + (int)(24 * s)));
        }

        private int TextHeight(out int titleHeight, out int hintHeight)
        {
            int width = Math.Max(1, TextWidth);
            titleHeight = TextRenderer.MeasureText(title.Length == 0 ? " " : title, Font, new Size(width, int.MaxValue), TitleFlags).Height;
            hintHeight = description.Length == 0 ? 0 : TextRenderer.MeasureText(description, HintFont, new Size(width, int.MaxValue), HintFlags).Height;
            return titleHeight + (hintHeight > 0 ? (int)(2 * DpiScale) + hintHeight : 0);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            hintFont?.Dispose();
            hintFont = null;
            base.OnFontChanged(e);
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
            var location = new Point(Width - (int)(Inset * DpiScale) - setting.Width, (Height - setting.Height) / 2);
            if (setting.Location != location)
                setting.Location = location;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int left = (int)(Inset * DpiScale);
            int width = Math.Max(1, TextWidth);
            int total = TextHeight(out int titleHeight, out int hintHeight);
            int top = (Height - total) / 2;
            TextRenderer.DrawText(g, title, Font, new Rectangle(left, top, width, titleHeight), Theme.TextPrimary, Theme.Surface, TitleFlags);
            if (hintHeight > 0)
            {
                TextRenderer.DrawText(g, description, HintFont, new Rectangle(left, top + titleHeight + (int)(2 * DpiScale), width, hintHeight),
                    descriptionColor.IsEmpty ? Theme.TextSecondary : descriptionColor, Theme.Surface, HintFlags);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                hintFont?.Dispose();
            base.Dispose(disposing);
        }
    }
}
