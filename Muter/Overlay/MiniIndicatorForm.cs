using System.Drawing;
using System.Windows.Forms;
using Muter.UI;

namespace Muter.Overlay
{
    // Small, permanently visible, click-through status square: microphone glyph with a colored strip
    // (green = ON, red = OFF, gray = no device, amber = unknown).
    internal sealed class MiniIndicatorForm : OverlayWindow
    {
        public static readonly Color LiveColor = Color.FromArgb(46, 204, 64);
        public static readonly Color MutedColor = Color.FromArgb(231, 76, 60);
        public static readonly Color NoDeviceColor = Color.FromArgb(140, 140, 140);
        public static readonly Color UnknownColor = Color.FromArgb(243, 156, 18);

        private StatusPresentation presentation;
        private bool indicatorEnabled;

        public StatusPresentation Presentation => presentation;

        public bool IndicatorEnabled => indicatorEnabled;

        protected override Size BaseSize => new Size(28, 28);

        public void SetPresentation(StatusPresentation value)
        {
            presentation = value;
            Invalidate();
            ApplyVisibility();
        }

        public void Configure(bool enabled)
        {
            indicatorEnabled = enabled;
            ApplyVisibility();
        }

        public override void SetMoveMode(bool enabled)
        {
            base.SetMoveMode(enabled);
            ApplyVisibility();
        }

        // Re-applies position (monitor may follow the mouse) and keeps the indicator above other topmost windows.
        public void ApplyVisibility()
        {
            if (IsDisposed)
                return;
            if (!indicatorEnabled || presentation == null)
            {
                Hide();
                return;
            }
            UpdateLayout();
            ShowOnTop();
        }

        public static Color ColorFor(StatusVisual visual)
        {
            switch (visual)
            {
                case StatusVisual.Live: return LiveColor;
                case StatusVisual.Muted: return MutedColor;
                case StatusVisual.NoDevice: return NoDeviceColor;
                default: return UnknownColor;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (presentation == null)
                return;

            Graphics g = e.Graphics;
            Rectangle all = ClientRectangle;
            int strip = Px(5 * RenderScale);
            g.FillRectangle(Brushes.Black, all);
            using (var brush = new SolidBrush(ColorFor(presentation.Visual)))
                g.FillRectangle(brush, new Rectangle(0, all.Height - strip, all.Width, strip));

            Image image = presentation.ShowsMuted ? AppResources.ImageMuted
                : presentation.Visual == StatusVisual.NoDevice ? AppResources.ImageNoDevice
                : AppResources.ImageLive;
            int pad = Px(3 * RenderScale);
            DrawImageFit(g, image, new Rectangle(pad, pad, all.Width - 2 * pad, all.Height - strip - 2 * pad));

            if (MoveMode)
                DrawMoveFrame(g);
        }
    }
}
