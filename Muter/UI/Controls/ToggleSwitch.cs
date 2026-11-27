using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // iOS-style switch: a pill track (accent green when on, grey when off) with a white round knob and a soft shadow that
    // slides (animated). The control is 2 px larger on every side for the focus ring.
    public class ToggleSwitch : Control
    {
        private const int AnimationMs = 150;
        private static readonly Color OffTrack = Color.FromArgb(0x33, 0x37, 0x5A);
        private static readonly Color OffTrackHover = Color.FromArgb(0x3F, 0x44, 0x6E);

        private readonly Timer animation = new Timer { Interval = 15 };
        private bool isChecked;
        private bool hover;
        private float position; // 0 = off, 1 = on (animated)
        private DateTime animationStart;
        private float animationFrom;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Theme.Surface;
            Size = new Size(42, 26);
            animation.Tick += (s, e) => Animate();
        }

        // Raised when the user flips the switch (not when Checked is set from code).
        public event EventHandler Toggled;

        // Setting it from code moves the knob without animation.
        [DefaultValue(false)]
        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                    return;
                isChecked = value;
                animation.Stop();
                position = value ? 1f : 0f;
                Invalidate();
            }
        }

        // What a click does: flip with the sliding animation.
        public void Toggle()
        {
            if (!Enabled)
                return;
            isChecked = !isChecked;
            animationFrom = position;
            animationStart = DateTime.UtcNow;
            animation.Start();
            Invalidate();
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                animation.Dispose();
            base.Dispose(disposing);
        }

        private void Animate()
        {
            float t = Math.Min(1f, (float)(DateTime.UtcNow - animationStart).TotalMilliseconds / AnimationMs);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            float target = isChecked ? 1f : 0f;
            position = animationFrom + (target - animationFrom) * eased;
            if (t >= 1f)
            {
                position = target;
                animation.Stop();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = DeviceDpi / 96f;
            int alpha = Enabled ? 255 : 110;

            var track = new RectangleF(2 * s, 2 * s, Width - 4 * s, Height - 4 * s);
            float radius = track.Height / 2;
            Color off = hover && Enabled ? OffTrackHover : OffTrack;
            Color on = hover && Enabled ? Theme.AccentHover : Theme.Accent;
            using (GraphicsPath path = Theme.RoundedRectangle(track, radius))
            using (var fill = new SolidBrush(Color.FromArgb(alpha, Blend(off, on, position))))
                g.FillPath(fill, path);

            // Knob: white circle (2 px inside the track) with a soft shadow, sliding along the track.
            float margin = 2 * s;
            float knob = track.Height - 2 * margin;
            float travel = track.Width - 2 * margin - knob;
            float x = track.Left + margin + travel * position;
            float y = track.Top + margin;
            for (int i = 3; i >= 1; i--)
            {
                using (var shadow = new SolidBrush(Color.FromArgb(alpha * 28 / 255, 0, 0, 0)))
                    g.FillEllipse(shadow, x - i * 0.5f * s, y - i * 0.25f * s + 1.5f * s, knob + i * s, knob + i * s);
            }
            using (var knobBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillEllipse(knobBrush, x, y, knob, knob);

            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Theme.RoundedRectangle(new RectangleF(0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s), Height / 2f))
                using (var pen = new Pen(Theme.TextPrimary, s))
                    g.DrawPath(pen, ring);
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }
    }
}
