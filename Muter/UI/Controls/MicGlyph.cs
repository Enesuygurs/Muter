using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Muter.UI.Controls
{
    // The microphone drawn as vectors on a 24-unit grid: a capsule, the cradle arc, the stem and the base. OFF adds a
    // slash with a gap cut around it, No device is drawn as an outline.
    internal static class MicGlyph
    {
        // gap: the colour behind the glyph, used for the cut around the slash (Color.Transparent with
        // CompositingMode.SourceCopy cuts real transparency into an icon).
        public static void Draw(Graphics g, RectangleF box, Color color, bool outline, bool slash, Color gap, float strokeUnits = 2f)
        {
            float s = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - s) / 2;
            float y = box.Y + (box.Height - s) / 2;
            float u = s / 24f;
            float sw = strokeUnits * u;
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var capsule = new RectangleF(x + 8 * u, y + 2 * u, 8 * u, 12 * u);
            if (outline)
            {
                RectangleF r = capsule;
                r.Inflate(-sw / 2, -sw / 2);
                using (GraphicsPath path = Theme.RoundedRectangle(r, 4 * u - sw / 2))
                using (var pen = new Pen(color, sw))
                    g.DrawPath(pen, path);
            }
            else
            {
                Theme.Fill(g, capsule, 4 * u, color);
            }

            using (var pen = new Pen(color, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLine(x + 19 * u, y + 9 * u, x + 19 * u, y + 11 * u);
                    path.AddArc(x + 5 * u, y + 4 * u, 14 * u, 14 * u, 0, 180);
                    path.AddLine(x + 5 * u, y + 11 * u, x + 5 * u, y + 9 * u);
                    g.DrawPath(pen, path);
                }
                g.DrawLine(pen, x + 12 * u, y + 18 * u, x + 12 * u, y + 21 * u);
                g.DrawLine(pen, x + 8 * u, y + 21 * u, x + 16 * u, y + 21 * u);
            }

            if (slash)
            {
                CompositingMode compositing = g.CompositingMode;
                if (gap.A == 0)
                    g.CompositingMode = CompositingMode.SourceCopy;
                using (var gapPen = new Pen(gap, sw + 3.2f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(gapPen, x + 3.5f * u, y + 2.5f * u, x + 20.5f * u, y + 21.5f * u);
                g.CompositingMode = compositing;
                using (var pen = new Pen(color, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, x + 3.5f * u, y + 2.5f * u, x + 20.5f * u, y + 21.5f * u);
            }
            g.SmoothingMode = smoothing;
        }

        // The glyph of a state in its ink colour on its state fill.
        public static void DrawState(Graphics g, RectangleF box, StatusVisual visual)
        {
            Draw(g, box, Theme.StateInk(visual), visual == StatusVisual.NoDevice, visual == StatusVisual.Muted, Theme.StateFill(visual));
        }
    }
}
