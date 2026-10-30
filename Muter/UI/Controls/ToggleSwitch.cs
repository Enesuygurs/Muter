using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // On/off switch in the style of the Windows settings app.
    public class ToggleSwitch : Control
    {
        private bool isChecked;
        private bool hover;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Size = new Size(40, 20);
        }

        // Raised when the user flips the switch (not when Checked is set from code).
        public event EventHandler Toggled;

        [DefaultValue(false)]
        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                    return;
                isChecked = value;
                Invalidate();
            }
        }

        // What a click does.
        public void Toggle()
        {
            if (!Enabled)
                return;
            Checked = !Checked;
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Toggle();
            base.OnClick(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
                Toggle();
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
            float pad = 2 * s;
            var track = new RectangleF(pad, pad, Width - 2 * pad - 1, Height - 2 * pad - 1);
            float radius = track.Height / 2;

            using (GraphicsPath path = Theme.RoundedRectangle(track, radius))
            {
                if (isChecked)
                {
                    using (var fill = new SolidBrush(!Enabled ? Theme.AccentDisabled : hover ? Theme.AccentHover : Theme.Accent))
                        g.FillPath(fill, path);
                }
                else
                {
                    using (var fill = new SolidBrush(hover ? Theme.ButtonHover : Theme.Surface))
                    using (var pen = new Pen(Enabled ? Theme.TextSecondary : Theme.TextDisabled, s))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                }
            }

            float knob = track.Height - 6 * s;
            float x = isChecked ? track.Right - 3 * s - knob : track.Left + 3 * s;
            using (var brush = new SolidBrush(isChecked ? Theme.AccentText : Enabled ? Theme.TextSecondary : Theme.TextDisabled))
                g.FillEllipse(brush, x, track.Top + 3 * s, knob, knob);

            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
                using (var pen = new Pen(Theme.TextPrimary, s))
                    g.DrawPath(pen, ring);
            }
        }
    }
}
