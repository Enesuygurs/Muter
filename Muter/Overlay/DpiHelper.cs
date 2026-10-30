using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Muter.Overlay
{
    internal static class DpiHelper
    {
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        // Scale factor (1.0 = 96 DPI) of the monitor showing the given screen.
        public static float GetScale(ScreenInfo screen)
        {
            try
            {
                var center = new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
                IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                    return dpiX / 96f;
            }
            catch (DllNotFoundException)
            {
                // Windows 7/8: no per-monitor DPI API.
            }
            catch (EntryPointNotFoundException)
            {
            }

            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                return graphics.DpiX / 96f;
        }

        // For the log: how Windows treats this process ("PerMonitor" is expected).
        public static string DescribeProcessAwareness()
        {
            try
            {
                if (GetProcessDpiAwareness(IntPtr.Zero, out int awareness) == 0)
                    return awareness == 2 ? "PerMonitor" : awareness == 1 ? "System" : "Unaware";
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            return "unknown";
        }

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
