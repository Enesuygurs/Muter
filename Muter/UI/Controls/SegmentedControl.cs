using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // iOS-style segmented control (e.g. Small | Medium | Large): a dark track with a raised thumb that slides to the
    // selected segment.
    public class SegmentedControl : Control
    {
        private const int AnimationMs = 160;
        private static readonly Color Track = Color.FromArgb(32, 32, 34);
        private static readonly Color TrackBorder = Color.FromArgb(56, 56, 60);
        private static readonly Color ThumbHighlight = Color.FromArgb(72, 200, 56); // top of the green thumb, Theme.Accent below
        private static readonly Color Divider = Color.FromArgb(62, 62, 66);

        private readonly Timer animation = new Timer { Interval = 15 };
        private string[] segments = Array.Empty<string>();
        private int selectedIndex = -1;
        private int hoverIndex = -1;
        private float thumbPosition = -1; // segment index, fractional while sliding
        private float animationFrom;
        private DateTime animationStart;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Size = new Size(200, 32);
            animation.Tick += (s, e) => Animate();
        }

        // Raised when the user picks a segment (not when SelectedIndex is set from code).
        public event EventHandler SelectionChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<string> Segments
        {
            get => segments;
            set
            {
                segments = (value ?? Array.Empty<string>()).ToArray();
                Invalidate();
            }
        }

        // Setting it from code moves the thumb without animation.
        [DefaultValue(-1)]
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                selectedIndex = value;
                animation.Stop();
                thumbPosition = value;
                Invalidate();
            }
        }

        // What clicking a segment does: select it with the sliding animation.
        public void Choose(int index)
        {
            if (index < 0 || index >= segments.Length || !Enabled || index == selectedIndex)
                return;
            animationFrom = thumbPosition < 0 ? index : thumbPosition;
            selectedIndex = index;
            animationStart = DateTime.UtcNow;
            animation.Start();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            Choose(IndexAt(e.X));
            base.OnMouseClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selectedIndex > 0)
                Choose(selectedIndex - 1);
            else if (e.KeyCode == Keys.Right && selectedIndex < segments.Length - 1)
                Choose(selectedIndex + 1);
            base.OnKeyDown(e);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                animation.Dispose();
            base.Dispose(disposing);
        }

        private int IndexAt(int x) => segments.Length == 0 ? -1 : Math.Max(0, Math.Min(segments.Length - 1, x * segments.Length / Math.Max(1, Width)));

        private void Animate()
        {
            float t = Math.Min(1f, (float)(DateTime.UtcNow - animationStart).TotalMilliseconds / AnimationMs);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            thumbPosition = animationFrom + (selectedIndex - animationFrom) * eased;
            if (t >= 1f)
            {
                thumbPosition = selectedIndex;
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
            var track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = 8 * s;

            using (GraphicsPath path = Theme.RoundedRectangle(track, radius))
            using (var fill = new SolidBrush(Track))
            using (var pen = new Pen(Focused && ShowFocusCues ? Theme.TextPrimary : TrackBorder, s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            if (segments.Length == 0)
                return;

            float pad = 2.5f * s;
            float width = (track.Width - 2 * pad) / segments.Length;

            // Thin dividers between segments, hidden next to the thumb.
            using (var pen = new Pen(Divider, s))
            {
                for (int i = 1; i < segments.Length; i++)
                {
                    bool nearThumb = thumbPosition >= 0 && Math.Abs(i - 0.5f - thumbPosition) < 1f;
                    if (nearThumb)
                        continue;
                    float x = track.Left + pad + i * width;
                    g.DrawLine(pen, x, track.Top + 8 * s, x, track.Bottom - 8 * s);
                }
            }

            if (thumbPosition >= 0)
            {
                var thumb = new RectangleF(track.Left + pad + thumbPosition * width, track.Top + pad, width, track.Height - 2 * pad);
                for (int i = 2; i >= 1; i--)
                {
                    using (GraphicsPath shadow = Theme.RoundedRectangle(new RectangleF(thumb.X - i * 0.5f * s, thumb.Y + i * 0.5f * s, thumb.Width + i * s, thumb.Height + i * 0.5f * s), radius - pad))
                    using (var brush = new SolidBrush(Color.FromArgb(Enabled ? 40 : 15, 0, 0, 0)))
                        g.FillPath(brush, shadow);
                }
                using (GraphicsPath path = Theme.RoundedRectangle(thumb, radius - pad))
                using (var fill = Enabled
                    ? (Brush)new LinearGradientBrush(thumb, ThumbHighlight, Theme.Accent, LinearGradientMode.Vertical)
                    : new SolidBrush(Theme.ButtonFill))
                {
                    g.FillPath(fill, path);
                }
            }

            for (int i = 0; i < segments.Length; i++)
            {
                var cell = new RectangleF(track.Left + pad + i * width, track.Top, width, track.Height);
                bool selected = i == selectedIndex;
                Color text = !Enabled ? Theme.TextDisabled : selected || i == hoverIndex ? Theme.TextPrimary : Theme.TextSecondary;
                TextRenderer.DrawText(g, segments[i], Font, Rectangle.Round(cell), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
