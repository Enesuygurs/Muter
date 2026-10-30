using System;
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

                CoreAudioEndpointProvider provider;
                try
                {
                    provider = new CoreAudioEndpointProvider();
                }
                catch (Exception ex)
                {
                    Log.Error("The Windows audio system could not be opened", ex);
                    MessageBox.Show("Muter could not access the Windows audio system:\n" + ex.Message, "Muter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using (var context = new TrayApplicationContext(Settings.Default, provider, new GlobalHotkey(), new StartupRegistration(Application.ExecutablePath)))
                    Application.Run(context);

                Log.Info("Muter stopped");
                GC.KeepAlive(mutex);
            }
        }

        // Carries settings over from the previous version and converts the old text hotkey ("SHIFT + F") to key codes.
        internal static void MigrateSettings(Settings settings)
        {
            try
            {
                if (settings.settingsUpgradeRequired)
                {
                    settings.Upgrade();
                    settings.settingsUpgradeRequired = false;
                    Log.Info("Settings carried over from the previous version");
                }

                if (!Hotkey.TryFromSettings(settings.hotkeyKey, settings.hotkeyModifiers, out _))
                {
                    Hotkey hotkey = Hotkey.TryParse(settings.shortcut, out Hotkey parsed) ? parsed : Hotkey.Default;
                    settings.hotkeyKey = (int)hotkey.Key;
                    settings.hotkeyModifiers = (int)hotkey.Modifiers;
                    Log.Info($"Hotkey '{settings.shortcut}' migrated to {hotkey}");
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
