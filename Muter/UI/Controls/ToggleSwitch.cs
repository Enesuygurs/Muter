using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // iOS-style switch: a pill-shaped track with a white knob that slides (animated) between off and on.
    public class ToggleSwitch : Control
    {
        private const int AnimationMs = 150;
        private static readonly Color OffTrack = Color.FromArgb(72, 72, 76);
        private static readonly Color OffTrackHover = Color.FromArgb(84, 84, 88);

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
            Cursor = Cursors.Hand;
            Size = new Size(50, 30);
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
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
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

            // Room around the track for the focus ring.
            float inset = 2 * s;
            var track = new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset);
            float radius = track.Height / 2;
            int alpha = Enabled ? 255 : 110;

            Color off = hover && Enabled ? OffTrackHover : OffTrack;
            Color on = hover && Enabled ? Theme.AccentHover : Theme.Accent;
            Color trackColor = Blend(off, on, position);
            using (GraphicsPath path = Theme.RoundedRectangle(track, radius))
            using (var fill = new SolidBrush(Color.FromArgb(alpha, trackColor)))
                g.FillPath(fill, path);

            // Knob: a white circle with a soft shadow, sliding along the track.
            float margin = 2.5f * s;
            float knob = track.Height - 2 * margin;
            float travel = track.Width - 2 * margin - knob;
            float x = track.Left + margin + travel * position;
            float y = track.Top + margin;
            for (int i = 3; i >= 1; i--)
            {
                using (var shadow = new SolidBrush(Color.FromArgb(alpha * 22 / 255, 0, 0, 0)))
                    g.FillEllipse(shadow, x - i * 0.5f * s, y - i * 0.25f * s + s, knob + i * s, knob + i * s);
            }
            using (var knobBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillEllipse(knobBrush, x, y, knob, knob);

            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Theme.RoundedRectangle(new RectangleF(0.75f * s, 0.75f * s, Width - 1.5f * s - 1, Height - 1.5f * s - 1), Height / 2f))
                using (var pen = new Pen(Theme.TextPrimary, 1.25f * s))
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
