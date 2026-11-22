using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Muter
{
    // Colourful (green / red / gray) or discreet: one plain colour like Windows' own tray icons, white for a dark
    // taskbar and black for a light one.
    internal enum TrayIconStyle
    {
        Colored,
        White,
        Black,
    }

    // Icons and images embedded from the Resources folder (see Muter.csproj). Loaded once and shared; never dispose them.
    internal static class AppResources
    {
        private static readonly Lazy<Icon> appIcon = new Lazy<Icon>(() => LoadIcon("muter.ico", SystemInformation.IconSize));
        private static readonly Dictionary<string, Icon> trayIcons = new Dictionary<string, Icon>();
        private static readonly Lazy<Bitmap> imageLive = new Lazy<Bitmap>(() => LoadBitmap("muteron.png"));
        private static readonly Lazy<Bitmap> imageMuted = new Lazy<Bitmap>(() => LoadBitmap("muteroff.png"));
        private static readonly Lazy<Bitmap> imageNoDevice = new Lazy<Bitmap>(() => LoadBitmap("nodevice.png"));

        public static Icon AppIcon => appIcon.Value;

        public static Icon TrayLive => TrayIcon(UI.StatusVisual.Live, false);

        public static Icon TrayMuted => TrayIcon(UI.StatusVisual.Muted, false);

        public static Icon TrayNoDevice => TrayIcon(UI.StatusVisual.NoDevice, false);

        // Tray icon for a state; Unknown looks like Live. The alternate instance has the same image; switching to it
        // forces the tray icon to be sent to Explorer again (NotifyIcon skips updates when the icon object is unchanged).
        public static Icon TrayIcon(UI.StatusVisual visual, bool alternate, TrayIconStyle style = TrayIconStyle.Colored)
        {
            string name = visual == UI.StatusVisual.Muted ? "muteroff"
                : visual == UI.StatusVisual.NoDevice ? "nodevice"
                : "muteron";
            if (style != TrayIconStyle.Colored)
                name += style == TrayIconStyle.White ? "-white" : "-black";
            string key = name + (alternate ? "|alternate" : string.Empty);
            lock (trayIcons)
            {
                if (!trayIcons.TryGetValue(key, out Icon icon))
                {
                    icon = alternate ? (Icon)TrayIcon(visual, false, style).Clone() : LoadIcon(name + ".ico", SystemInformation.SmallIconSize);
                    trayIcons[key] = icon;
                }
                return icon;
            }
        }

        public static Bitmap ImageLive => imageLive.Value;

        public static Bitmap ImageMuted => imageMuted.Value;

        public static Bitmap ImageNoDevice => imageNoDevice.Value;

        private static Stream Open(string fileName)
        {
            string name = "Muter.Resources." + fileName;
            return typeof(AppResources).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded resource " + name);
        }

        private static Icon LoadIcon(string fileName, Size size)
        {
            using (Stream stream = Open(fileName))
                return new Icon(stream, size);
        }

        private static Bitmap LoadBitmap(string fileName)
        {
            // GDI+ needs the source stream for the lifetime of a Bitmap, so return an independent copy.
            using (Stream stream = Open(fileName))
            using (var image = new Bitmap(stream))
                return new Bitmap(image);
        }
    }
}
