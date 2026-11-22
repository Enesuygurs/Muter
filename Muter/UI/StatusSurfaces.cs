using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Muter.UI.Controls;

namespace Muter.UI
{
    // Draws the status surfaces: the overlay (dark panel with a state-coloured ring, a state disc with the microphone,
    // the title and the state word) and the mini indicator (a solid state-coloured rounded square). Used by the real
    // windows and by the previews in the settings window.
    internal static class StatusSurfaces
    {
        // Overlay size in design units (Medium, 96 DPI).
        public static readonly Size OverlayBaseSize = new Size(120, 150);

        public static float OverlayRadius(float k) => 12 * k;

        public static float MiniRadius(float size) => Math.Max(4, size * 0.24f);

        // k: pixels per design unit. withText: false draws only the ring and the disc (small previews).
        public static void DrawOverlay(Graphics g, RectangleF bounds, float k, StatusVisual visual, string title, string state, bool withText,
            Color? ringOverride = null)
        {
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float ring = Math.Max(1, (float)Math.Round(2 * k));
            Theme.FillBox(g, bounds, OverlayRadius(k), Theme.OverlayPanel, ringOverride ?? Theme.StateRing(visual), ring);

            float cx = bounds.X + bounds.Width / 2f;
            float disc = 60 * k;
            float cy = bounds.Y + (withText ? 64 : 75) * k;
            using (var brush = new SolidBrush(Theme.StateFill(visual)))
                g.FillEllipse(brush, cx - disc / 2, cy - disc / 2, disc, disc);
            float glyph = 34 * k;
            MicGlyph.DrawState(g, new RectangleF(cx - glyph / 2, cy - glyph / 2, glyph, glyph), visual);

            if (visual == StatusVisual.Unknown)
                DrawUnknownBadge(g, cx + disc * 0.36f, cy - disc * 0.36f, k);

            if (withText)
            {
                const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                int inset = (int)Math.Ceiling(ring + 4 * k);
                int width = (int)bounds.Width - 2 * inset;
                using (Font titleFont = FitFont(title, Theme.FontFamily, 12f * k, width))
                using (Font stateFont = FitFont(state, Theme.SemiboldFontFamily, 16f * k, width))
                {
                    var titleBox = new Rectangle((int)bounds.X + inset, (int)(bounds.Y + 10 * k), width, (int)(20 * k));
                    var stateBox = new Rectangle((int)bounds.X + inset, (int)(bounds.Y + 102 * k), width, (int)(30 * k));
                    TextRenderer.DrawText(g, title, titleFont, titleBox, Theme.TextSecondary, Theme.OverlayPanel, flags);
                    TextRenderer.DrawText(g, state, stateFont, stateBox, Theme.StateText(visual), Theme.OverlayPanel, flags);
                }
            }
            g.SmoothingMode = smoothing;
        }

        // The font at the given pixel size, made smaller if the text would not fit the width.
        internal static Font FitFont(string text, string family, float pixels, int width)
        {
            var font = new Font(family, pixels, GraphicsUnit.Pixel);
            int needed = TextRenderer.MeasureText(text ?? string.Empty, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            if (needed <= width || needed == 0)
                return font;
            font.Dispose();
            return new Font(family, Math.Max(6f, pixels * width / needed), GraphicsUnit.Pixel);
        }

        // "!" in an amber circle with a panel-coloured cut, on the top right of the disc.
        private static void DrawUnknownBadge(Graphics g, float x, float y, float k)
        {
            float outer = 22 * k;
            float inner = outer - 4 * k;
            using (var panel = new SolidBrush(Theme.OverlayPanel))
                g.FillEllipse(panel, x - outer / 2, y - outer / 2, outer, outer);
            using (var amber = new SolidBrush(Theme.UnknownFill))
                g.FillEllipse(amber, x - inner / 2, y - inner / 2, inner, inner);
            using (var pen = new Pen(Theme.UnknownInk, Math.Max(1.2f, 2.2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, x, y - 4.5f * k, x, y + 0.8f * k);
            float dot = 2.4f * k;
            using (var ink = new SolidBrush(Theme.UnknownInk))
                g.FillEllipse(ink, x - dot / 2, y + 4.2f * k - dot / 2, dot, dot);
        }

        public static void DrawMini(Graphics g, RectangleF bounds, StatusVisual visual, Color? ringOverride = null)
        {
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float size = Math.Min(bounds.Width, bounds.Height);
            float radius = MiniRadius(size);
            if (ringOverride.HasValue)
                Theme.FillBox(g, bounds, radius, Theme.StateFill(visual), ringOverride.Value, Math.Max(1.5f, size / 14f));
            else if (visual == StatusVisual.NoDevice)
                Theme.FillBox(g, bounds, radius, Theme.NoDeviceFill, Theme.NoDeviceRing, Math.Max(1, size / 28f));
            else
                Theme.Fill(g, bounds, radius, Theme.StateFill(visual));
            float glyph = size * 0.72f;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f + size * 0.01f;
            MicGlyph.DrawState(g, new RectangleF(cx - glyph / 2, cy - glyph / 2, glyph, glyph), visual);
            g.SmoothingMode = smoothing;
        }
    }
}
