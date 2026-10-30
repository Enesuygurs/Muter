using System.Collections.Generic;
using System.Linq;
using Muter.Audio;
using Muter.Tests.Fakes;
using Xunit;

namespace Muter.Tests
{
    public class MicrophoneControllerTests
    {
        private readonly FakeAudioEndpointProvider provider = new FakeAudioEndpointProvider();

        public MicrophoneControllerTests() => TestSetup.Init();

        private MicrophoneController Create(string target = null, bool sync = false)
        {
            var controller = new MicrophoneController(provider, target, sync);
            controller.Refresh();
            return controller;
        }

        // ---- Default mode -------------------------------------------------------------------------------------

        [Fact]
        public void Default_single_device_toggles_between_live_and_muted()
        {
            provider.Add("a");
            provider.SetDefault("a");
            var controller = Create();

            Assert.Equal(MicState.Live, controller.Status.State);
            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.True(provider.Get("a").Muted);
            Assert.Equal(MicState.Live, controller.ToggleMute().State);
            Assert.False(provider.Get("a").Muted);
        }

        [Fact]
        public void Default_mode_controls_recording_and_communications_defaults()
        {
            provider.Add("console");
            provider.Add("comms");
            provider.Add("other");
            provider.SetDefault("console", EndpointRole.Console, EndpointRole.Multimedia);
            provider.SetDefault("comms", EndpointRole.Communications);
            var controller = Create();

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.True(provider.Get("console").Muted);
            Assert.True(provider.Get("comms").Muted);
            Assert.False(provider.Get("other").Muted); // not a default device: untouched
        }

        [Fact]
        public void Default_mode_is_not_muted_while_the_communications_device_is_live()
        {
            // The old version muted only the console default and showed OFF while a call app used the live comms mic.
            provider.Add("console", muted: true);
            provider.Add("comms", muted: false);
            provider.SetDefault("console", EndpointRole.Console, EndpointRole.Multimedia);
            provider.SetDefault("comms", EndpointRole.Communications);
            var controller = Create(sync: false);

            Assert.Equal(MicState.Live, controller.Status.State);
            Assert.Equal(1, controller.Status.LiveCount);
            Assert.Equal(1, controller.Status.MutedCount);
        }

        [Fact]
        public void Default_mode_without_defaults_controls_every_active_device()
        {
            provider.Add("a");
            provider.Add("b");
            var controller = Create();

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
        }

        [Fact]
        public void Default_mode_controls_every_device_when_defaults_cannot_be_read()
        {
            provider.Add("a");
            provider.Add("b");
            provider.SetDefault("a");
            provider.FailDefaults = true;
            var controller = Create();

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
        }

        [Fact]
        public void Default_mode_follows_a_changed_default_device()
        {
            provider.Add("a", muted: true);
            provider.Add("b", muted: false);
            provider.SetDefault("a");
            var controller = Create();
            Assert.Equal(MicState.Muted, controller.Status.State);

            provider.SetDefault("b");
            Assert.Equal(MicState.Live, controller.Refresh().State);
        }

        // ---- All devices mode ----------------------------------------------------------------------------------

        [Fact]
        public void All_devices_toggle_mutes_and_unmutes_every_device()
        {
            provider.Add("a");
            provider.Add("b", muted: true);
            provider.Add("c");
            var controller = Create(MicrophoneController.AllDevicesId);

            Assert.Equal(MicState.Live, controller.Status.State);
            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
            Assert.Equal(MicState.Live, controller.ToggleMute().State);
            Assert.All(provider.Devices, d => Assert.False(d.Muted));
        }

        [Fact]
        public void All_devices_with_one_live_microphone_is_live_without_sync()
        {
            provider.Add("a", muted: true);
            provider.Add("b", muted: false);
            var controller = Create(MicrophoneController.AllDevicesId, sync: false);

            Assert.Equal(MicState.Live, controller.Status.State);
            Assert.False(provider.Get("b").Muted); // sync off: nothing was changed
        }

        [Fact]
        public void New_live_microphone_makes_the_state_live_without_sync()
        {
            provider.Add("a");
            var controller = Create(MicrophoneController.AllDevicesId, sync: false);
            controller.ToggleMute();
            Assert.Equal(MicState.Muted, controller.Status.State);

            provider.Add("usb", muted: false);
            Assert.Equal(MicState.Live, controller.Refresh().State);
        }

        // ---- Sync ----------------------------------------------------------------------------------------------

        [Fact]
        public void Sync_mutes_a_newly_connected_microphone_when_the_others_are_muted()
        {
            provider.Add("a");
            provider.Add("b");
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);
            controller.ToggleMute();

