using System;
using System.Collections.Generic;
using System.Linq;
using Muter.Audio;
using Muter.Tests.Fakes;
using Xunit;

namespace Muter.Tests
{
    // Randomized scenarios (devices appearing/disappearing, defaults changing, external mute changes, read/write
    // failures, misbehaving drivers, mode and sync changes) checked against an independent model after every step.
    public class MicrophoneControllerSafetyTests
    {
        private const int Scenarios = 4000;
        private const int StepsPerScenario = 40;
        private static readonly EndpointRole[] Roles = { EndpointRole.Console, EndpointRole.Multimedia, EndpointRole.Communications };

        public MicrophoneControllerSafetyTests() => TestSetup.Init();

        [Fact]
        public void Random_scenarios_never_report_a_false_state()
        {
            for (int seed = 0; seed < Scenarios; seed++)
                new Scenario(seed).Run();
        }

        private sealed class Scenario
        {
            private readonly Random rng;
            private readonly int seed;
            private readonly FakeAudioEndpointProvider provider = new FakeAudioEndpointProvider();
            private readonly List<string> history = new List<string>();
            private MicrophoneController controller;
            private string target;
            private int nextId;

            public Scenario(int seed)
            {
                this.seed = seed;
                rng = new Random(seed);
            }

            public void Run()
            {
                int initialDevices = rng.Next(0, 5);
                for (int i = 0; i < initialDevices; i++)
                    AddDevice();
                RandomizeDefaults();
                target = RandomTarget();
                controller = new MicrophoneController(provider, target, rng.Next(2) == 0);
                history.Add($"start target={target ?? "default"} sync={controller.SyncDevices} devices=[{string.Join(", ", provider.Devices)}]");
                Verify("initial refresh", () => controller.Refresh(), toggle: false);

                for (int step = 0; step < StepsPerScenario; step++)
                {
                    switch (rng.Next(14))
                    {
                        case 0:
                        case 1:
                        case 2:
                            Verify("toggle", () => controller.ToggleMute(), toggle: true);
                            continue;
                        case 3:
                            Verify("refresh", () => controller.Refresh(), toggle: false);
                            continue;
                        case 4:
                            if (provider.Devices.Count > 0)
                            {
                                FakeDevice d = Pick();
                                d.Muted = rng.Next(2) == 0;
                                history.Add($"external: {d.Id} -> {(d.Muted ? "muted" : "live")}");
                            }
                            break;
                        case 5:
                            if (provider.Devices.Count < 6)
                                AddDevice();
                            break;
                        case 6:
                            if (provider.Devices.Count > 0)
                            {
                                FakeDevice d = Pick();
                                d.Active = !d.Active;
                                history.Add($"{d.Id} active={d.Active}");
                                controller.NotifyDevicesChanged();
                            }
                            break;
                        case 7:
                            RandomizeDefaults();
                            break;
                        case 8:
                            if (provider.Devices.Count > 0)
                            {
                                FakeDevice d = Pick();
                                d.FailReads = new[] { 0, 0, 1, -1 }[rng.Next(4)];
                                history.Add($"{d.Id} failReads={d.FailReads}");
                            }
                            break;
                        case 9:
                            if (provider.Devices.Count > 0)
                            {
                                FakeDevice d = Pick();
                                d.FailWrites = new[] { 0, 0, 1, -1 }[rng.Next(4)];
                                d.IgnoreWrites = rng.Next(6) == 0;
                                history.Add($"{d.Id} failWrites={d.FailWrites} ignoreWrites={d.IgnoreWrites}");
                            }
                            break;
                        case 10:
                            provider.FailEnumeration = rng.Next(5) == 0;
                            provider.FailDefaults = rng.Next(5) == 0;
                            history.Add($"failEnumeration={provider.FailEnumeration} failDefaults={provider.FailDefaults}");
                            break;
                        case 11:
                            target = RandomTarget();
                            Verify($"set target {target ?? "default"}", () => controller.SetTarget(target), toggle: false);
                            continue;
                        case 12:
                            controller.SyncDevices = !controller.SyncDevices;
                            Verify($"sync={controller.SyncDevices}", () => controller.Refresh(), toggle: false);
                            continue;
                        default:
                            Verify("toggle", () => controller.ToggleMute(), toggle: true);
                            continue;
                    }

                    // Usually a notification or the poll refreshes after an external change; sometimes it is missed.
                    if (rng.Next(3) != 0)
                        Verify("refresh after change", () => controller.Refresh(), toggle: false);
                }
            }

