using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    // Push to talk: the hotkey unmutes while it is held and the microphones are muted again when it is let go.
    // A missed release would leave a microphone open, so every way of letting go must end muted.
    public sealed class PushToTalkTests : IDisposable
    {
        private const string StartupKey = @"Software\MuterTests\PttRun";

        public PushToTalkTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        // The keyboard as the app sees it.
        private sealed class FakeKeyboard
        {
            private volatile bool f9Down;

            public bool F9Down
            {
                get => f9Down;
                set => f9Down = value;
            }

            public bool IsKeyDown(Keys key) => key == Keys.F9 && f9Down;
        }

        private sealed class Harness
        {
            public TrayApplicationContext App;
            public FakeAudioEndpointProvider Provider;
            public FakeGlobalHotkey Hotkey;
            public FakeKeyboard Keyboard;
            public List<FeedbackSound> Sounds;

            // Like a real press: the key goes down, then Windows reports the hotkey.
            public void Press()
            {
                Keyboard.F9Down = true;
                Hotkey.Press();
                Assert.True(App.WaitForAudio());
            }

            public void Release() => Keyboard.F9Down = false;

            public bool AllMuted => Provider.Devices.Where(d => d.Active).All(d => d.Muted);

            public bool AnyLive => Provider.Devices.Where(d => d.Active).Any(d => !d.Muted);

            // Waits for the release to be noticed and the mute to be carried out (without needing the UI thread).
            public bool WaitUntilMuted(int timeoutMs = 2000)
            {
                DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < end && (App.PushToTalkHeld || !AllMuted))
                    Thread.Sleep(5);
                return !App.PushToTalkHeld && AllMuted;
            }
        }

        private static void WithApp(Action<Settings> configure, Action<Harness> test, params string[] mics)
        {
            TestSetup.RunSta(() =>
            {
                var provider = new FakeAudioEndpointProvider();
                foreach (string id in mics.Length == 0 ? new[] { "a" } : mics)
                    provider.Add(id);
                provider.SetDefault(provider.Devices[0].Id);
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                settings.selectedDeviceId = MicrophoneController.AllDevicesId;
                configure?.Invoke(settings);
                var h = new Harness { Provider = provider, Hotkey = new FakeGlobalHotkey(), Keyboard = new FakeKeyboard(), Sounds = new List<FeedbackSound>() };
                using (h.App = new TrayApplicationContext(settings, () => provider, h.Hotkey, new StartupRegistration(Application.ExecutablePath, StartupKey),
                    sound => { lock (h.Sounds) h.Sounds.Add(sound); }, h.Keyboard.IsKeyDown))
                {
                    Assert.True(h.App.WaitForAudio());
                    test(h);
                }
            });
        }

        [Fact]
        public void Starting_with_push_to_talk_on_mutes_the_microphones()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                Assert.True(h.AllMuted);
                Assert.Equal(MicState.Muted, h.App.Status.State);
                Assert.Empty(h.Sounds); // no sound for the automatic mute
            }, "a", "b");
        }

        [Fact]
        public void Turning_it_on_mutes_and_saves()
        {
            WithApp(null, h =>
            {
                Assert.True(h.AnyLive);
                h.App.SetPushToTalk(true);
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.App.Settings.pushToTalk);
                Assert.True(h.AllMuted);
                Assert.Equal(MicState.Muted, h.App.Status.State);
            }, "a", "b");
        }

        [Fact]
        public void Holding_the_hotkey_talks_and_letting_go_mutes()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                h.Press();
                Assert.False(h.Provider.Get("a").Muted);
                Assert.Equal(MicState.Live, h.App.Status.State);

                // Still held a while later: still live.
                Thread.Sleep(150);
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.App.PushToTalkHeld);
                Assert.False(h.Provider.Get("a").Muted);

                h.Release();
                Assert.True(h.WaitUntilMuted());
                Assert.True(h.App.WaitForAudio());
                Assert.Equal(MicState.Muted, h.App.Status.State);
                lock (h.Sounds)
                    Assert.Equal(new[] { FeedbackSound.Live, FeedbackSound.Muted }, h.Sounds);
            }, "a", "b");
        }

        [Fact]
        public void A_quick_tap_ends_muted()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                // Let go before the hotkey message is handled.
                h.Hotkey.Press();
                Assert.True(h.WaitUntilMuted());
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.AllMuted);
                Assert.Equal(MicState.Muted, h.App.Status.State);
            }, "a", "b");
        }

        [Fact]
        public void The_release_mutes_even_while_the_UI_thread_is_frozen()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                h.Press();
                Assert.False(h.Provider.Get("a").Muted);

                h.Release();
                // The UI thread (this one) does not process anything during this wait.
                DateTime end = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < end && !h.AllMuted)
                    Thread.Sleep(5);
                Assert.True(h.AllMuted, "muted without the UI thread");
            }, "a", "b");
        }

        [Fact]
        public void Rapid_presses_always_end_muted_and_never_show_muted_while_live()
        {
            var random = new Random(4242);
            WithApp(s => s.pushToTalk = true, h =>
            {
                for (int round = 0; round < 60; round++)
                {
                    h.Keyboard.F9Down = true;
                    h.Hotkey.Press();
                    if (random.Next(2) == 0)
                        Thread.Sleep(random.Next(0, 40));
                    h.Release();
                    if (random.Next(3) == 0)
                        Application.DoEvents();
                }
                Assert.True(h.WaitUntilMuted());
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.AllMuted);
                Assert.Equal(MicState.Muted, h.App.Status.State);
            }, "a", "b");
        }

        [Fact]
        public void An_unreadable_microphone_on_release_shows_unknown_and_warns()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                h.Press();
                h.Provider.Get("b").FailReads = -1;
                h.Release();
                DateTime end = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < end && h.App.PushToTalkHeld)
                    Thread.Sleep(5);
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.Provider.Get("a").Muted);
                Assert.Equal(MicState.Unknown, h.App.Status.State); // never "muted" while one microphone is unverified
                lock (h.Sounds)
                    Assert.Equal(FeedbackSound.Warning, h.Sounds.Last());
            }, "a", "b");
        }

        [Fact]
        public void Exiting_while_the_key_is_held_mutes()
        {
            FakeAudioEndpointProvider provider = null;
            WithApp(s => s.pushToTalk = true, h =>
            {
                provider = h.Provider;
                h.Press();
                Assert.False(h.Provider.Get("a").Muted);
                // the app is disposed with the key still down
            }, "a", "b");
            Assert.True(provider.Devices.All(d => d.Muted));
        }

        [Fact]
        public void Turned_off_the_hotkey_toggles_as_before()
        {
            WithApp(null, h =>
            {
                h.Press();
                Assert.True(h.AllMuted);
                h.Release();
                Thread.Sleep(100);
                Assert.False(h.App.PushToTalkHeld);
                Assert.True(h.AllMuted); // letting go changes nothing

                h.Press();
                h.Release();
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.AnyLive); // the second press toggled back on
            }, "a", "b");
        }

        [Fact]
        public void Turning_it_off_while_held_does_not_leave_the_watcher_running()
        {
            WithApp(s => s.pushToTalk = true, h =>
            {
                h.Press();
                h.App.SetPushToTalk(false);
                Assert.False(h.App.PushToTalkHeld);
                Assert.True(h.App.WaitForAudio());
            }, "a");
        }

        [Fact]
        public void The_settings_switch_turns_it_on_and_off()
        {
            WithApp(null, h =>
            {
                SettingsForm form = h.App.OpenSettings();
                Application.DoEvents();
                Assert.False(form.PushToTalkToggle.Checked);
                form.PushToTalkToggle.Toggle();
                Assert.True(h.App.WaitForAudio());
                Assert.True(h.App.Settings.pushToTalk);
                Assert.True(h.AllMuted);

                h.Press();
                Assert.True(h.AnyLive);
                h.Release();
                Assert.True(h.WaitUntilMuted());

                form.PushToTalkToggle.Toggle();
                Assert.False(h.App.Settings.pushToTalk);
                form.Close();
            }, "a", "b");
        }

        [Fact]
        public void Watcher_reports_a_release_once()
        {
            bool down = true;
            int releases = 0;
            using (var watcher = new KeyReleaseWatcher(() => Interlocked.Increment(ref releases), k => Volatile.Read(ref down)))
            {
                watcher.Start(Keys.F9);
                Thread.Sleep(100);
                Assert.Equal(0, releases);
                Assert.True(watcher.Watching);

                Volatile.Write(ref down, false);
                Assert.True(SpinWait.SpinUntil(() => releases > 0, 2000));
                Thread.Sleep(100);
                Assert.Equal(1, releases);
                Assert.False(watcher.Watching);
            }
        }

        [Fact]
        public void Watcher_treats_an_unreadable_key_as_released()
        {
            int releases = 0;
            using (var watcher = new KeyReleaseWatcher(() => Interlocked.Increment(ref releases), k => throw new InvalidOperationException("no access")))
            {
                watcher.Start(Keys.F9);
                Assert.True(SpinWait.SpinUntil(() => releases == 1, 2000));
            }
        }

        [Fact]
        public void Watcher_stopped_or_disposed_reports_nothing()
        {
            int releases = 0;
            var watcher = new KeyReleaseWatcher(() => Interlocked.Increment(ref releases), k => false);
            watcher.Start(Keys.F9);
            watcher.Stop();
            watcher.Dispose();
            watcher.Start(Keys.F9); // ignored after dispose
            Thread.Sleep(100);
            Assert.InRange(releases, 0, 1); // a check may already have been running when Stop was called
            int seen = releases;
            Thread.Sleep(100);
            Assert.Equal(seen, releases);
        }

        [Fact]
        public void Controller_SetMute_sets_every_target_and_reports_what_it_reads_back()
        {
            var provider = new FakeAudioEndpointProvider();
            provider.Add("a");
            provider.Add("b", muted: true);
            var controller = new MicrophoneController(provider, MicrophoneController.AllDevicesId, syncDevices: false);

            Assert.Equal(MicState.Muted, controller.SetMute(true).State);
            Assert.True(provider.Devices.All(d => d.Muted));
            Assert.Equal(MicState.Live, controller.SetMute(false).State);
            Assert.True(provider.Devices.All(d => !d.Muted));

            provider.Get("b").IgnoreWrites = true; // a driver that ignores the mute
            Assert.Equal(MicState.Live, controller.SetMute(true).State);

            provider.Get("b").IgnoreWrites = false;
            provider.Get("b").FailReads = -1;
            Assert.Equal(MicState.Unknown, controller.SetMute(true).State);
        }
    }
}
