using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.UI;

namespace Muter
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\Muter.SingleInstance.{A96241F9-3D3E-484A-B3B5-1E57202B1B6D}";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("Muter is already running. Look for its icon in the system tray.", "Muter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, args) => Log.Error("Unhandled exception on the UI thread", args.Exception);
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                    Log.Error($"Unhandled exception (terminating: {args.IsTerminating})", args.ExceptionObject as Exception);

                Log.Info($"Muter {Application.ProductVersion} starting ({(Environment.Is64BitProcess ? "64" : "32")}-bit, Windows {Environment.OSVersion.Version})");
                MigrateSettings(Settings.Default);

                // The audio provider is created on the audio thread; if Windows audio is unavailable the app shows the
                // state as unknown and keeps retrying.
                using (var context = new TrayApplicationContext(Settings.Default, () => new CoreAudioEndpointProvider(), new GlobalHotkey(), new StartupRegistration(Application.ExecutablePath)))
                    Application.Run(context);

                Log.Info("Muter stopped");
                GC.KeepAlive(mutex);
            }
        }

        // Settings that versions before 1.1 could have saved; any of them means Muter was used before.
        private static readonly string[] LegacySettingNames = { "shortcut", "selectedDeviceId", "selectedDeviceName", "showGui", "syncDevices" };

        // Carries settings over from the previous version and converts the old text hotkey ("SHIFT + F") to key codes.
        // Someone upgrading keeps their hotkey (including the old Shift + F default); a new install gets Hotkey.Default.
        // previousVersion is Settings.GetPreviousVersion; tests replace it.
        internal static void MigrateSettings(Settings settings, Func<string, object> previousVersion = null)
        {
            try
            {
                previousVersion = previousVersion ?? settings.GetPreviousVersion;
                bool usedBefore = false;
                if (settings.settingsUpgradeRequired)
                {
                    usedBefore = LegacySettingNames.Any(name => previousVersion(name) != null);
                    settings.Upgrade();
                    settings.settingsUpgradeRequired = false;
                    Log.Info(usedBefore ? "Settings carried over from the previous version" : "First start");
                }

                if (!Hotkey.TryFromSettings(settings.hotkeyKey, settings.hotkeyModifiers, out _))
                {
                    // The text setting defaults to "SHIFT + F", which is only meaningful for someone who used an older version.
                    Hotkey hotkey = usedBefore && Hotkey.TryParse(settings.shortcut, out Hotkey parsed) ? parsed : Hotkey.Default;
                    settings.hotkeyKey = (int)hotkey.Key;
                    settings.hotkeyModifiers = (int)hotkey.Modifiers;
                    Log.Info(usedBefore ? $"Hotkey '{settings.shortcut}' migrated to {hotkey}" : $"Hotkey set to the default {hotkey}");
                }

                settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Migrating the settings failed", ex);
            }
        }
    }
}
