using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    public enum KeyCaptureState
    {
        // Showing a combination, waiting for keys.
        Idle,

        // Modifiers are held, the final key is still missing.
        Pending,

        // A new combination that can be saved.
        Valid,

        // A combination that cannot be used.
        Invalid,
    }

    // The area where the new hotkey is pressed; draws the combination as keycaps.
    public class KeyCaptureBox : Control
    {
        private IReadOnlyList<string> keycaps = Array.Empty<string>();
        private KeyCaptureState state;

        public KeyCaptureBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
        }

        // Keycaps to show; when pending, a final "…" cap is added.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<string> Keycaps
        {
            get => keycaps;
            set
            {
                keycaps = value ?? Array.Empty<string>();
                Invalidate();
            }
        }

        [DefaultValue(KeyCaptureState.Idle)]
        public KeyCaptureState State
        {
            get => state;
            set
            {
                state = value;
                Invalidate();
            }
        }

        [DefaultValue("Press a key combination")]
        public string Placeholder { get; set; } = "Press a key combination";

        // Only the keycaps, right-aligned and without the frame (for showing the hotkey in other windows).
        [DefaultValue(false)]
        public bool Compact { get; set; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
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
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; // crisp, equal 1 px edges on every side
            float scale = DeviceDpi / 96f;

            var caps = new List<string>(keycaps);
            if (state == KeyCaptureState.Pending)
                caps.Add("…");
            if (Compact)
            {
                if (caps.Count > 0)
                    DrawKeycaps(g, caps, scale);
                return;
            }

            Color border = state == KeyCaptureState.Invalid ? Theme.ErrorBorder
                : state == KeyCaptureState.Valid || state == KeyCaptureState.Pending || Focused ? Theme.Accent
                : Theme.Border;
            float borderWidth = (state == KeyCaptureState.Idle && !Focused ? 1f : 2f) * scale;
            var bounds = new RectangleF(borderWidth / 2, borderWidth / 2, Width - borderWidth, Height - borderWidth);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 8 * scale))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(border, borderWidth))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (caps.Count == 0)
            {
                TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle, Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            DrawKeycaps(g, caps, scale);
        }

        private void DrawKeycaps(Graphics g, List<string> caps, float scale)
        {
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            float padding = (Compact ? 8 : 12) * scale;
            float gap = (Compact ? 5 : 8) * scale;
            float depth = 3 * scale;
            float capHeight = Compact ? Height - depth - 1 : Math.Min(Height - 20 * scale, Font.Height + 16 * scale);
            Size plusSize = TextRenderer.MeasureText(g, "+", Font, Size.Empty, flags);

            var widths = new List<float>();
            float total = 0;
            foreach (string cap in caps)
            {
                float width = Math.Max(capHeight, TextRenderer.MeasureText(g, cap, Font, Size.Empty, flags).Width + 2 * padding);
                widths.Add(width);
                total += width;
            }
            total += (caps.Count - 1) * (plusSize.Width + 2 * gap);

            // Shrink the caps if a long combination would not fit.
            float available = Compact ? Width - 1 : Width - 24 * scale;
            float shrink = total > available ? available / total : 1f;
            float x = Compact ? Width - 1 - total * shrink : (Width - total * shrink) / 2;
            float top = (Height - capHeight - depth) / 2;

            for (int i = 0; i < caps.Count; i++)
            {
                float width = widths[i] * shrink;
                var cap = new RectangleF(x, top, width, capHeight);
                bool placeholder = caps[i] == "…";
                using (GraphicsPath shadow = Theme.RoundedRectangle(new RectangleF(cap.X, cap.Y + depth, cap.Width, cap.Height), 6 * scale))
                using (var shadowBrush = new SolidBrush(Theme.KeycapShadow))
                    g.FillPath(shadowBrush, shadow);
                using (GraphicsPath path = Theme.RoundedRectangle(cap, 6 * scale))
                using (var fill = new SolidBrush(placeholder ? Theme.Surface : Theme.KeycapFill))
                using (var pen = new Pen(placeholder ? Theme.Accent : Theme.KeycapBorder, scale) { DashStyle = placeholder ? DashStyle.Dash : DashStyle.Solid })
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, caps[i], Font, Rectangle.Round(cap), placeholder ? Theme.TextSecondary : Theme.TextPrimary,
                    flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += width;

                if (i < caps.Count - 1)
                {
                    x += gap * shrink;
                    var plus = new RectangleF(x, top, plusSize.Width * shrink, capHeight);
                    TextRenderer.DrawText(g, "+", Font, Rectangle.Round(plus), Theme.TextSecondary,
                        flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += (plusSize.Width + gap) * shrink;
                }
            }
        }
    }
}
