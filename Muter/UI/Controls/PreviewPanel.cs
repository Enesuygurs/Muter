using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Muter.Overlay;

namespace Muter.UI.Controls
{
    // The preview card content of the Overlay and Mini indicator pages: a small picture of a screen with the overlay or
    // the mini indicator where it will appear, and two lines of text (position; size and display time) next to it.
    public class PreviewPanel : Control
    {
        private const int ThumbWidth = 220;
        private const int ThumbHeight = 124;
        private static readonly Color Screen = Color.FromArgb(0x0B, 0x0C, 0x18);
        private static readonly Color WindowA = Color.FromArgb(0x1F, 0x22, 0x40);
        private static readonly Color WindowB = Color.FromArgb(0x18, 0x1A, 0x2E);
        private static readonly Color WindowLine = Color.FromArgb(0x2A, 0x2D, 0x4A);
        private static readonly Color TextLine = Color.FromArgb(0x2E, 0x32, 0x57);
        private static readonly Color Taskbar = Color.FromArgb(0x14, 0x16, 0x2A);

        private bool forOverlay = true;
        private OverlayAnchor anchor = OverlayAnchor.LowerCenter;
        private OverlaySize size = OverlaySize.Medium;
        private PointF customPosition;
        private bool shown = true;
        private string firstLine = string.Empty;
        private string secondLine = string.Empty;
        private Font secondFont;

        public PreviewPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Size = new Size(476, 156);
        }

        // The button under the text (e.g. "Show now"); optional.
        public FlatButton Button { get; private set; }

        public bool ForOverlay
        {
            get => forOverlay;
            set
            {
                forOverlay = value;
                Invalidate();
            }
        }

        public void AddButton(FlatButton button)
        {
            Button = button;
            button.BackColor = Theme.Surface;
            Controls.Add(button);
            PlaceButton();
        }

        // customPosition: for OverlayAnchor.Custom, the window's top-left as a fraction of the working area.
        public void SetPreview(OverlayAnchor anchor, OverlaySize size, PointF customPosition, bool shown, string firstLine, string secondLine)
        {
            this.anchor = anchor;
            this.size = size;
            this.customPosition = customPosition;
            this.shown = shown;
            this.firstLine = firstLine ?? string.Empty;
            this.secondLine = secondLine ?? string.Empty;
            Invalidate();
        }

        private float DpiScale => DeviceDpi / 96f;

        private Rectangle ThumbBounds
        {
            get
            {
                float s = DpiScale;
                return new Rectangle((int)(8 * s), (Height - (int)(ThumbHeight * s)) / 2, (int)(ThumbWidth * s), (int)(ThumbHeight * s));
            }
        }

        private int TextLeft => ThumbBounds.Right + (int)(20 * DpiScale);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PlaceButton();
        }

        private void PlaceButton()
        {
            if (Button != null)
                Button.Location = new Point(TextLeft, ThumbBounds.Bottom - Button.Height);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            secondFont?.Dispose();
            secondFont = null;
            base.OnFontChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                secondFont?.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = DpiScale;
            Rectangle screen = ThumbBounds;
            Theme.FillBox(g, screen, 6 * s, Screen, Theme.ButtonBorder, Math.Max(1, (int)s));

            // A couple of windows and a taskbar.
            g.SmoothingMode = SmoothingMode.None;
            var window = new Rectangle(screen.X + (int)(18 * s), screen.Y + (int)(16 * s), (int)(120 * s), (int)(70 * s));
            Theme.Fill(g, new Rectangle(screen.X + (int)(110 * s), screen.Y + (int)(40 * s), (int)(92 * s), (int)(58 * s)), 3 * s, WindowB);
            Theme.Fill(g, window, 3 * s, WindowA);
            using (var line = new SolidBrush(WindowLine))
                g.FillRectangle(line, window.X, window.Y + (int)(10 * s), window.Width, Math.Max(1, (int)s));
            for (int i = 0; i < 4; i++)
                Theme.Fill(g, new RectangleF(window.X + 8 * s, window.Y + (18 + i * 10) * s, (60 + i * 23 % 40) * s, 4 * s), 2 * s, TextLine);
            int taskbar = (int)(9 * s);
            using (var bar = new SolidBrush(Taskbar))
                g.FillRectangle(bar, screen.X + 1, screen.Bottom - taskbar - 1, screen.Width - 2, taskbar);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (shown)
            {
                var work = new Rectangle(screen.X + 1, screen.Y + 1, screen.Width - 2, screen.Height - taskbar - 2);
                float factor = OverlayPlacement.SizeFactor(size);
                if (forOverlay)
                {
                    float k = 0.22f * factor * s;
                    var box = new Size((int)Math.Round(122 * k), (int)Math.Round(151 * k));
                    Point at = Place(work, box, (int)(6 * s));
                    var rect = new Rectangle(at, box);
                    g.FillRectangle(Brushes.Black, rect);
                    MicGlyph.Draw(g, new RectangleF(rect.X + rect.Width * 0.2f, rect.Y + rect.Height * 0.25f, rect.Width * 0.6f, rect.Height * 0.5f), Color.White, false, false, Color.Black);
                }
                else
                {
                    int side = (int)Math.Round(9 * factor * s);
                    Point at = Place(work, new Size(side, side), (int)(6 * s));
                    int strip = Math.Max(1, side / 6);
                    g.FillRectangle(Brushes.Black, at.X, at.Y, side, side);
                    using (var green = new SolidBrush(Theme.LiveFill))
                        g.FillRectangle(green, at.X, at.Y + side - strip, side, strip);
                    MicGlyph.Draw(g, new RectangleF(at.X + 1, at.Y + 1, side - 2, side - strip - 2), Color.White, false, false, Color.Black);
                }
            }

            int left = TextLeft;
            int width = Width - left - (int)(8 * s);
            if (secondFont == null)
                secondFont = new Font(Font.FontFamily, 9f);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, firstLine, Font, new Rectangle(left, screen.Y + (int)(6 * s), width, (int)(20 * s)), Theme.TextPrimary, Theme.Surface, flags);
            TextRenderer.DrawText(g, secondLine, secondFont, new Rectangle(left, screen.Y + (int)(28 * s), width, (int)(18 * s)), Theme.TextSecondary, Theme.Surface, flags);
        }

        private Point Place(Rectangle work, Size box, int margin)
        {
            var offset = new Point((int)Math.Round(customPosition.X * work.Width), (int)Math.Round(customPosition.Y * work.Height));
            return OverlayPlacement.ComputeLocation(work, box, anchor, offset, margin);
        }
    }
}