            provider.Add("usb", muted: false);
            provider.SetDefault("usb"); // even if Windows makes the new microphone the default
            Assert.Equal(MicState.Muted, controller.Refresh().State);
            Assert.True(provider.Get("usb").Muted);
        }

        [Fact]
        public void Sync_propagates_an_external_unmute_to_the_other_microphones()
        {
            provider.Add("a");
            provider.Add("b");
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);
            controller.ToggleMute();

            provider.Get("a").Muted = false; // unmuted in Windows settings
            Assert.Equal(MicState.Live, controller.Refresh().State);
            Assert.False(provider.Get("b").Muted);
        }

        [Fact]
        public void Sync_propagates_an_external_mute_to_the_other_microphones()
        {
            provider.Add("a");
            provider.Add("b");
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);

            provider.Get("b").Muted = true;
            Assert.Equal(MicState.Muted, controller.Refresh().State);
            Assert.True(provider.Get("a").Muted);
        }

        [Fact]
        public void Sync_with_unknown_history_follows_the_default_recording_device()
        {
            provider.Add("a", muted: false);
            provider.Add("b", muted: true);
            provider.SetDefault("b");
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);

            Assert.Equal(MicState.Muted, controller.Status.State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
        }

        [Fact]
        public void Sync_with_unknown_history_and_no_default_mutes()
        {
            provider.Add("a", muted: false);
            provider.Add("b", muted: true);
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);

            Assert.Equal(MicState.Muted, controller.Status.State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
        }

        [Fact]
        public void Turning_sync_on_aligns_mixed_microphones()
        {
            provider.Add("a", muted: false);
            provider.Add("b", muted: true);
            provider.SetDefault("a");
            var controller = Create(MicrophoneController.AllDevicesId, sync: false);
            Assert.Equal(MicState.Live, controller.Status.State);

            controller.SyncDevices = true;
            Assert.Equal(MicState.Live, controller.Refresh().State);
            Assert.All(provider.Devices, d => Assert.False(d.Muted)); // followed the default device (live)
        }

        [Fact]
        public void DecideSyncTarget_follows_a_device_that_was_muted()
        {
            var previous = new Dictionary<string, bool> { ["a"] = false, ["b"] = false };
            var current = new Dictionary<string, bool> { ["a"] = true, ["b"] = false };
            Assert.True(MicrophoneController.DecideSyncTarget(current, previous, "b"));
        }

        [Fact]
        public void DecideSyncTarget_follows_a_device_that_was_unmuted()
        {
            var previous = new Dictionary<string, bool> { ["a"] = true, ["b"] = true };
            var current = new Dictionary<string, bool> { ["a"] = true, ["b"] = false };
            Assert.False(MicrophoneController.DecideSyncTarget(current, previous, "a"));
        }

        [Fact]
        public void DecideSyncTarget_conflicting_changes_resolve_to_muted()
        {
            var current = new Dictionary<string, bool> { ["a"] = true, ["b"] = false };
            var previous = new Dictionary<string, bool> { ["a"] = false, ["b"] = true };
            Assert.True(MicrophoneController.DecideSyncTarget(current, previous, "b"));
        }

        [Fact]
        public void DecideSyncTarget_new_devices_follow_known_devices()
        {
            var current = new Dictionary<string, bool> { ["a"] = false, ["b"] = false, ["new"] = true };
            var previous = new Dictionary<string, bool> { ["a"] = false, ["b"] = false };
            Assert.False(MicrophoneController.DecideSyncTarget(current, previous, "new"));
        }

        // ---- Failures: never show muted unless verified ---------------------------------------------------------

        [Fact]
        public void Unreadable_live_microphone_is_unknown_not_muted()
        {
            provider.Add("a", muted: true);
            provider.Add("b", muted: false).FailReads = -1;
            var controller = Create(MicrophoneController.AllDevicesId);

            Assert.Equal(MicState.Unknown, controller.Status.State);
        }

        [Fact]
        public void Unreadable_muted_microphone_is_still_unknown()
        {
            provider.Add("a", muted: true).FailReads = -1;
            provider.SetDefault("a");
            var controller = Create();

            Assert.Equal(MicState.Unknown, controller.Status.State);
        }

        [Fact]
        public void A_single_transient_read_failure_is_retried()
        {
            provider.Add("a", muted: true).FailReads = 1;
            provider.SetDefault("a");
            var controller = Create();

            Assert.Equal(MicState.Muted, controller.Status.State);
        }

        [Fact]
        public void Failed_mute_leaves_the_state_live()
        {
            provider.Add("a");
            provider.Add("b").FailWrites = -1;
            var controller = Create(MicrophoneController.AllDevicesId, sync: true);

            Assert.Equal(MicState.Live, controller.ToggleMute().State);
            Assert.False(provider.Get("b").Muted);
        }

        [Fact]
        public void A_single_transient_write_failure_is_retried()
        {
            provider.Add("a").FailWrites = 1;
            provider.SetDefault("a");
            var controller = Create();

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.True(provider.Get("a").Muted);
        }

        [Fact]
        public void Driver_that_ignores_mute_is_reported_live()
        {
            provider.Add("a").IgnoreWrites = true;
            provider.SetDefault("a");
            var controller = Create();

            Assert.Equal(MicState.Live, controller.ToggleMute().State);
        }

        [Fact]
        public void Enumeration_failure_is_unknown_and_toggle_writes_nothing()
        {
            provider.Add("a");
            provider.SetDefault("a");
            var controller = Create();
            provider.FailEnumeration = true;

            Assert.Equal(MicState.Unknown, controller.Refresh().State);
            Assert.Equal(MicState.Unknown, controller.ToggleMute().State);
            Assert.Equal(0, provider.Get("a").Writes);
        }

        [Fact]
        public void Toggle_from_unknown_mutes_instead_of_unmuting()
        {
            provider.Add("a", muted: true);
            provider.Add("b", muted: true).FailReads = -1;
            var controller = Create(MicrophoneController.AllDevicesId);
            Assert.Equal(MicState.Unknown, controller.Status.State);

            controller.ToggleMute();
            Assert.True(provider.Get("a").Muted);
            Assert.True(provider.Get("b").Muted);
        }

        [Fact]
        public void Toggle_from_partially_muted_mutes_everything()
        {
            provider.Add("a", muted: true);
            provider.Add("b", muted: false);
            var controller = Create(MicrophoneController.AllDevicesId, sync: false);

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.All(provider.Devices, d => Assert.True(d.Muted));
        }

        // ---- Specific device / no device -----------------------------------------------------------------------

        [Fact]
        public void Specific_device_is_controlled_alone_and_matched_case_insensitively()
        {
            provider.Add("{0.0.1.00000000}.{ABC}");
            provider.Add("other");
            var controller = Create("{0.0.1.00000000}.{abc}");

            Assert.Equal(MicState.Muted, controller.ToggleMute().State);
            Assert.True(provider.Get("{0.0.1.00000000}.{ABC}").Muted);
            Assert.False(provider.Get("other").Muted);
        }

        [Fact]
        public void Disconnected_specific_device_reports_no_device_and_recovers()
        {
            FakeDevice mic = provider.Add("mic", muted: true);
            var controller = Create("mic");
            Assert.Equal(MicState.Muted, controller.Status.State);

            mic.Active = false;
            Assert.Equal(MicState.NoDevice, controller.Refresh().State);
            Assert.Equal(MicState.NoDevice, controller.ToggleMute().State);

            mic.Active = true;
            mic.Muted = false;
            Assert.Equal(MicState.Live, controller.Refresh().State);
        }

        [Fact]
        public void No_microphones_is_no_device()
        {
            var controller = Create();
            Assert.Equal(MicState.NoDevice, controller.Status.State);
            Assert.Equal(MicState.NoDevice, controller.ToggleMute().State);
        }

        [Fact]
        public void Duplicate_ids_from_the_system_are_counted_once()
        {
            provider.Add("a", muted: true);
            provider.Add("A", muted: true);
            var controller = Create(MicrophoneController.AllDevicesId);
            Assert.Equal(1, controller.Status.DeviceCount);
        }

        // ---- Events and watching -------------------------------------------------------------------------------

        [Fact]
        public void StatusChanged_is_raised_only_when_the_status_changes()
        {
            provider.Add("a");
            provider.SetDefault("a");
            var controller = new MicrophoneController(provider, null, false);
            int raised = 0;
            controller.StatusChanged += (s, e) => raised++;

            controller.Refresh();
            controller.Refresh();
            Assert.Equal(1, raised);
            controller.ToggleMute();
            Assert.Equal(2, raised);
        }

        [Fact]
        public void Watches_the_targeted_devices_and_rewatches_after_device_changes()
        {
            provider.Add("a");
            provider.Add("b");
            provider.SetDefault("a");
            var controller = Create();
            Assert.Equal(new[] { "a" }, provider.WatchCalls.Last().ToArray());

            int calls = provider.WatchCalls.Count;
            controller.Refresh();
            Assert.Equal(calls, provider.WatchCalls.Count); // unchanged targets: no re-registration

            controller.NotifyDevicesChanged();
            controller.Refresh();
            Assert.Equal(calls + 1, provider.WatchCalls.Count);

            controller.SetTarget(MicrophoneController.AllDevicesId);
            Assert.True(provider.WatchCalls.Last().SetEquals(new[] { "a", "b" }));
        }
    }
}
