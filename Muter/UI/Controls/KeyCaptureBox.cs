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

        // Width the compact keycaps need (at the current DPI).
        public int CompactWidth()
        {
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            float scale = DeviceDpi / 96f;
            float capHeight = 28 * scale;
            float total = 0;
            foreach (string cap in keycaps)
                total += Math.Max(capHeight, TextRenderer.MeasureText(cap, Font, Size.Empty, flags).Width + 16 * scale);
            total += Math.Max(0, keycaps.Count - 1) * 16 * scale;
            return (int)Math.Ceiling(total) + 1;
        }

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
                : state == KeyCaptureState.Valid || state == KeyCaptureState.Pending ? Theme.Accent
                : Focused ? Theme.SegmentSelectedBorder
                : Theme.ButtonBorder;
            float borderWidth = (state == KeyCaptureState.Idle ? 1f : 2f) * scale;
            Theme.FillBox(g, ClientRectangle, 8 * scale, Theme.Inset, border, borderWidth);

            if (caps.Count == 0)
            {
                TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle, Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            DrawKeycaps(g, caps, scale);
        }

        // Keycaps: fill with a 1 px border and a 2 px darker bottom edge, joined by a "+" in a 16 px (26 px large) slot.
        private void DrawKeycaps(Graphics g, List<string> caps, float scale)
        {
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            float capHeight = (Compact ? 28 : 36) * scale;
            float padding = (Compact ? 8 : 12) * scale;
            float plusWidth = (Compact ? 16 : 26) * scale;
            float edge = 2 * scale;

            var widths = new List<float>();
            float total = 0;
            foreach (string cap in caps)
            {
                float width = Math.Max(capHeight, TextRenderer.MeasureText(g, cap, Font, Size.Empty, flags).Width + 2 * padding);
                widths.Add(width);
                total += width;
            }
            total += (caps.Count - 1) * plusWidth;

            // Shrink the caps if a long combination would not fit.
            float available = Compact ? Width - 1 : Width - 24 * scale;
            float shrink = total > available ? available / total : 1f;
            float x = Compact ? Width - total * shrink : (Width - total * shrink) / 2;
            float top = (float)Math.Floor((Height - capHeight) / 2);

            for (int i = 0; i < caps.Count; i++)
            {
                float width = widths[i] * shrink;
                var cap = new RectangleF(x, top, width, capHeight);
                var face = new RectangleF(cap.X, cap.Y, cap.Width, cap.Height - edge);
                bool placeholder = caps[i] == "…";
                if (placeholder)
                {
                    using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(face.X + 0.5f, face.Y + 0.5f, face.Width - 1, face.Height - 1), 5 * scale))
                    using (var pen = new Pen(Theme.Accent, scale) { DashStyle = DashStyle.Dash })
                        g.DrawPath(pen, path);
                }
                else
                {
                    Theme.Fill(g, cap, 5 * scale, Theme.KeycapShadow);
                    Theme.FillBox(g, face, 5 * scale, Theme.KeycapFill, Theme.KeycapBorder, scale);
                }
                TextRenderer.DrawText(g, caps[i], Font, Rectangle.Round(face), placeholder ? Theme.TextSecondary : Theme.TextPrimary,
                    flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += width;

                if (i < caps.Count - 1)
                {
                    var plus = new RectangleF(x, top, plusWidth * shrink, capHeight - edge);
                    TextRenderer.DrawText(g, "+", Font, Rectangle.Round(plus), Theme.TextDisabled,
                        flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += plusWidth * shrink;
                }
            }
        }
    }
}
