using System.Drawing;
using System.Drawing.Drawing2D;

namespace Muter.UI.Controls
{
    // Dark theme shared by Muter's windows; the accent is the green of the app icon.
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(31, 31, 31);
        public static readonly Color Surface = Color.FromArgb(43, 43, 43);
        public static readonly Color Border = Color.FromArgb(61, 61, 61);
        public static readonly Color TextPrimary = Color.FromArgb(243, 243, 243);
        public static readonly Color TextSecondary = Color.FromArgb(167, 167, 167);
        public static readonly Color TextDisabled = Color.FromArgb(110, 110, 110);

        public static readonly Color Accent = Color.FromArgb(56, 182, 41);
        public static readonly Color AccentHover = Color.FromArgb(67, 201, 52);
        public static readonly Color AccentPressed = Color.FromArgb(47, 156, 34);
        public static readonly Color AccentText = Color.FromArgb(10, 30, 8);
        public static readonly Color AccentDisabled = Color.FromArgb(44, 58, 42);

        public static readonly Color Error = Color.FromArgb(241, 112, 123);
        public static readonly Color ErrorBorder = Color.FromArgb(229, 72, 77);

        public static readonly Color ButtonFill = Color.FromArgb(48, 48, 48);
        public static readonly Color ButtonHover = Color.FromArgb(58, 58, 58);
        public static readonly Color ButtonPressed = Color.FromArgb(40, 40, 40);
        public static readonly Color ButtonBorder = Color.FromArgb(72, 72, 72);
        public static readonly Color CloseHover = Color.FromArgb(196, 43, 28);

        public static readonly Color KeycapFill = Color.FromArgb(62, 62, 62);
        public static readonly Color KeycapBorder = Color.FromArgb(84, 84, 84);
        public static readonly Color KeycapShadow = Color.FromArgb(22, 22, 22);

        public const string SemiboldFontFamily = "Segoe UI Semibold";
        public const string IconFontFamily = "Segoe MDL2 Assets";

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0 || bounds.Width < d || bounds.Height < d)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
