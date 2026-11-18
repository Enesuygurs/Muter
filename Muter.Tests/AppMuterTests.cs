using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // Muting the app in front with its own hotkey.
    public sealed class AppMuterTests : IDisposable
    {
        private const int Own = 999;
        private const string StartupKey = @"Software\MuterTests\AppMuteRun";

        public AppMuterTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        // Audio sessions that remember their mute state.
        private sealed class FakeSessions : IAudioSessionSource
        {
            public readonly Dictionary<int, bool> Muted = new Dictionary<int, bool>(); // one session per process (by id)
            public readonly HashSet<int> FailWrites = new HashSet<int>();
            public readonly List<(int, bool)> Writes = new List<(int, bool)>();

            public IReadOnlyList<AppAudioSession> GetSessions() => Muted.Select(p => new AppAudioSession(p.Key, p.Value)).ToList();

            public void SetMute(AppAudioSession session, bool mute)
            {
                Writes.Add((session.ProcessId, mute));
                if (FailWrites.Contains(session.ProcessId))
                    throw new InvalidOperationException("device gone");
                Muted[session.ProcessId] = mute;
            }
        }

        // chrome (10) with helpers 11 and 12 (12 started by 11), an unrelated chrome window (13), spotify (20), and the
        // game launcher 30 that started the game 31 (another exe name).
        private static readonly IReadOnlyDictionary<int, ProcessEntry> Processes = new Dictionary<int, ProcessEntry>
        {
            [1] = new ProcessEntry(0, "explorer.exe"),
            [10] = new ProcessEntry(1, "chrome.exe"),
            [11] = new ProcessEntry(10, "chrome.exe"),
            [12] = new ProcessEntry(11, "chrome.exe"),
            [13] = new ProcessEntry(1, "CHROME.EXE"),
            [20] = new ProcessEntry(1, "Spotify.exe"),
            [30] = new ProcessEntry(1, "launcher.exe"),
            [31] = new ProcessEntry(30, "game.exe"),
            [Own] = new ProcessEntry(1, "Muter.exe"),
        };

        private static AppMuter Muter(FakeSessions sessions) => new AppMuter(() => sessions, () => Processes, Own);

        [Fact]
        public void The_app_is_the_process_in_front_its_namesakes_and_everything_it_started()
        {
            Assert.Equal(new[] { 10, 11, 12, 13 }, AppMuter.AppProcesses(10, Processes).OrderBy(p => p));
            Assert.Equal(new[] { 30, 31 }, AppMuter.AppProcesses(30, Processes).OrderBy(p => p));
            Assert.Equal(new[] { 20 }, AppMuter.AppProcesses(20, Processes).OrderBy(p => p));
            Assert.Equal(new[] { 77 }, AppMuter.AppProcesses(77, Processes)); // exited meanwhile: only itself
        }

        [Fact]
        public void A_loop_in_the_parent_chain_does_not_hang()
        {
            var looped = new Dictionary<int, ProcessEntry> { [5] = new ProcessEntry(6, "a.exe"), [6] = new ProcessEntry(5, "b.exe"), [7] = new ProcessEntry(7, "c.exe") };
            Assert.Equal(new[] { 5, 6 }, AppMuter.AppProcesses(5, looped).OrderBy(p => p));
        }

        [Fact]
        public void Toggling_mutes_every_session_of_the_app_and_nothing_else()
        {
            var sessions = new FakeSessions { Muted = { [11] = false, [12] = false, [20] = false, [31] = false } };
            AppMuter muter = Muter(sessions);

            Assert.Equal(AppMuteResult.Muted, muter.Toggle(10));
            Assert.True(sessions.Muted[11]);
            Assert.True(sessions.Muted[12]);
            Assert.False(sessions.Muted[20]);
            Assert.False(sessions.Muted[31]);

            Assert.Equal(AppMuteResult.Unmuted, muter.Toggle(10));
            Assert.False(sessions.Muted[11]);
            Assert.False(sessions.Muted[12]);
        }

        [Fact]
        public void A_partly_muted_app_is_muted_completely()
        {
            var sessions = new FakeSessions { Muted = { [11] = true, [12] = false } };
            Assert.Equal(AppMuteResult.Muted, Muter(sessions).Toggle(10));
            Assert.True(sessions.Muted[11] && sessions.Muted[12]);
        }

        [Fact]
        public void A_game_started_by_a_launcher_in_front_is_muted_too()
        {
            var sessions = new FakeSessions { Muted = { [31] = false } };
            Assert.Equal(AppMuteResult.Muted, Muter(sessions).Toggle(30));
            Assert.True(sessions.Muted[31]);
        }

        [Fact]
        public void Nothing_in_front_Muter_itself_or_an_app_without_sound_changes_nothing()
        {
            var sessions = new FakeSessions { Muted = { [Own] = false, [20] = false } };
            AppMuter muter = Muter(sessions);
            Assert.Equal(AppMuteResult.NoApp, muter.Toggle(0));
            Assert.Equal(AppMuteResult.NoApp, muter.Toggle(Own));
            Assert.Equal(AppMuteResult.NoSound, muter.Toggle(10));
            Assert.Empty(sessions.Writes);
        }

        [Fact]
        public void Muter_own_sound_is_never_muted_even_when_it_looks_like_part_of_the_app()
        {
            var processes = new Dictionary<int, ProcessEntry> { [10] = new ProcessEntry(1, "x.exe"), [Own] = new ProcessEntry(10, "Muter.exe") };
            var sessions = new FakeSessions { Muted = { [10] = false, [Own] = false } };
            Assert.Equal(AppMuteResult.Muted, new AppMuter(() => sessions, () => processes, Own).Toggle(10));
            Assert.False(sessions.Muted[Own]);
        }

        [Fact]
        public void A_session_that_cannot_be_changed_does_not_stop_the_others()
        {
            var sessions = new FakeSessions { Muted = { [11] = false, [12] = false }, FailWrites = { 11 } };
            Assert.Equal(AppMuteResult.Muted, Muter(sessions).Toggle(10));
            Assert.True(sessions.Muted[12]);
        }

        // The application: the setting, the second hotkey and the toggle.
        private sealed class Harness
        {
            public TrayApplicationContext App;
            public FakeGlobalHotkey MicHotkey;
            public FakeGlobalHotkey AppHotkey;
            public int AppHotkeysMade;
            public FakeSessions Sessions;
            public int Foreground = 10;
        }

        private static void WithApp(Action<Settings> configure, Action<Harness> test)
        {
            TestSetup.RunSta(() =>
            {
                var provider = new FakeAudioEndpointProvider();
                provider.Add("a");
                provider.SetDefault("a");
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                configure?.Invoke(settings);
                var h = new Harness { MicHotkey = new FakeGlobalHotkey(), Sessions = new FakeSessions { Muted = { [11] = false, [20] = false } } };
                using (h.App = new TrayApplicationContext(settings, () => provider, h.MicHotkey, new StartupRegistration(Application.ExecutablePath, StartupKey),
                    TestSetup.Silent, null,
                    () => { h.AppHotkeysMade++; return h.AppHotkey = new FakeGlobalHotkey(); },
                    new AppMuter(() => h.Sessions, () => Processes, Own),
                    () => h.Foreground))
                {
                    Assert.True(h.App.WaitForAudio());
                    test(h);
                }
            });
        }

        [Fact]
        public void Off_by_default_no_second_hotkey_is_registered()
        {
            WithApp(null, h =>
            {
                Assert.False(h.App.Settings.appMuteEnabled);
                Assert.Equal(0, h.AppHotkeysMade);
                Assert.False(h.App.AppHotkeyRegistered);
                Assert.Equal(Hotkey.AppDefault, h.App.ConfiguredAppHotkey);
            });
        }

        [Fact]
        public void Turned_on_the_hotkey_mutes_and_unmutes_the_app_in_front()
        {
            WithApp(null, h =>
            {
                h.App.SetAppMuteEnabled(true);
                Assert.True(h.App.AppHotkeyRegistered);
                Assert.Equal(Hotkey.AppDefault, h.AppHotkey.Current);

                h.AppHotkey.Press();
                Assert.Equal(AppMuteResult.Muted, h.App.LastAppMute.Result);
                Assert.True(h.Sessions.Muted[11]);
                Assert.False(h.Sessions.Muted[20]);

                h.Foreground = 20; // another app in front now
                h.AppHotkey.Press();
                Assert.Equal(AppMuteResult.Muted, h.App.LastAppMute.Result);
                Assert.True(h.Sessions.Muted[20]);

                h.Foreground = 10;
                h.AppHotkey.Press();
                Assert.Equal(AppMuteResult.Unmuted, h.App.LastAppMute.Result);
                Assert.False(h.Sessions.Muted[11]);

                // The microphone is not touched by any of it.
                Assert.Equal(MicState.Live, h.App.Status.State);

                h.App.SetAppMuteEnabled(false);
                Assert.False(h.App.AppHotkeyRegistered);
                Assert.False(h.App.Settings.appMuteEnabled);
            });
        }

        [Fact]
        public void The_microphone_hotkey_keeps_working_and_is_separate()
        {
            WithApp(s => s.appMuteEnabled = true, h =>
            {
                Assert.True(h.App.AppHotkeyRegistered);
                h.MicHotkey.Press();
                Assert.True(h.App.WaitForAudio());
                Assert.Equal(MicState.Muted, h.App.Status.State);
                Assert.Null(h.App.LastAppMute);
                Assert.False(h.Sessions.Muted[11]);
            });
        }

        [Fact]
        public void The_two_hotkeys_cannot_be_the_same()
        {
            WithApp(s => s.appMuteEnabled = true, h =>
            {
                string error = h.App.TryApplyAppHotkey(h.App.ConfiguredHotkey);
                Assert.Contains("F9", error);
                Assert.Equal(Hotkey.AppDefault, h.App.ConfiguredAppHotkey);

                var apply = typeof(TrayApplicationContext).GetMethod("TryApplyHotkey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                error = (string)apply.Invoke(h.App, new object[] { Hotkey.AppDefault });
                Assert.NotNull(error);
                Assert.Equal((int)Keys.F9, h.App.Settings.hotkeyKey);
            });
        }

        [Fact]
        public void A_saved_app_hotkey_equal_to_the_microphone_one_is_not_registered()
        {
            WithApp(s =>
            {
                s.appMuteEnabled = true;
                s.appMuteHotkeyKey = (int)Keys.F9;
                s.appMuteHotkeyModifiers = 0;
            }, h =>
            {
                Assert.False(h.App.AppHotkeyRegistered);
                Assert.True(h.MicHotkey.IsRegistered);
            });
        }

        [Fact]
        public void A_new_app_hotkey_is_registered_and_saved()
        {
            WithApp(s => s.appMuteEnabled = true, h =>
            {
                var hotkey = new Hotkey(Keys.F11, HotkeyModifiers.Control | HotkeyModifiers.Alt);
                Assert.Null(h.App.TryApplyAppHotkey(hotkey));
                Assert.Equal(hotkey, h.AppHotkey.Current);
                Assert.Equal((int)Keys.F11, h.App.Settings.appMuteHotkeyKey);
                Assert.Equal(hotkey, new Hotkey((Keys)new Settings().appMuteHotkeyKey, (HotkeyModifiers)new Settings().appMuteHotkeyModifiers));

                h.AppHotkey.Taken = new Hotkey(Keys.F12, HotkeyModifiers.Control);
                Assert.Contains("already used", h.App.TryApplyAppHotkey(new Hotkey(Keys.F12, HotkeyModifiers.Control)));
                Assert.Equal(hotkey, h.App.ConfiguredAppHotkey);
            });
        }

        [Fact]
        public void The_settings_window_turns_it_on_and_shows_the_hotkey()
        {
            WithApp(null, h =>
            {
                SettingsForm form = h.App.OpenSettings();
                Application.DoEvents();
                Assert.False(form.AppMuteToggle.Checked);
                Assert.Equal("Ctrl + Shift + F10", string.Join(" + ", form.AppHotkeyKeys.Keycaps));

                form.AppMuteToggle.Toggle();
                Assert.True(h.App.Settings.appMuteEnabled);
                Assert.True(h.App.AppHotkeyRegistered);
                form.Close();
            });
        }
    }
}