            private void Verify(string operation, Func<MicrophoneStatus> action, bool toggle)
            {
                List<FakeDevice> targetsBefore = OracleTargets();
                bool[] mutedBefore = targetsBefore.Select(d => d.Muted).ToArray();
                bool readFailuresBefore = targetsBefore.Any(d => d.FailReads != 0);
                bool writeProblemsBefore = targetsBefore.Any(d => d.FailWrites != 0 || d.IgnoreWrites);
                bool enumerationFailing = provider.FailEnumeration;
                Dictionary<FakeDevice, int> writesBefore = provider.Devices.ToDictionary(d => d, d => d.Writes);
                Dictionary<FakeDevice, bool> stateBefore = provider.Devices.ToDictionary(d => d, d => d.Muted);

                MicrophoneStatus status = action();
                Assert.Equal(status, controller.Status);
                history.Add($"{operation} -> {status}  devices=[{string.Join(", ", provider.Devices)}]");

                List<FakeDevice> targets = OracleTargets();
                string context = $"seed {seed}:\n  " + string.Join("\n  ", history);

                if (enumerationFailing)
                {
                    Assert.True(status.State == MicState.Unknown, "enumeration failure must be Unknown. " + context);
                    Assert.True(provider.Devices.All(d => d.Writes == writesBefore[d]), "nothing may be written when devices cannot be listed. " + context);
                    return;
                }

                // I1: Muted only if every targeted microphone really is muted.
                if (status.State == MicState.Muted)
                    Assert.True(targets.Count > 0 && targets.All(d => d.Muted), "FALSE MUTED. " + context);

                // I2: NoDevice only if there really is no targeted microphone.
                if (status.State == MicState.NoDevice)
                    Assert.True(targets.Count == 0, "false NoDevice. " + context);
                else
                    Assert.True(targets.Count > 0 || status.State == MicState.Unknown, "missing NoDevice. " + context);

                // I3: Live only if a targeted microphone really is live.
                if (status.State == MicState.Live)
                    Assert.True(targets.Any(d => !d.Muted), "false Live. " + context);

                Assert.Equal(targets.Count, status.DeviceCount);

                // I4: without read failures the state is exact.
                bool readFailuresNow = targets.Any(d => d.FailReads != 0);
                if (!readFailuresBefore && !readFailuresNow)
                {
                    MicState expected = targets.Count == 0 ? MicState.NoDevice : targets.All(d => d.Muted) ? MicState.Muted : MicState.Live;
                    Assert.True(expected == status.State, $"expected {expected}. " + context);
                }

                // I7: Unknown only when something could not be read.
                if (status.State == MicState.Unknown)
                    Assert.True(readFailuresBefore || readFailuresNow, "spurious Unknown. " + context);

                // I6: devices that are not targeted are never touched.
                foreach (FakeDevice d in provider.Devices.Where(d => !targets.Contains(d) && writesBefore.ContainsKey(d)))
                {
                    Assert.True(d.Writes == writesBefore[d], $"non-target {d.Id} was written. " + context);
                    Assert.True(d.Muted == stateBefore[d], $"non-target {d.Id} changed. " + context);
                }

                // I5: a toggle without failures goes the right way: all muted -> unmute all, otherwise mute all.
                if (toggle && targetsBefore.Count > 0 && !readFailuresBefore && !writeProblemsBefore && targets.SequenceEqual(targetsBefore))
                {
                    bool shouldMute = !mutedBefore.All(m => m);
                    Assert.True(targets.All(d => d.Muted == shouldMute), $"toggle should have {(shouldMute ? "muted" : "unmuted")} all. " + context);
                    Assert.True(status.State == (shouldMute ? MicState.Muted : MicState.Live), "toggle result state. " + context);
                }
            }

            // Independent model of which microphones must be controlled.
            private List<FakeDevice> OracleTargets()
            {
                List<FakeDevice> active = provider.Devices.Where(d => d.Active).ToList();
                if (target == MicrophoneController.AllDevicesId)
                    return active;
                if (target != null)
                    return active.Where(d => string.Equals(d.Id, target, StringComparison.OrdinalIgnoreCase)).ToList();
                if (provider.FailDefaults)
                    return active;

                var defaults = new List<FakeDevice>();
                foreach (EndpointRole role in new[] { EndpointRole.Console, EndpointRole.Communications, EndpointRole.Multimedia })
                {
                    if (provider.Defaults.TryGetValue(role, out string id))
                    {
                        FakeDevice d = active.FirstOrDefault(x => x.Id == id);
                        if (d != null && !defaults.Contains(d))
                            defaults.Add(d);
                    }
                }
                return defaults.Count > 0 ? defaults : active;
            }

            private void AddDevice()
            {
                FakeDevice d = provider.Add("d" + nextId++, muted: rng.Next(2) == 0);
                history.Add($"add {d}");
                controller?.NotifyDevicesChanged();
            }

            private void RandomizeDefaults()
            {
                provider.Defaults.Clear();
                foreach (EndpointRole role in Roles)
                {
                    if (provider.Devices.Count > 0 && rng.Next(5) != 0)
                        provider.Defaults[role] = Pick().Id;
                }
                history.Add("defaults: " + string.Join(", ", provider.Defaults.Select(p => $"{p.Key}={p.Value}")));
            }

            private string RandomTarget()
            {
                switch (rng.Next(4))
                {
                    case 0: return null;
                    case 1: return MicrophoneController.AllDevicesId;
                    case 2: return provider.Devices.Count > 0 ? Pick().Id.ToUpperInvariant() : "missing";
                    default: return "missing-device";
                }
            }

            private FakeDevice Pick() => provider.Devices[rng.Next(provider.Devices.Count)];
        }
    }
}
