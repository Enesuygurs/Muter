using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Muter.Hotkeys;
using Muter.Properties;
using Xunit;

namespace Muter.Tests
{
    // The settings live in one fixed file, whatever folder the exe runs from and whatever its version, and nothing an
    // earlier version saved is lost on the way there. (TestSetup points both folders to a temporary place.)
    public sealed class SettingsFileProviderTests : IDisposable
    {
        public SettingsFileProviderTests()
        {
            TestSetup.Init();
            Clean();
        }

        public void Dispose() => Clean();

        private static string SettingsPath => SettingsFileProvider.FilePath;

        private static void Clean()
        {
            foreach (string directory in new[] { SettingsFileProvider.Directory, SettingsFileProvider.LegacyDirectory })
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        // A user.config like the ones versions up to 1.3 saved, in <legacy>\<exeFolder>\<version>\user.config.
        private static string WriteLegacy(string exeFolder, string version, params (string Name, string Value)[] settings)
        {
            string folder = Path.Combine(SettingsFileProvider.LegacyDirectory, exeFolder, version);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "user.config");
            string body = string.Concat(settings.Select(s =>
                $"            <setting name=\"{s.Name}\" serializeAs=\"String\">\r\n                <value>{System.Security.SecurityElement.Escape(s.Value)}</value>\r\n            </setting>\r\n"));
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n    <userSettings>\r\n        <Muter.Properties.Settings>\r\n"
                + body + "        </Muter.Properties.Settings>\r\n    </userSettings>\r\n</configuration>\r\n");
            return path;
        }

        [Fact]
        public void Values_are_kept_in_the_fixed_file_and_read_back_by_a_new_instance()
        {
            var settings = new Settings();
            settings.hotkeyKey = (int)Keys.F9;
            settings.selectedDeviceId = "ALL_DEVICES";
            settings.showGui = false;
            settings.language = "tr";
            settings.overlayCustomX = -1234;
            settings.selectedDeviceName = "Mikrofon <USB> & \"Ses\"";
            settings.Save();

            Assert.True(File.Exists(SettingsPath));
            var again = new Settings();
            Assert.Equal((int)Keys.F9, again.hotkeyKey);
            Assert.Equal("ALL_DEVICES", again.selectedDeviceId);
            Assert.False(again.showGui);
            Assert.Equal("tr", again.language);
            Assert.Equal(-1234, again.overlayCustomX);
            Assert.Equal("Mikrofon <USB> & \"Ses\"", again.selectedDeviceName);
        }

        [Fact]
        public void The_file_does_not_depend_on_the_exe_folder_or_version()
        {
            Assert.Equal(Path.Combine(SettingsFileProvider.Directory, "settings.xml"), SettingsPath);
            Assert.DoesNotContain(Application.ExecutablePath, SettingsPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(typeof(Settings).Assembly.GetName().Version.ToString(), SettingsPath);
        }

        [Fact]
        public void Without_any_file_the_defaults_apply()
        {
            var settings = new Settings();
            Assert.True(settings.showGui);
            Assert.True(settings.syncDevices);
            Assert.True(settings.settingsUpgradeRequired);
            Assert.Equal(0, settings.hotkeyKey);
            Assert.False(File.Exists(SettingsPath));
        }

        [Fact]
        public void Values_left_at_their_default_are_not_stored()
        {
            var settings = new Settings();
            settings.hotkeyKey = (int)Keys.F9;
            settings.Save();

            var document = new XmlDocument();
            document.Load(SettingsPath);
            Assert.Equal(new[] { "hotkeyKey" }, document.DocumentElement.ChildNodes.Cast<XmlElement>().Select(e => e.GetAttribute("name")).ToArray());

            // Set back to the default explicitly: stored, so it stays even if the default changes one day.
            settings.showGui = true;
            settings.Save();
            Assert.True(new Settings().showGui);
            document.Load(SettingsPath);
            Assert.Contains("showGui", document.DocumentElement.ChildNodes.Cast<XmlElement>().Select(e => e.GetAttribute("name")));
        }

        [Fact]
        public void Entries_of_a_newer_version_are_kept()
        {
            Directory.CreateDirectory(SettingsFileProvider.Directory);
            File.WriteAllText(SettingsPath, "<settings><setting name=\"fromTheFuture\">42</setting><setting name=\"hotkeyKey\">120</setting></settings>");

            var settings = new Settings();
            Assert.Equal(120, settings.hotkeyKey);
            settings.showGui = false;
            settings.Save();

            Assert.Contains("fromTheFuture", File.ReadAllText(SettingsPath));
            Assert.False(new Settings().showGui);
        }

        [Fact]
        public void Reset_goes_back_to_the_defaults_and_does_not_take_over_old_settings_again()
        {
            WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("hotkeyKey", "120"));
            var settings = new Settings();
            Assert.Equal(120, settings.hotkeyKey);

            settings.Reset();
            Assert.Equal(0, settings.hotkeyKey);
            Assert.Equal(0, new Settings().hotkeyKey);
        }

