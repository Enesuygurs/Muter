using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Muter.Overlay
{
    public enum OverlayAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        Center,
        LowerCenter,
        BottomLeft,
        BottomCenter,
        BottomRight,
        Custom,
    }

    public enum OverlaySize
    {
        Small,
        Medium,
        Large,
    }

    public sealed class ScreenInfo
    {
        public ScreenInfo(string deviceName, Rectangle bounds, Rectangle workingArea, bool isPrimary)
        {
            DeviceName = deviceName ?? string.Empty;
            Bounds = bounds;
            WorkingArea = workingArea;
            IsPrimary = isPrimary;
        }

        public string DeviceName { get; }

        public Rectangle Bounds { get; }

        public Rectangle WorkingArea { get; }

        public bool IsPrimary { get; }

        public static IReadOnlyList<ScreenInfo> All()
        {
            return Screen.AllScreens.Select(s => new ScreenInfo(s.DeviceName, s.Bounds, s.WorkingArea, s.Primary)).ToList();
        }
    }

    public sealed class OverlayPlacementOptions
    {
        public OverlayAnchor Anchor { get; set; } = OverlayAnchor.LowerCenter;

        public OverlaySize Size { get; set; } = OverlaySize.Medium;

        // Monitor for preset positions: PrimaryMonitor, FollowMouseMonitor or a screen device name.
        public string Monitor { get; set; } = OverlayPlacement.PrimaryMonitor;

        // Where a dragged (Custom) window was dropped: the monitor and the offset from its working area's top-left.
        public string CustomMonitor { get; set; } = string.Empty;

        public Point CustomOffset { get; set; }
    }

    // Pure placement math, independent of real screens so it can be unit tested.
    public static class OverlayPlacement
    {
        public const string PrimaryMonitor = "";
        public const string FollowMouseMonitor = "*FollowMouse*";

        public static float SizeFactor(OverlaySize size)
        {
            switch (size)
            {
                case OverlaySize.Small: return 0.75f;
                case OverlaySize.Large: return 1.5f;
                default: return 1f;
            }
        }

        public static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, ignoreCase: true, result: out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : fallback;
        }

        public static ScreenInfo ResolveScreen(IReadOnlyList<ScreenInfo> screens, OverlayPlacementOptions options, Point cursor)
        {
            if (screens == null || screens.Count == 0)
                throw new ArgumentException("At least one screen is required.", nameof(screens));

            ScreenInfo primary = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];

            if (options.Anchor == OverlayAnchor.Custom)
            {
                ScreenInfo dropped = FindByName(screens, options.CustomMonitor);
                if (dropped != null)
                    return dropped;
            }

            if (options.Monitor == FollowMouseMonitor)
                return screens.FirstOrDefault(s => s.Bounds.Contains(cursor)) ?? Nearest(screens, cursor) ?? primary;

            return FindByName(screens, options.Monitor) ?? primary;
        }

        public static Point ComputeLocation(Rectangle workArea, Size size, OverlayAnchor anchor, Point customOffset, int margin)
        {
            int left = workArea.Left + margin;
            int right = workArea.Right - size.Width - margin;
            int top = workArea.Top + margin;
            int bottom = workArea.Bottom - size.Height - margin;
            int centerX = workArea.Left + (workArea.Width - size.Width) / 2;
            int centerY = workArea.Top + (workArea.Height - size.Height) / 2;

            Point location;
            switch (anchor)
            {
                case OverlayAnchor.TopLeft: location = new Point(left, top); break;
                case OverlayAnchor.TopCenter: location = new Point(centerX, top); break;
                case OverlayAnchor.TopRight: location = new Point(right, top); break;
                case OverlayAnchor.Center: location = new Point(centerX, centerY); break;
                case OverlayAnchor.BottomLeft: location = new Point(left, bottom); break;
                case OverlayAnchor.BottomCenter: location = new Point(centerX, bottom); break;
                case OverlayAnchor.BottomRight: location = new Point(right, bottom); break;
                case OverlayAnchor.Custom: location = new Point(workArea.Left + customOffset.X, workArea.Top + customOffset.Y); break;
                default: location = new Point(centerX, workArea.Top + workArea.Height * 3 / 4 - size.Height / 2); break; // LowerCenter
            }
            return Clamp(location, size, workArea);
        }

        // Keeps the window fully inside the area (top-left aligned when it is larger than the area).
        public static Point Clamp(Point location, Size size, Rectangle area)
        {
            int x = Math.Max(area.Left, Math.Min(location.X, area.Right - size.Width));
            int y = Math.Max(area.Top, Math.Min(location.Y, area.Bottom - size.Height));
            return new Point(x, y);
        }

        public static Point ToCustomOffset(Point location, Rectangle workArea) => new Point(location.X - workArea.Left, location.Y - workArea.Top);

        // The screen showing most of the window (nearest screen if it is off-screen).
        public static ScreenInfo ScreenForBounds(IReadOnlyList<ScreenInfo> screens, Rectangle bounds)
        {
            ScreenInfo best = null;
            long bestArea = 0;
            foreach (ScreenInfo screen in screens)
            {
                Rectangle overlap = Rectangle.Intersect(screen.Bounds, bounds);
                long area = (long)overlap.Width * overlap.Height;
                if (area > bestArea)
                {
                    best = screen;
                    bestArea = area;
                }
            }
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            return best ?? Nearest(screens, center) ?? screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        }

        private static ScreenInfo FindByName(IReadOnlyList<ScreenInfo> screens, string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName) || deviceName == FollowMouseMonitor)
                return null;
            return screens.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private static ScreenInfo Nearest(IReadOnlyList<ScreenInfo> screens, Point point)
        {
            return screens
                .OrderBy(s => DistanceSquared(s.Bounds, point))
                .FirstOrDefault();
        }

        private static long DistanceSquared(Rectangle rect, Point point)
        {
            long dx = point.X < rect.Left ? rect.Left - point.X : point.X >= rect.Right ? point.X - rect.Right + 1 : 0;
            long dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y >= rect.Bottom ? point.Y - rect.Bottom + 1 : 0;
            return dx * dx + dy * dy;
        }
    }
}
