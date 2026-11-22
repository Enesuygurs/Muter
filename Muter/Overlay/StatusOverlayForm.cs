using System;
using System.Drawing;
using System.Windows.Forms;
using Muter.Localization;
using Muter.UI;

namespace Muter.Overlay
{
    // The large overlay (title, microphone image, ON/OFF text). Shown when the state changes, kept for the configured
    // display time and then faded out - or kept visible permanently when the display time is "always".
    internal sealed class StatusOverlayForm : OverlayWindow
    {
        public const int AlwaysVisible = 0;
        private const int FadeStepMs = 25;
        private const int FadeDurationMs = 400;

        private readonly Timer holdTimer = new Timer();
        private readonly Timer fadeTimer = new Timer { Interval = FadeStepMs };
        private StatusPresentation presentation;
        private Font font;
        private bool overlayEnabled = true;
        private int displayTimeMs = 1000;

        public StatusOverlayForm()
        {
            holdTimer.Tick += (s, e) =>
            {
                holdTimer.Stop();
                fadeTimer.Start();
            };
            fadeTimer.Tick += (s, e) => FadeStep();
            OnRenderScaleChanged();
        }

        public StatusPresentation Presentation => presentation;

        public bool OverlayEnabled => overlayEnabled;

        public int DisplayTimeMs => displayTimeMs;

        protected override Size BaseSize => new Size(122, 151);

        public void SetPresentation(StatusPresentation value)
        {
            presentation = value;
            Invalidate();
        }

        public void Configure(bool enabled, int displayTime)
        {
            overlayEnabled = enabled;
            displayTimeMs = Math.Max(0, displayTime);
            ApplySteadyState();
        }

        // Shows the overlay and (unless it is always visible or being moved) fades it out after the display time.
        public void ShowTransient()
        {
            if (IsDisposed)
                return;
            if (!overlayEnabled)
            {
                HideNow();
                return;
            }

            UpdateLayout();
            holdTimer.Stop();
            fadeTimer.Stop();
            Opacity = 1;
            ShowOnTop();
            if (MoveMode || displayTimeMs == AlwaysVisible)
                return;
            holdTimer.Interval = displayTimeMs;
            holdTimer.Start();
        }

        public override void SetMoveMode(bool enabled)
        {
            base.SetMoveMode(enabled);
            ApplySteadyState();
        }

        protected override void OnRenderScaleChanged()
        {
            font?.Dispose();
            font = new Font("Segoe UI Semibold", 13.333f * RenderScale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (presentation == null)
                return;

            Graphics g = e.Graphics;
            Rectangle titleBar = Scaled(0, 0, BaseSize.Width, 23);
            Rectangle imageBox = Scaled(0, 28, BaseSize.Width, 96);
            Rectangle stateBar = Scaled(0, 128, BaseSize.Width, 23);

            g.FillRectangle(Brushes.Black, titleBar);
            g.FillRectangle(Brushes.Black, imageBox);
            g.FillRectangle(Brushes.Black, stateBar);

            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, MoveMode ? Strings.Current.DragToMove : presentation.Title, font, titleBar, Color.White, Color.Black, flags);
            TextRenderer.DrawText(g, presentation.StateText, font, stateBar, Color.White, Color.Black, flags);

            Image image = presentation.ShowsMuted ? AppResources.ImageMuted
                : presentation.Visual == StatusVisual.NoDevice ? AppResources.ImageNoDevice
                : AppResources.ImageLive;
            // Native image size at this scale, shrunk only if it does not fit (the glyphs are a bit taller than the area).
            Rectangle imageArea = Rectangle.Inflate(imageBox, -Px(6 * RenderScale), -Px(6 * RenderScale));
            var native = new Size(Px(image.Width * RenderScale), Px(image.Height * RenderScale));
            var box = new Rectangle(imageArea.Left + Math.Max(0, (imageArea.Width - native.Width) / 2), imageArea.Top + Math.Max(0, (imageArea.Height - native.Height) / 2),
                Math.Min(native.Width, imageArea.Width), Math.Min(native.Height, imageArea.Height));
            DrawImageFit(g, image, box);

            if (MoveMode)
                DrawMoveFrame(g);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                holdTimer.Dispose();
                fadeTimer.Dispose();
                font?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ApplySteadyState()
        {
            if (IsDisposed)
                return;
            if (!overlayEnabled)
                HideNow();
            else if (MoveMode || displayTimeMs == AlwaysVisible)
                ShowTransient();
            else if (Visible && !holdTimer.Enabled && !fadeTimer.Enabled)
            {
                holdTimer.Interval = displayTimeMs;
                holdTimer.Start();
            }
        }

        private void FadeStep()
        {
            double next = Opacity - (double)FadeStepMs / FadeDurationMs;
            if (next > 0.01)
            {
                Opacity = next;
                return;
            }
            HideNow();
        }

        private void HideNow()
        {
            holdTimer.Stop();
            fadeTimer.Stop();
            Hide();
            Opacity = 1;
        }
    }
}
