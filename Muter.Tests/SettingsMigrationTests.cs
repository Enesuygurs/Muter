using System.Windows.Forms;
using Muter.Hotkeys;
using Muter.Properties;
using Xunit;

namespace Muter.Tests
{
    // Runs against the test host's own user.config, never Muter's.
    public class SettingsMigrationTests
    {
        public SettingsMigrationTests() => TestSetup.Init();

        private static Settings Fresh()
        {
            var settings = new Settings();
            settings.Reset();
            return settings;
        }

        [Theory]
        [InlineData("CONTROL + NUMPAD1", Keys.NumPad1, HotkeyModifiers.Control)]
        [InlineData("F9", Keys.F9, HotkeyModifiers.None)]
        [InlineData("SHIFT + F", Keys.F, HotkeyModifiers.Shift)]
        [InlineData("garbage", Keys.F, HotkeyModifiers.Shift)]
        public void Old_text_hotkey_is_converted(string text, Keys key, HotkeyModifiers modifiers)
        {
            // As in the real app the old value comes from disk (Upgrade() reloads, discarding unsaved changes).
            Settings settings = Fresh();
            settings.shortcut = text;
            settings.Save();

            Program.MigrateSettings(settings);

            Assert.Equal((int)key, settings.hotkeyKey);
            Assert.Equal((int)modifiers, settings.hotkeyModifiers);
            Assert.False(settings.settingsUpgradeRequired);

            // Persisted: a second run does not migrate again.
            settings.Reload();
            Assert.Equal((int)key, settings.hotkeyKey);
            Assert.False(settings.settingsUpgradeRequired);
        }

        [Fact]
        public void Already_migrated_hotkey_is_kept()
        {
            Settings settings = Fresh();
            settings.shortcut = "SHIFT + F";
            settings.hotkeyKey = (int)Keys.F12;
            settings.hotkeyModifiers = (int)HotkeyModifiers.Alt;
            settings.Save();

            Program.MigrateSettings(settings);

            Assert.Equal((int)Keys.F12, settings.hotkeyKey);
            Assert.Equal((int)HotkeyModifiers.Alt, settings.hotkeyModifiers);
        }

        [Fact]
        public void Defaults_match_the_documented_behaviour()
        {
            Settings settings = Fresh();
            Assert.True(settings.showGui);
            Assert.True(settings.syncDevices);
            Assert.Equal(string.Empty, settings.selectedDeviceId);
            Assert.Equal("LowerCenter", settings.overlayPosition);
            Assert.Equal(1000, settings.overlayDurationMs);
            Assert.False(settings.miniIndicatorEnabled);
        }
    }
}
