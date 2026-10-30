using System;
using System.IO;
using Microsoft.Win32;

namespace Muter
{
    // Manages the HKCU "Run" entry that starts Muter with Windows.
    internal sealed class StartupRegistration
    {
        public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly string runKeyPath;
        private readonly string valueName;
        private readonly string executablePath;

        public StartupRegistration(string executablePath, string runKeyPath = DefaultRunKeyPath, string valueName = "Muter")
        {
            this.executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            this.runKeyPath = runKeyPath;
            this.valueName = valueName;
        }

        public string ExpectedValue => Quote(executablePath);

        public bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
                return key?.GetValue(valueName) is string value && value.Length > 0;
        }

        public void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath))
            {
                if (enabled)
                    key.SetValue(valueName, ExpectedValue, RegistryValueKind.String);
                else if (key.GetValue(valueName) != null)
                    key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        // If startup is enabled but the entry points to a file that no longer exists (the app was moved), or the path
        // is unquoted and contains spaces, rewrite it for the current executable. Returns true if it changed anything.
        public bool RepairIfNeeded()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true))
            {
                if (!(key?.GetValue(valueName) is string value) || value.Length == 0)
                    return false;

                string registeredPath = ExtractPath(value);
                bool missing = !File.Exists(registeredPath);
                bool unquotedWithSpaces = !value.TrimStart().StartsWith("\"", StringComparison.Ordinal) && registeredPath.IndexOf(' ') >= 0;
                if (!missing && !unquotedWithSpaces)
                    return false;

                string newValue = missing ? ExpectedValue : Quote(registeredPath);
                if (string.Equals(value, newValue, StringComparison.Ordinal))
                    return false;
                key.SetValue(valueName, newValue, RegistryValueKind.String);
                Log.Info($"Startup entry updated from '{value}' to '{newValue}'");
                return true;
            }
        }

        public static string Quote(string path) => "\"" + path + "\"";

        // Path part of a Run value: either a quoted path (optionally followed by arguments) or a bare path.
        public static string ExtractPath(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = trimmed.IndexOf('"', 1);
                return end > 0 ? trimmed.Substring(1, end - 1) : trimmed.Trim('"');
            }
            return trimmed;
        }
    }
}
