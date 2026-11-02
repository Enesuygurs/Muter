using System;
using Microsoft.Win32;

namespace Muter.UI
{
    internal static class TaskbarTheme
    {
        // Windows 10/11 draw the taskbar light when "Choose your default Windows mode" is Light (SystemUsesLightTheme = 1).
        // Missing on older Windows, where the taskbar is dark.
        public static bool IsLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the taskbar theme", ex);
                return false;
            }
        }
    }
}
