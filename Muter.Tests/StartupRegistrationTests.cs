using System;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace Muter.Tests
{
    // Uses a throwaway key under HKCU\Software\MuterTests instead of the real Run key.
    public sealed class StartupRegistrationTests : IDisposable
    {
        private readonly string keyPath = @"Software\MuterTests\Run-" + Guid.NewGuid().ToString("N");
        private readonly string exePath;
        private readonly string tempDir;

        public StartupRegistrationTests()
        {
            TestSetup.Init();
            tempDir = Path.Combine(Path.GetTempPath(), "Muter Startup " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            exePath = Path.Combine(tempDir, "Muter.exe");
            File.WriteAllText(exePath, string.Empty);
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);
            Directory.Delete(tempDir, recursive: true);
        }

        private string Read()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                return key?.GetValue("Muter") as string;
        }

        private void Write(string value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                key.SetValue("Muter", value);
        }

        [Fact]
        public void Enable_and_disable_work_when_the_key_does_not_exist()
        {
            var startup = new StartupRegistration(exePath, keyPath);
            Assert.False(startup.IsEnabled());

            startup.SetEnabled(true);
            Assert.True(startup.IsEnabled());
            Assert.Equal("\"" + exePath + "\"", Read());

            startup.SetEnabled(false);
            Assert.False(startup.IsEnabled());
            Assert.Null(Read());

            startup.SetEnabled(false); // disabling twice is harmless
        }

        [Fact]
        public void Repair_quotes_an_unquoted_path_with_spaces()
        {
            Write(exePath);
            Assert.True(new StartupRegistration(exePath, keyPath).RepairIfNeeded());
            Assert.Equal("\"" + exePath + "\"", Read());
            Assert.False(new StartupRegistration(exePath, keyPath).RepairIfNeeded());
        }

        [Fact]
        public void Repair_points_a_missing_executable_to_the_current_one()
        {
            Write("\"C:\\Gone\\Muter.exe\"");
            Assert.True(new StartupRegistration(exePath, keyPath).RepairIfNeeded());
            Assert.Equal("\"" + exePath + "\"", Read());
        }

        [Fact]
        public void Repair_leaves_a_valid_entry_for_another_copy_alone()
        {
            string other = Path.Combine(tempDir, "Other.exe");
            File.WriteAllText(other, string.Empty);
            Write("\"" + other + "\"");
            Assert.False(new StartupRegistration(exePath, keyPath).RepairIfNeeded());
            Assert.Equal("\"" + other + "\"", Read());
        }

        [Fact]
        public void Repair_does_nothing_when_disabled()
        {
            Assert.False(new StartupRegistration(exePath, keyPath).RepairIfNeeded());
            Assert.Null(Read());
        }

        [Theory]
        [InlineData("\"C:\\A B\\Muter.exe\"", "C:\\A B\\Muter.exe")]
        [InlineData("\"C:\\A B\\Muter.exe\" --minimized", "C:\\A B\\Muter.exe")]
        [InlineData("C:\\AB\\Muter.exe", "C:\\AB\\Muter.exe")]
        [InlineData("  \"C:\\x.exe\"  ", "C:\\x.exe")]
        public void Extracts_the_executable_path(string value, string expected)
        {
            Assert.Equal(expected, StartupRegistration.ExtractPath(value));
        }
    }
}
