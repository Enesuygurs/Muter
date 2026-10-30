using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // The sound after a mute or unmute must tell the verified result: "muted" only when every microphone reads muted.
    public sealed class SoundFeedbackTests : IDisposable
    {
        private const string StartupKey = @"Software\MuterTests\SoundRun";

        public SoundFeedbackTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        private static void WithApp(FakeAudioEndpointProvider provider, Action<Settings> configure, Action<TrayApplicationContext, List<FeedbackSound>> test)
        {
            TestSetup.RunSta(() =>
            {
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                configure?.Invoke(settings);
                var played = new List<FeedbackSound>();
                using (var app = new TrayApplicationContext(settings, () => provider, new FakeGlobalHotkey(),
                    new StartupRegistration(Application.ExecutablePath, StartupKey), played.Add))
                {
                    Assert.True(app.WaitForAudio());
                    played.Clear();
                    test(app, played);
                }
            });
        }

        private static FakeAudioEndpointProvider Mics(params string[] ids)
        {
            var provider = new FakeAudioEndpointProvider();
            foreach (string id in ids)
                provider.Add(id);
            provider.SetDefault(ids[0]);
            return provider;
        }

        private static void Toggle(TrayApplicationContext app)
        {
            app.ToggleMute();
            Assert.True(app.WaitForAudio());
        }

        [Fact]
        public void Toggling_plays_the_sound_of_the_verified_state()
        {
            WithApp(Mics("a"), null, (app, played) =>
            {
                Toggle(app);
                Toggle(app);
                Assert.Equal(new[] { FeedbackSound.Muted, FeedbackSound.Live }, played);
            });
        }

        [Fact]
        public void A_mute_that_did_not_take_plays_the_live_sound()
        {
            FakeAudioEndpointProvider provider = Mics("a");
            provider.Get("a").IgnoreWrites = true;
            WithApp(provider, null, (app, played) =>
            {
                Toggle(app);
                Assert.Equal(MicState.Live, app.Status.State);
                Assert.Equal(new[] { FeedbackSound.Live }, played);
            });
        }

        [Fact]
        public void An_unreadable_microphone_plays_the_warning_never_the_muted_sound()
        {
            FakeAudioEndpointProvider provider = Mics("a", "b");
            WithApp(provider, s => s.selectedDeviceId = MicrophoneController.AllDevicesId, (app, played) =>
            {
                provider.Get("b").FailReads = -1;
                Toggle(app);
                Assert.True(provider.Get("a").Muted);
                Assert.Equal(MicState.Unknown, app.Status.State);
                Assert.Equal(new[] { FeedbackSound.Warning }, played);
            });
        }

        [Fact]
        public void No_microphone_plays_the_warning()
        {
            FakeAudioEndpointProvider provider = Mics("a");
            provider.Get("a").Active = false;
            WithApp(provider, null, (app, played) =>
            {
                Toggle(app);
                Assert.Equal(MicState.NoDevice, app.Status.State);
                Assert.Equal(new[] { FeedbackSound.Warning }, played);
            });
        }

        [Fact]
        public void Polls_external_changes_and_settings_changes_are_silent()
        {
            FakeAudioEndpointProvider provider = Mics("a", "b");
            WithApp(provider, null, (app, played) =>
            {
                provider.Get("a").Muted = true;
                provider.RaiseMuteStateChanged();
                app.RefreshNow();
                Assert.True(app.WaitForAudio());
                app.SelectDevice(MicrophoneController.AllDevicesId, "All Devices");
                app.SetSyncDevices(false);
                Assert.True(app.WaitForAudio());
                Assert.Empty(played);
            });
        }

        [Fact]
        public void Turned_off_it_plays_nothing()
        {
            WithApp(Mics("a"), s => s.soundFeedback = false, (app, played) =>
            {
                Toggle(app);
                Toggle(app);
                Assert.Empty(played);
            });
        }

        [Fact]
        public void The_settings_switch_saves_the_choice_and_plays_a_sample()
        {
            WithApp(Mics("a"), null, (app, played) =>
            {
                SettingsForm form = app.OpenSettings();
                Application.DoEvents();
                Assert.True(form.SoundToggle.Checked);

                form.SoundToggle.Toggle();
                Assert.False(app.Settings.soundFeedback);
                Toggle(app);
                Assert.Empty(played);

                form.SoundToggle.Toggle();
                Assert.True(app.Settings.soundFeedback);
                Assert.Equal(new[] { FeedbackSound.Muted }, played); // the current state's sound as a sample
                form.Close();
            });
        }

        [Fact]
        public void Every_sound_is_a_valid_short_wave_file()
        {
            foreach (FeedbackSound sound in Enum.GetValues(typeof(FeedbackSound)))
            {
                byte[] wave = FeedbackSounds.Wave(sound);
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
                Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
                Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
                double seconds = (wave.Length - 44) / 2.0 / 44100;
                Assert.InRange(seconds, 0.1, 0.4);
            }
        }
    }
}
