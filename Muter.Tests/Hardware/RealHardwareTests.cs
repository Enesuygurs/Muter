using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Muter.Audio;
using NAudio.CoreAudioApi;
using Xunit;
using Xunit.Abstractions;

namespace Muter.Tests.Hardware
{
    // Runs against this computer's real microphones (mute states are restored afterwards).
    [Collection("Hardware")]
    public sealed class RealHardwareTests : IDisposable
    {
        private readonly HardwareFixture hw;
        private readonly ITestOutputHelper output;
        private readonly List<string> ids;

        public RealHardwareTests(HardwareFixture hw, ITestOutputHelper output)
        {
            this.hw = hw;
            this.output = output;
            ids = hw.DeviceIds();
            Assert.True(ids.Count > 0, "These tests need at least one active microphone.");
            output.WriteLine($"{ids.Count} microphone(s), {(Environment.Is64BitProcess ? "64" : "32")}-bit process");
        }

        public void Dispose() => hw.Restore();

        [Fact]
        public void Lists_the_same_microphones_and_names_as_NAudio()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                IReadOnlyList<AudioEndpointInfo> mine = provider.GetActiveCaptureEndpoints();
                Dictionary<string, string> theirs = hw.Names();
                Assert.Equal(theirs.Keys.OrderBy(k => k), mine.Select(e => e.Id).OrderBy(k => k));
                foreach (AudioEndpointInfo e in mine)
                {
                    output.WriteLine($"{e.Name}  {e.Id}");
                    Assert.Equal(theirs[e.Id], e.Name);
                }
            }
        }

        [Fact]
        public void Default_devices_match_NAudio_for_every_role()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                Assert.Equal(hw.DefaultId(Role.Console), provider.GetDefaultCaptureEndpointId(EndpointRole.Console));
                Assert.Equal(hw.DefaultId(Role.Multimedia), provider.GetDefaultCaptureEndpointId(EndpointRole.Multimedia));
                Assert.Equal(hw.DefaultId(Role.Communications), provider.GetDefaultCaptureEndpointId(EndpointRole.Communications));
            }
        }

        [Fact]
        public void Reads_and_writes_agree_with_NAudio_on_every_microphone()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                foreach (string id in ids)
                {
                    foreach (bool mute in new[] { true, false, true, false })
                    {
                        hw.Write(id, mute);
                        Assert.Equal(mute, provider.GetMute(id));

                        provider.SetMute(id, !mute);
                        Assert.Equal(!mute, hw.Read(id));
                        Assert.Equal(!mute, provider.GetMute(id));
                    }
                }
            }
        }

        [Fact]
        public void Works_on_an_STA_thread_like_the_application()
        {
            TestSetup.RunSta(() =>
            {
                using (var provider = new CoreAudioEndpointProvider())
                {
                    foreach (string id in ids)
                    {
                        provider.SetMute(id, true);
                        Assert.True(hw.Read(id));
                        provider.SetMute(id, false);
                        Assert.False(hw.Read(id));
                    }
                }
            });
        }

        [Fact]
        public void Mute_changes_by_another_program_raise_a_notification()
        {
            using (var provider = new CoreAudioEndpointProvider())
            using (var signal = new ManualResetEventSlim())
            {
                provider.MuteStateChanged += (s, e) => signal.Set();
                foreach (string id in ids)
                {
                    provider.WatchMuteChanges(new[] { id });
                    bool current = hw.Read(id);
                    signal.Reset();
                    hw.Write(id, !current);
                    Assert.True(signal.Wait(2000), "no notification for " + id);
                }

                provider.WatchMuteChanges(Array.Empty<string>());
                signal.Reset();
                hw.Write(ids[0], !hw.Read(ids[0]));
                Assert.False(signal.Wait(500), "unwatched device still notifies");
            }
        }

        [Fact]
        public void Controller_all_devices_mode_matches_the_hardware_on_every_toggle()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                var controller = new MicrophoneController(provider, MicrophoneController.AllDevicesId, syncDevices: false);
                hw.SetAll(false);
                Assert.Equal(MicState.Live, controller.Refresh().State);

                for (int i = 0; i < 20; i++)
                {
                    MicrophoneStatus status = controller.ToggleMute();
                    bool[] real = ids.Select(hw.Read).ToArray();
                    Assert.Equal(real.All(m => m) ? MicState.Muted : MicState.Live, status.State);
                    Assert.Equal(i % 2 == 0, real.All(m => m));
                    Assert.Equal(i % 2 == 0, !real.Any(m => !m));
                }

                if (ids.Count > 1)
                {
                    hw.SetAll(true);
                    hw.Write(ids[1], false); // one live microphone
                    Assert.Equal(MicState.Live, controller.Refresh().State);
                }
            }
        }

        [Fact]
        public void Controller_default_mode_controls_the_default_microphones_only()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                var defaults = new[] { hw.DefaultId(Role.Console), hw.DefaultId(Role.Communications), hw.DefaultId(Role.Multimedia) }
                    .Where(id => id != null).Distinct().ToList();
                List<string> others = ids.Except(defaults).ToList();
                hw.SetAll(false);

                var controller = new MicrophoneController(provider, null, syncDevices: false);
                Assert.Equal(MicState.Muted, controller.ToggleMute().State);
                Assert.All(defaults, id => Assert.True(hw.Read(id)));
                Assert.All(others, id => Assert.False(hw.Read(id)));

                Assert.Equal(MicState.Live, controller.ToggleMute().State);
                Assert.All(defaults, id => Assert.False(hw.Read(id)));
            }
        }

        [Fact]
        public void Sync_aligns_real_microphones_after_an_external_change()
        {
            if (ids.Count < 2)
                return;
            using (var provider = new CoreAudioEndpointProvider())
            {
                hw.SetAll(false);
                var controller = new MicrophoneController(provider, MicrophoneController.AllDevicesId, syncDevices: true);
                Assert.Equal(MicState.Live, controller.Refresh().State);

                hw.Write(ids[0], true); // muted in Windows settings
                Assert.Equal(MicState.Muted, controller.Refresh().State);
                Assert.All(ids, id => Assert.True(hw.Read(id)));

                hw.Write(ids[1], false); // unmuted by another program
                Assert.Equal(MicState.Live, controller.Refresh().State);
                Assert.All(ids, id => Assert.False(hw.Read(id)));
            }
        }

        [Fact]
        public void Unknown_device_id_fails_cleanly()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                Assert.Throws<AudioDeviceException>(() => provider.GetMute("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}"));
                Assert.Throws<AudioDeviceException>(() => provider.SetMute("not-a-device", true));
                Assert.Throws<ArgumentException>(() => provider.GetMute(""));
            }
        }

        [Fact]
        public void Disposed_provider_rejects_calls_and_dispose_is_idempotent()
        {
            var provider = new CoreAudioEndpointProvider();
            provider.WatchMuteChanges(ids);
            provider.Dispose();
            provider.Dispose();
            Assert.Throws<ObjectDisposedException>(() => provider.GetActiveCaptureEndpoints());
        }

        [Fact]
        public void No_handle_or_memory_leak_and_fast_enough_to_poll()
        {
            using (var provider = new CoreAudioEndpointProvider())
            {
                var controller = new MicrophoneController(provider, MicrophoneController.AllDevicesId, syncDevices: true);
                for (int i = 0; i < 50; i++)
                    controller.Refresh();
                Settle();
                Process process = Process.GetCurrentProcess();
                process.Refresh();
                int handlesBefore = process.HandleCount;
                long memoryBefore = GC.GetTotalMemory(true);

                var watch = Stopwatch.StartNew();
                const int refreshes = 2000;
                for (int i = 0; i < refreshes; i++)
                {
                    controller.Refresh();
                    if (i % 10 == 0)
                        controller.NotifyDevicesChanged(); // forces re-registering notifications
                }
                double msPerRefresh = watch.Elapsed.TotalMilliseconds / refreshes;
                for (int i = 0; i < 100; i++)
                    controller.ToggleMute();

                Settle();
                process.Refresh();
                int handleGrowth = process.HandleCount - handlesBefore;
                long memoryGrowth = GC.GetTotalMemory(true) - memoryBefore;
                output.WriteLine($"{msPerRefresh:0.000} ms per refresh, handle growth {handleGrowth}, managed memory growth {memoryGrowth / 1024} KB");

                Assert.True(msPerRefresh < 20, $"refresh too slow: {msPerRefresh} ms");
                Assert.True(handleGrowth < 50, $"handle leak: {handleGrowth}");
                Assert.True(memoryGrowth < 2 * 1024 * 1024, $"memory leak: {memoryGrowth}");
            }
        }

        private static void Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Thread.Sleep(200);
        }
    }
}
