using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Muter.Overlay
{
    public sealed class OverlayDraggedEventArgs : EventArgs
    {
        public OverlayDraggedEventArgs(string monitor, Point offset)
        {
            Monitor = monitor;
            Offset = offset;
        }

        public string Monitor { get; }

        public Point Offset { get; }
    }

    // Base for the always-on-top overlay windows: never takes focus, hidden from the taskbar and Alt+Tab,
    // click-through unless it is being moved, positioned per OverlayPlacementOptions and sized for the DPI of the
    // monitor it is on. Subclasses paint at RenderScale.
    internal abstract class OverlayWindow : Form
    {
        protected static readonly Color KeyColor = Color.Lime;

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_DPICHANGED = 0x02E0;
        private const int MA_NOACTIVATE = 3;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private OverlayPlacementOptions placement = new OverlayPlacementOptions();
        private bool moveMode;
        private bool inSizeMove;

        protected OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            Text = "Muter";
            Icon = AppResources.AppIcon;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        // Raised after the user dragged the window in move mode.
        public event EventHandler<OverlayDraggedEventArgs> Dragged;

        public bool MoveMode => moveMode;

        public OverlayPlacementOptions Placement => placement;

        // Pixels per design unit: size factor x monitor DPI scale.
        protected float RenderScale { get; private set; } = 1f;

        // Size in design units (96 DPI, Medium).
        protected abstract Size BaseSize { get; }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                if (!moveMode)
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void SetPlacement(OverlayPlacementOptions options)
        {
            placement = options ?? throw new ArgumentNullException(nameof(options));
            UpdateLayout();
        }

        public virtual void SetMoveMode(bool enabled)
        {
            if (moveMode == enabled)
                return;
            moveMode = enabled;
            if (IsHandleCreated)
                UpdateStyles();
            Cursor = enabled ? Cursors.SizeAll : Cursors.Default;
            Invalidate();
        }

        // Recomputes size and position for the current settings, monitors and DPI.
        public void UpdateLayout()
        {
            if (inSizeMove || IsDisposed)
                return;
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.All();
            ScreenInfo screen = OverlayPlacement.ResolveScreen(screens, placement, Cursor.Position);
            float dpiScale = DpiHelper.GetScale(screen);
            SetRenderScale(OverlayPlacement.SizeFactor(placement.Size) * dpiScale);

            int margin = (int)Math.Round(16 * dpiScale);
            Point location = OverlayPlacement.ComputeLocation(screen.WorkingArea, Size, placement.Anchor, placement.CustomOffset, margin);
            if (Location != location)
                Location = location;
        }

        // Shows without activating and puts the window back on top of other topmost windows.
        public void ShowOnTop()
        {
            if (IsDisposed)
                return;
            if (!Visible)
                Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        protected static int Px(float value) => (int)Math.Round(value);

        protected Rectangle Scaled(int x, int y, int width, int height)
        {
            float s = RenderScale;
            int left = Px(x * s);
            int top = Px(y * s);
            return new Rectangle(left, top, Px((x + width) * s) - left, Px((y + height) * s) - top);
        }

        protected virtual void OnRenderScaleChanged()
        {
        }

        protected void DrawMoveFrame(Graphics graphics)
        {
            int width = Math.Max(2, Px(2 * RenderScale));
            using (var pen = new Pen(Color.OrangeRed, width) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected static void DrawImageFit(Graphics graphics, Image image, Rectangle box)
        {
            float fit = Math.Min((float)box.Width / image.Width, (float)box.Height / image.Height);
            int width = Px(image.Width * fit);
            int height = Px(image.Height * fit);
            var target = new Rectangle(box.Left + (box.Width - width) / 2, box.Top + (box.Height - height) / 2, width, height);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(image, target);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return;
                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    if (moveMode && m.Result == (IntPtr)HTCLIENT)
                        m.Result = (IntPtr)HTCAPTION;
                    return;
                case WM_ENTERSIZEMOVE:
                    inSizeMove = true;
                    break;
                case WM_EXITSIZEMOVE:
                    inSizeMove = false;
                    base.WndProc(ref m);
                    OnDragFinished();
                    return;
                case WM_DPICHANGED:
                    // Handled here instead of by WinForms so it does not rescale the window on its own.
                    int dpi = (int)((long)m.WParam & 0xFFFF);
                    if (inSizeMove)
                    {
                        if (dpi > 0)
                            SetRenderScale(OverlayPlacement.SizeFactor(placement.Size) * dpi / 96f);
                    }
                    else
                    {
                        BeginInvoke(new Action(UpdateLayout));
                    }
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        private void SetRenderScale(float scale)
        {
            Size size = new Size(Math.Max(1, Px(BaseSize.Width * scale)), Math.Max(1, Px(BaseSize.Height * scale)));
            bool changed = Math.Abs(scale - RenderScale) > 0.0001f;
            RenderScale = scale;
            if (changed)
                OnRenderScaleChanged();
            if (Size != size)
                Size = size;
            Invalidate();
        }

        private void OnDragFinished()
        {
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.All();
            ScreenInfo screen = OverlayPlacement.ScreenForBounds(screens, Bounds);
            Point location = OverlayPlacement.Clamp(Location, Size, screen.WorkingArea);
            Dragged?.Invoke(this, new OverlayDraggedEventArgs(screen.DeviceName, OverlayPlacement.ToCustomOffset(location, screen.WorkingArea)));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
