using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    public enum FlatButtonStyle
    {
        Primary,
        Secondary,
        Link,
        Close,
    }

    // Rounded, flat, themed button. Works as a dialog's AcceptButton/CancelButton.
    public class FlatButton : Control, IButtonControl
    {
        private FlatButtonStyle buttonStyle = FlatButtonStyle.Secondary;
        private bool hover;
        private bool pressed;
        private bool isDefault;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
        }

        [DefaultValue(FlatButtonStyle.Secondary)]
        public FlatButtonStyle ButtonStyle
        {
            get => buttonStyle;
            set
            {
                buttonStyle = value;
                Invalidate();
            }
        }

        [DefaultValue(DialogResult.None)]
        public DialogResult DialogResult { get; set; }

        public void NotifyDefault(bool value)
        {
            isDefault = value;
            Invalidate();
        }

        public void PerformClick()
        {
            if (Enabled && Visible)
                OnClick(EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled)
                return;
            Form form = FindForm();
            if (form != null && DialogResult != DialogResult.None)
                form.DialogResult = DialogResult;
            base.OnClick(e);
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
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
                PerformClick();
            base.OnKeyUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
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

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; // crisp, equal 1 px edges on every side
            float scale = DeviceDpi / 96f;
            Color text;

            float radius = 6 * scale;
            switch (buttonStyle)
            {
                case FlatButtonStyle.Primary:
                    if (!Enabled)
                    {
                        Theme.Fill(g, ClientRectangle, radius, Theme.DisabledFill);
                        text = Theme.DisabledText;
                        break;
                    }
                    Theme.Fill(g, ClientRectangle, radius, pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent);
                    text = Theme.AccentText;
                    break;

                case FlatButtonStyle.Close:
                    if (hover || pressed)
                    {
                        using (var brush = new SolidBrush(pressed ? Color.FromArgb(150, 35, 25) : Theme.CloseHover))
                            g.FillRectangle(brush, ClientRectangle);
                    }
                    text = hover || pressed ? Color.White : Theme.TextSecondary;
                    break;

                case FlatButtonStyle.Link:
                    text = !Enabled ? Theme.TextDisabled : hover ? Theme.AccentHover : Theme.Link;
                    break;

                default:
                    if (!Enabled)
                    {
                        Theme.Fill(g, ClientRectangle, radius, Theme.DisabledFill);
                        text = Theme.DisabledText;
                        break;
                    }
                    Theme.FillBox(g, ClientRectangle, radius, pressed ? Theme.ButtonPressed : hover ? Theme.ButtonHover : Theme.ButtonFill, Theme.ButtonBorder, scale);
                    text = Theme.TextPrimary;
                    break;
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (buttonStyle == FlatButtonStyle.Link)
                flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Font font = Font;
            Font extra = null;
            if (buttonStyle == FlatButtonStyle.Link && hover && Enabled)
                font = extra = new Font(Font, FontStyle.Underline);
            else if (buttonStyle == FlatButtonStyle.Primary && Enabled && Font.Name == Theme.FontFamily)
                font = extra = new Font(Theme.SemiboldFontFamily, Font.SizeInPoints);
            TextRenderer.DrawText(g, Text, font, ClientRectangle, text, flags);
            extra?.Dispose();

            // Keyboard focus ring (not drawn for mouse clicks).
            if (Focused && ShowFocusCues && buttonStyle != FlatButtonStyle.Close)
            {
                using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(1.5f * scale, 1.5f * scale, Width - 3f * scale, Height - 3f * scale), 5 * scale))
                using (var pen = new Pen(Theme.TextPrimary, scale))
                    g.DrawPath(pen, path);
            }
        }
    }
}
