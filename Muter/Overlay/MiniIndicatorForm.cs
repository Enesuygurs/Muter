using System.Drawing;
using System.Drawing.Drawing2D;
using Muter.UI;
using Muter.UI.Controls;

namespace Muter.Overlay
{
    // Small, permanently visible, click-through status square: a solid rounded square in the state colour with the
    // microphone glyph (green = ON, red = OFF with a slash, grey outline = no device, amber = unknown).
    internal sealed class MiniIndicatorForm : OverlayWindow
    {
        private StatusPresentation presentation;
        private bool indicatorEnabled;

        public StatusPresentation Presentation => presentation;

        public bool IndicatorEnabled => indicatorEnabled;

        protected override Size BaseSize => new Size(28, 28);

        public void SetPresentation(StatusPresentation value)
        {
            presentation = value;
            Redraw();
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

        protected override Color EdgeColor => MoveMode ? Theme.MoveFrame : Theme.StateRing(Presentation?.Visual ?? StatusVisual.Unknown);

        protected override GraphicsPath Shape() => Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), StatusSurfaces.MiniRadius(Width));

        protected override void Render(Graphics g)
        {
            if (presentation == null)
                return;
            StatusSurfaces.DrawMini(g, new RectangleF(0, 0, Width, Height), presentation.Visual, MoveMode ? Theme.MoveFrame : (Color?)null);
        }
    }
}
