using System.Drawing;
using System.Drawing.Drawing2D;

namespace Muter.UI.Controls
{
    // The flat dark theme shared by Muter's windows (design tokens). Midnight palette: deep navy with a violet accent.
    internal static class Theme
    {
        public static readonly Color Background = Hex(0x0F1020);   // window, title bar, navigation
        public static readonly Color Surface = Hex(0x181A2E);      // cards
        public static readonly Color Border = Hex(0x262944);       // card border, dividers
        public static readonly Color WindowBorder = Hex(0x2A2D4A);
        public static readonly Color TextPrimary = Hex(0xF5F6FF);
        public static readonly Color TextSecondary = Hex(0xA0A5C8); // hints, section headers
        public static readonly Color TextDisabled = Hex(0x5B6082);  // footer, "+" between keys

        public static readonly Color Accent = Hex(0x7B61FF);
        public static readonly Color AccentHover = Hex(0x8E78FF);
        public static readonly Color AccentPressed = Hex(0x6A50E8);
        public static readonly Color AccentText = Hex(0xFFFFFF);    // text on accent
        public static readonly Color Link = Hex(0x9B87FF);
        public static readonly Color Success = Hex(0x4ADE80);

        public static readonly Color Error = Hex(0xF87171);
        public static readonly Color ErrorBorder = Hex(0xF87171);

        public static readonly Color ButtonFill = Hex(0x23263F);    // controls: buttons, dropdowns
        public static readonly Color ButtonHover = Hex(0x2D3152);
        public static readonly Color ButtonPressed = Hex(0x1D2036);
        public static readonly Color ButtonBorder = Hex(0x33375A);
        public static readonly Color DisabledFill = Hex(0x1B1E33);
        public static readonly Color DisabledText = Hex(0x565B7C);
        public static readonly Color AccentDisabled = DisabledFill;
        public static readonly Color CloseHover = Color.FromArgb(196, 43, 28);

        public static readonly Color NavSelected = Hex(0x1F2240);
        public static readonly Color MenuHover = Hex(0x2D3152);
        public static readonly Color Popup = Hex(0x1D2036);
        public static readonly Color PopupBorder = Hex(0x33375A);
        public static readonly Color Inset = Hex(0x0B0C18);         // hotkey capture area
        public static readonly Color SegmentTrack = Hex(0x0F1020);
        public static readonly Color SegmentSelected = Hex(0x2D3152);
        public static readonly Color SegmentSelectedBorder = Hex(0x3A3F66);
        public static readonly Color SwitchOff = Hex(0x4A4F78);
        public static readonly Color SwitchKnob = Hex(0xFFFFFF);
        public static readonly Color DialogFooter = Hex(0x13152A);

        public static readonly Color KeycapFill = Hex(0x252845);
        public static readonly Color KeycapBorder = Hex(0x3A3F66);
        public static readonly Color KeycapShadow = Hex(0x080915);  // the darker bottom edge

        // Microphone states: fill, ink on the fill, text on a dark panel.
        public static readonly Color LiveFill = Hex(0x22C55E), LiveInk = Hex(0x052E16), LiveText = Hex(0x4ADE80);
        public static readonly Color MutedFill = Hex(0xEF4444), MutedInk = Hex(0xFFFFFF), MutedText = Hex(0xF87171);
        public static readonly Color NoDeviceFill = Hex(0x4B5563), NoDeviceInk = Hex(0xE5E7EB), NoDeviceText = Hex(0xA3AAB5), NoDeviceRing = Hex(0x6B7280);
        public static readonly Color UnknownFill = Hex(0xF59E0B), UnknownInk = Hex(0x2A1800), UnknownText = Hex(0xFBBF24);
        public static readonly Color OverlayPanel = Hex(0x15171C);

        // Overlay/mini indicator frame while it can be dragged (not a state colour).
        public static readonly Color MoveFrame = Hex(0x60A5FA);

        public const string FontFamily = "Segoe UI";
        public const string SemiboldFontFamily = "Segoe UI Semibold";
        public const string IconFontFamily = "Segoe MDL2 Assets";

        private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0.01f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            float d = System.Math.Min(radius * 2, System.Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // A filled rounded rectangle with a border of the given width (fill drawn inside the border).
        public static void FillBox(Graphics g, RectangleF r, float radius, Color fill, Color border, float borderWidth = 1)
        {
            using (var path = RoundedRectangle(r, radius))
            using (var brush = new SolidBrush(border))
                g.FillPath(brush, path);
            r.Inflate(-borderWidth, -borderWidth);
            using (var path = RoundedRectangle(r, System.Math.Max(0, radius - borderWidth)))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
        }

        public static Color StateFill(Muter.UI.StatusVisual visual) =>
            visual == Muter.UI.StatusVisual.Live ? LiveFill : visual == Muter.UI.StatusVisual.Muted ? MutedFill
            : visual == Muter.UI.StatusVisual.NoDevice ? NoDeviceFill : UnknownFill;

        public static Color StateInk(Muter.UI.StatusVisual visual) =>
            visual == Muter.UI.StatusVisual.Live ? LiveInk : visual == Muter.UI.StatusVisual.Muted ? MutedInk
            : visual == Muter.UI.StatusVisual.NoDevice ? NoDeviceInk : UnknownInk;

        public static Color StateText(Muter.UI.StatusVisual visual) =>
            visual == Muter.UI.StatusVisual.Live ? LiveText : visual == Muter.UI.StatusVisual.Muted ? MutedText
            : visual == Muter.UI.StatusVisual.NoDevice ? NoDeviceText : UnknownText;

        // The ring/edge around a state surface (No device gets a lighter ring than its fill).
        public static Color StateRing(Muter.UI.StatusVisual visual) =>
            visual == Muter.UI.StatusVisual.NoDevice ? NoDeviceRing : StateFill(visual);

        public static void Fill(Graphics g, RectangleF r, float radius, Color fill)
        {
            using (var path = RoundedRectangle(r, radius))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
        }
    }
}
