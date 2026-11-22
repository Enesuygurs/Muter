using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    // monitor it is on. It is a layered window with per-pixel alpha, so its rounded corners are smooth over anything
    // behind it. Subclasses render at RenderScale and call Redraw when their content changes.
    internal abstract class OverlayWindow : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
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
        private double alpha = 1;

        protected OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
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

        // Opacity of the whole window (0..1), used for fading; Form.Opacity is not used with per-pixel alpha.
        public double Alpha
        {
            get => alpha;
            set
            {
                double clamped = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(clamped - alpha) < 0.0001)
                    return;
                alpha = clamped;
                Redraw();
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
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
            Redraw();
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

        protected virtual void OnRenderScaleChanged()
        {
        }

        // Draws the window content on an opaque bitmap of the window size; the parts outside Shape are cut away.
        protected abstract void Render(Graphics graphics);

        // The outline of the window (rounded rectangle) in client pixels.
        protected abstract GraphicsPath Shape();

        // The colour at the outline's edge; the anti-aliased edge pixels are this colour with partial alpha.
        protected abstract Color EdgeColor { get; }

        // Renders the content and hands it to Windows with per-pixel alpha.
        protected void Redraw()
        {
            if (IsDisposed || !IsHandleCreated || Width <= 0 || Height <= 0)
                return;
            try
            {
                using (Bitmap frame = Compose())
                    Present(frame);
            }
            catch (Exception ex)
            {
                Log.Warn("Drawing the overlay failed", ex);
            }
        }

        // The composed image with straight alpha (also for tests).
        internal Bitmap Compose()
        {
            int width = Width, height = Height;
            using (var content = new Bitmap(width, height, PixelFormat.Format32bppRgb))
            using (var mask = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(content))
                {
                    g.Clear(EdgeColor);
                    Render(g);
                }
                using (Graphics g = Graphics.FromImage(mask))
                using (GraphicsPath shape = Shape())
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.FillPath(Brushes.White, shape);
                }

                var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                BitmapData c = content.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                BitmapData m = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData f = frame.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var contentRow = new int[width];
                    var maskRow = new int[width];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(c.Scan0 + y * c.Stride, contentRow, 0, width);
                        Marshal.Copy(m.Scan0 + y * m.Stride, maskRow, 0, width);
                        for (int x = 0; x < width; x++)
                            contentRow[x] = (contentRow[x] & 0x00FFFFFF) | (maskRow[x] & unchecked((int)0xFF000000));
                        Marshal.Copy(contentRow, 0, f.Scan0 + y * f.Stride, width);
                    }
                }
                finally
                {
                    content.UnlockBits(c);
                    mask.UnlockBits(m);
                    frame.UnlockBits(f);
                }
                return frame;
            }
        }

        private void Present(Bitmap frame)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = frame.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memory, bitmap);
            try
            {
                var size = new NativeSize { Width = frame.Width, Height = frame.Height };
                var source = new NativePoint();
                var blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)Math.Round(alpha * 255), AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, memory, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
            }
            finally
            {
                SelectObject(memory, old);
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Redraw();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Redraw();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Layered windows are drawn by Redraw.
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
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
            else
                Redraw();
        }

        private void OnDragFinished()
        {
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.All();
            ScreenInfo screen = OverlayPlacement.ScreenForBounds(screens, Bounds);
            Point location = OverlayPlacement.Clamp(Location, Size, screen.WorkingArea);
            Dragged?.Invoke(this, new OverlayDraggedEventArgs(screen.DeviceName, OverlayPlacement.ToCustomOffset(location, screen.WorkingArea)));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref NativeSize psize, IntPtr hdcSrc,
            ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