        [Fact]
        public void Saving_replaces_the_file_in_one_step_and_keeps_a_backup()
        {
            var settings = new Settings { hotkeyKey = (int)Keys.F9 };
            settings.Save();
            settings.hotkeyKey = (int)Keys.F10;
            settings.Save();

            Assert.False(File.Exists(SettingsPath + ".tmp"));
            Assert.True(File.Exists(SettingsPath + ".bak"));
            Assert.Contains(">120<", File.ReadAllText(SettingsPath + ".bak"));
            Assert.Contains(">121<", File.ReadAllText(SettingsPath));
        }

        [Fact]
        public void A_damaged_file_falls_back_to_the_backup()
        {
            var settings = new Settings { hotkeyKey = (int)Keys.F9 };
            settings.Save();
            settings.hotkeyKey = (int)Keys.F10;
            settings.Save(); // backup holds F9
            File.WriteAllText(SettingsPath, "<settings><setting name=\"hotk"); // cut off

            Assert.Equal((int)Keys.F9, new Settings().hotkeyKey);
            Assert.True(File.Exists(SettingsPath + ".damaged"));
            Assert.Equal((int)Keys.F9, new Settings().hotkeyKey); // and repaired on disk
        }

        [Fact]
        public void A_damaged_file_without_a_backup_starts_with_defaults_and_is_kept_aside()
        {
            Directory.CreateDirectory(SettingsFileProvider.Directory);
            File.WriteAllText(SettingsPath, "not xml at all");
            WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("hotkeyKey", "120")); // must not be taken over over a damaged file

