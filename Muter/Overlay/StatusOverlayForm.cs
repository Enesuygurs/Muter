using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Muter.Localization;
using Muter.UI;
using Muter.UI.Controls;

namespace Muter.Overlay
{
    // The large overlay: a dark panel with a ring in the state colour, the title, a state disc with the microphone and
    // the state word (see StatusSurfaces). Shown when the state changes, kept for the configured
    // display time and then faded out - or kept visible permanently when the display time is "always".
    internal sealed class StatusOverlayForm : OverlayWindow
    {
        public const int AlwaysVisible = 0;
        private const int FadeStepMs = 25;
        private const int FadeDurationMs = 400;

        private readonly Timer holdTimer = new Timer();
        private readonly Timer fadeTimer = new Timer { Interval = FadeStepMs };
        private StatusPresentation presentation;
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
        }

        public StatusPresentation Presentation => presentation;

        public bool OverlayEnabled => overlayEnabled;

        public int DisplayTimeMs => displayTimeMs;

        protected override Size BaseSize => StatusSurfaces.OverlayBaseSize;

        public void SetPresentation(StatusPresentation value)
        {
            presentation = value;
            Redraw();
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
            Alpha = 1;
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

        protected override Color EdgeColor => MoveMode ? Theme.MoveFrame : Theme.StateRing(presentation?.Visual ?? StatusVisual.Unknown);

        protected override GraphicsPath Shape() => Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), StatusSurfaces.OverlayRadius(RenderScale));

        protected override void Render(Graphics g)
        {
            if (presentation == null)
                return;
            StatusSurfaces.DrawOverlay(g, new RectangleF(0, 0, Width, Height), RenderScale, presentation.Visual,
                MoveMode ? Strings.Current.DragToMove : presentation.Title, presentation.StateText, withText: true,
                ringOverride: MoveMode ? Theme.MoveFrame : (Color?)null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                holdTimer.Dispose();
                fadeTimer.Dispose();
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
            double next = Alpha - (double)FadeStepMs / FadeDurationMs;
            if (next > 0.01)
            {
                Alpha = next;
                return;
            }
            HideNow();
        }

        private void HideNow()
        {
            holdTimer.Stop();
            fadeTimer.Stop();
            Hide();
            Alpha = 1;
        }
    }
}
