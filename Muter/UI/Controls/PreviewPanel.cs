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
        private static readonly Color Screen = Color.FromArgb(0x0F, 0x11, 0x14);
        private static readonly Color WindowA = Color.FromArgb(0x23, 0x27, 0x2E);
        private static readonly Color WindowB = Color.FromArgb(0x1C, 0x20, 0x26);
        private static readonly Color WindowLine = Color.FromArgb(0x2E, 0x33, 0x3B);
        private static readonly Color TextLine = Color.FromArgb(0x30, 0x35, 0x3E);
        private static readonly Color Taskbar = Color.FromArgb(0x1A, 0x1D, 0x22);

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
                    var box = new Size((int)Math.Round(StatusSurfaces.OverlayBaseSize.Width * k), (int)Math.Round(StatusSurfaces.OverlayBaseSize.Height * k));
                    Point at = Place(work, box, (int)(6 * s));
                    StatusSurfaces.DrawOverlay(g, new RectangleF(at.X, at.Y, box.Width, box.Height), k, StatusVisual.Live, string.Empty, string.Empty, withText: false);
                }
                else
                {
                    int side = (int)Math.Round(9 * factor * s);
                    Point at = Place(work, new Size(side, side), (int)(6 * s));
                    StatusSurfaces.DrawMini(g, new RectangleF(at.X, at.Y, side, side), StatusVisual.Live);
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