            var settings = new Settings();
            Assert.Equal(0, settings.hotkeyKey);
            Assert.True(File.Exists(SettingsPath + ".damaged"));
            settings.hotkeyKey = (int)Keys.F11;
            settings.Save();
            Assert.Equal((int)Keys.F11, new Settings().hotkeyKey);
        }

        [Fact]
        public void The_first_start_takes_over_every_value_an_earlier_version_saved()
        {
            string legacy = WriteLegacy("Muter.exe_Url_other", "1.3.0.0",
                ("shortcut", "F9"), ("selectedDeviceId", "ALL_DEVICES"), ("selectedDeviceName", "All Devices"), ("showGui", "False"),
                ("syncDevices", "True"), ("settingsUpgradeRequired", "False"), ("hotkeyKey", "120"), ("hotkeyModifiers", "0"),
                ("overlayPosition", "Custom"), ("overlayCustomX", "-300"), ("miniIndicatorEnabled", "True"), ("soundFeedback", "False"),
                ("pushToTalk", "True"), ("language", "de"));
            string before = File.ReadAllText(legacy);

            var settings = new Settings();
            Assert.Equal(120, settings.hotkeyKey);
            Assert.Equal("ALL_DEVICES", settings.selectedDeviceId);
            Assert.Equal("All Devices", settings.selectedDeviceName);
            Assert.False(settings.showGui);
            Assert.False(settings.settingsUpgradeRequired);
            Assert.Equal("Custom", settings.overlayPosition);
            Assert.Equal(-300, settings.overlayCustomX);
            Assert.True(settings.miniIndicatorEnabled);
            Assert.False(settings.soundFeedback);
            Assert.True(settings.pushToTalk);
            Assert.Equal("de", settings.language);

            Assert.True(File.Exists(SettingsPath), "taken over right away");
            Assert.Equal(before, File.ReadAllText(legacy)); // the old file is left as it was
            Assert.Equal(120, new Settings().hotkeyKey);
        }

        [Fact]
        public void Earlier_settings_are_taken_over_only_once()
        {
            WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("hotkeyKey", "120"));
            var settings = new Settings();
            settings.hotkeyKey = (int)Keys.F11;
            settings.Save();

            WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("hotkeyKey", "123"));
            Assert.Equal((int)Keys.F11, new Settings().hotkeyKey);
        }

        [Fact]
        public void The_highest_version_of_this_exe_folder_wins()
        {
            string own = SettingsFileProvider.OwnLegacyFolderName();
            Assert.False(string.IsNullOrEmpty(own));
            WriteLegacy(own, "1.1.0.0", ("hotkeyKey", "112"));
            WriteLegacy(own, "1.3.0.0", ("hotkeyKey", "113"));
            WriteLegacy(own, "1.2.0.0", ("hotkeyKey", "114"));
            string otherNewer = WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("hotkeyKey", "115"));
            File.SetLastWriteTimeUtc(otherNewer, DateTime.UtcNow.AddDays(1));

            Assert.Equal(113, new Settings().hotkeyKey);
        }

        [Fact]
        public void Without_a_folder_of_this_exe_the_most_recent_one_wins()
        {
            string older = WriteLegacy("Muter.exe_Url_aaa", "1.3.0.0", ("hotkeyKey", "112"));
            string newer = WriteLegacy("Muter.exe_Url_bbb", "1.2.0.0", ("hotkeyKey", "113"));
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));

            Assert.Equal(113, new Settings().hotkeyKey);
        }

        [Fact]
        public void Unreadable_earlier_settings_are_skipped()
        {
            string folder = Path.Combine(SettingsFileProvider.LegacyDirectory, "Muter.exe_Url_bad", "1.3.0.0");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "user.config"), "<configuration><userSett");
            Directory.CreateDirectory(Path.Combine(SettingsFileProvider.LegacyDirectory, "not a version folder"));

            var settings = new Settings();
            Assert.Equal(0, settings.hotkeyKey);
            Assert.True(settings.showGui);
        }

        // Version 1.0 saved only the text hotkey; it is converted, so its users keep their hotkey.
        [Theory]
        [InlineData("F9", Keys.F9, HotkeyModifiers.None)]
        [InlineData("SHIFT + F", Keys.F, HotkeyModifiers.Shift)]
        [InlineData("CONTROL + NUMPAD1", Keys.NumPad1, HotkeyModifiers.Control)]
        public void Settings_of_version_1_0_keep_their_hotkey(string shortcut, Keys key, HotkeyModifiers modifiers)
        {
            WriteLegacy("Muter.exe_Url_old", "1.0.0.0", ("shortcut", shortcut));
            var settings = new Settings();

            Program.MigrateSettings(settings);

            Assert.Equal(new Hotkey(key, modifiers), new Hotkey((Keys)settings.hotkeyKey, (HotkeyModifiers)settings.hotkeyModifiers));
            Assert.False(settings.settingsUpgradeRequired);
            var again = new Settings();
            Assert.Equal((int)key, again.hotkeyKey);
            Assert.False(again.settingsUpgradeRequired);
        }

        [Fact]
        public void A_new_install_gets_the_default_hotkey()
        {
            var settings = new Settings();
            Program.MigrateSettings(settings);
            Assert.Equal(Hotkey.Default, new Hotkey((Keys)settings.hotkeyKey, (HotkeyModifiers)settings.hotkeyModifiers));
            Assert.True(File.Exists(SettingsPath));
        }

        [Fact]
        public void Settings_of_version_1_3_are_used_as_they_are()
        {
            WriteLegacy("Muter.exe_Url_other", "1.3.0.0", ("shortcut", "F9"), ("settingsUpgradeRequired", "False"), ("hotkeyKey", "120"), ("hotkeyModifiers", "0"), ("showGui", "False"));
            var settings = new Settings();

            Program.MigrateSettings(settings);

            Assert.Equal((int)Keys.F9, settings.hotkeyKey);
            Assert.Equal(0, settings.hotkeyModifiers);
            Assert.False(settings.showGui);
        }

        [Fact]
        public void Every_setting_is_stored_as_text()
        {
            foreach (SettingsProperty property in new Settings().Properties)
                Assert.Equal(SettingsSerializeAs.String, property.SerializeAs);
        }
    }
}
