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
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color text;

            switch (buttonStyle)
            {
                case FlatButtonStyle.Primary:
                    Color fill = !Enabled ? Theme.AccentDisabled : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                    using (GraphicsPath path = Theme.RoundedRectangle(bounds, 5 * scale))
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                    text = Enabled ? Theme.AccentText : Theme.TextDisabled;
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
                    text = !Enabled ? Theme.TextDisabled : hover ? Theme.AccentHover : Theme.Accent;
                    break;

                default:
                    Color background = !Enabled ? Theme.Background : pressed ? Theme.ButtonPressed : hover ? Theme.ButtonHover : Theme.ButtonFill;
                    using (GraphicsPath path = Theme.RoundedRectangle(bounds, 5 * scale))
                    using (var brush = new SolidBrush(background))
                    using (var pen = new Pen(Theme.ButtonBorder))
                    {
                        g.FillPath(brush, path);
                        g.DrawPath(pen, path);
                    }
                    text = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                    break;
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (buttonStyle == FlatButtonStyle.Link)
                flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Font font = Font;
            Font underlined = null;
            if (buttonStyle == FlatButtonStyle.Link && hover && Enabled)
                font = underlined = new Font(Font, FontStyle.Underline);
            TextRenderer.DrawText(g, Text, font, ClientRectangle, text, flags);
            underlined?.Dispose();

            // Keyboard focus ring (not drawn for mouse clicks).
            if (Focused && ShowFocusCues && buttonStyle != FlatButtonStyle.Close)
            {
                using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f), 4 * scale))
                using (var pen = new Pen(Theme.TextPrimary, 1.5f * scale))
                    g.DrawPath(pen, path);
            }
        }
    }
}
