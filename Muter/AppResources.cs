using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Muter
{
    // Icons and images embedded from the Resources folder (see Muter.csproj). Loaded once and shared; never dispose them.
    internal static class AppResources
    {
        private static readonly Lazy<Icon> appIcon = new Lazy<Icon>(() => LoadIcon("muter.ico", SystemInformation.IconSize));
        private static readonly Lazy<Icon> trayLive = new Lazy<Icon>(() => LoadIcon("muteron.ico", SystemInformation.SmallIconSize));
        private static readonly Lazy<Icon> trayMuted = new Lazy<Icon>(() => LoadIcon("muteroff.ico", SystemInformation.SmallIconSize));
        private static readonly Lazy<Icon> trayNoDevice = new Lazy<Icon>(() => LoadIcon("nodevice.ico", SystemInformation.SmallIconSize));
        private static readonly Lazy<Bitmap> imageLive = new Lazy<Bitmap>(() => LoadBitmap("muteron.png"));
        private static readonly Lazy<Bitmap> imageMuted = new Lazy<Bitmap>(() => LoadBitmap("muteroff.png"));
        private static readonly Lazy<Bitmap> imageNoDevice = new Lazy<Bitmap>(() => LoadBitmap("nodevice.png"));

        public static Icon AppIcon => appIcon.Value;

        public static Icon TrayLive => trayLive.Value;

        public static Icon TrayMuted => trayMuted.Value;

        public static Icon TrayNoDevice => trayNoDevice.Value;

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
