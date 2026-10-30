using System;
using System.Collections.Generic;
using System.Linq;

namespace Muter.Audio
{
    // Decides which microphones are controlled and computes the state shown to the user.
    //
    // Safety rules:
    //  * The state is always computed from values freshly read back from the devices - never from a cache and never
    //    from the value that was just written.
    //  * Muted is reported only if every targeted microphone was read successfully and is muted. A read failure makes
    //    the state Unknown, which the UI presents as live.
    //  * If the targeted microphones cannot be determined at all, the state is Unknown (never NoDevice or Muted).
    //
    // Targets: "Default" mode controls the default recording device AND the default communications device (they can
    // differ, and call apps usually use the communications one). "All devices" controls every active microphone.
    //
    // Not thread-safe: use from a single (UI) thread.
    public sealed class MicrophoneController
    {
        public const string AllDevicesId = "ALL_DEVICES";

        private static readonly EndpointRole[] DefaultRoles = { EndpointRole.Console, EndpointRole.Communications, EndpointRole.Multimedia };

        private readonly IAudioEndpointProvider provider;
        private readonly Dictionary<string, bool> lastKnownMute = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> watchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool rewatchRequested = true;
        private string targetDeviceId;

        public MicrophoneController(IAudioEndpointProvider provider, string targetDeviceId, bool syncDevices)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.targetDeviceId = Normalize(targetDeviceId);
            SyncDevices = syncDevices;
        }

        public event EventHandler StatusChanged;

        // Last published status. Unknown until the first successful refresh.
        public MicrophoneStatus Status { get; private set; } = MicrophoneStatus.ReadFailure;

        // null = default devices, AllDevicesId = every microphone, otherwise a device id.
        public string TargetDeviceId => targetDeviceId;

        public bool IsAllDevicesMode => string.Equals(targetDeviceId, AllDevicesId, StringComparison.Ordinal);

        public bool IsDefaultMode => targetDeviceId == null;

        // Keeps every targeted microphone in the same mute state when they drift apart (e.g. changed in Windows).
        public bool SyncDevices { get; set; }

        public MicrophoneStatus SetTarget(string deviceId)
        {
            targetDeviceId = Normalize(deviceId);
            lastKnownMute.Clear();
            rewatchRequested = true;
            return Refresh();
        }

        // Devices were added/removed/changed: re-subscribe to mute notifications on the next refresh.
        public void NotifyDevicesChanged() => rewatchRequested = true;

        public IReadOnlyList<AudioEndpointInfo> GetCaptureDevices() => provider.GetActiveCaptureEndpoints();

        public MicrophoneStatus Refresh() => Publish(Evaluate());

        public MicrophoneStatus ToggleMute()
        {
            Snapshot snapshot;
            try
            {
                snapshot = ReadSnapshot();
            }
            catch (Exception ex)
            {
                Log.Warn("Toggle failed: the microphones could not be read", ex);
                return Publish(Fail());
            }

            if (snapshot.Targets.Count > 0)
            {
                // Unmute only when everything is verified muted. Live, partially muted or unknown -> mute.
                bool mute = Classify(snapshot).State != MicState.Muted;
                Log.Info($"Toggle: {(mute ? "muting" : "unmuting")} {snapshot.Targets.Count} microphone(s)");
                Apply(snapshot, mute, onlyDifferent: false);
            }

            return Publish(Evaluate());
        }

        private MicrophoneStatus Evaluate()
        {
            Snapshot snapshot;
            try
            {
                snapshot = ReadSnapshot();
                if (SyncDevices && snapshot.IsMixed)
                {
                    bool mute = DecideSyncTarget(snapshot.Readable(), lastKnownMute, TryGetDefaultConsoleId());
                    Log.Info($"Sync: setting {snapshot.Targets.Count} microphones to {(mute ? "muted" : "unmuted")}");
                    Apply(snapshot, mute, onlyDifferent: true);
                    snapshot = ReadSnapshot();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("The microphones could not be read", ex);
                return Fail();
            }

            lastKnownMute.Clear();
            foreach (var pair in snapshot.Readable())
                lastKnownMute[pair.Key] = pair.Value;

            EnsureWatching(snapshot);
            return Classify(snapshot);
        }

        // Which state mixed microphones are synced to:
        //  1. A microphone that changed since the last refresh (someone toggled it) - the others follow it.
        //  2. Otherwise newly connected microphones follow the ones that were already known.
        //  3. Otherwise the default recording device decides; if that is unavailable, mute.
        // Conflicting signals resolve to muted.
        internal static bool DecideSyncTarget(IReadOnlyDictionary<string, bool> current, IReadOnlyDictionary<string, bool> previous, string defaultConsoleId)
        {
            List<bool> changed = current
                .Where(p => previous.TryGetValue(p.Key, out bool old) && old != p.Value)
                .Select(p => p.Value)
                .Distinct()
                .ToList();
            if (changed.Count > 0)
                return changed.Count == 1 ? changed[0] : true;

            List<bool> known = current
                .Where(p => previous.ContainsKey(p.Key))
                .Select(p => p.Value)
                .Distinct()
                .ToList();
            if (known.Count == 1)
                return known[0];

            if (defaultConsoleId != null && current.TryGetValue(defaultConsoleId, out bool defaultMute))
                return defaultMute;

            return true;
        }

        private static MicrophoneStatus Classify(Snapshot snapshot)
        {
            MicState state;
            if (snapshot.Targets.Count == 0)
                state = MicState.NoDevice;
            else if (snapshot.LiveCount > 0)
                state = MicState.Live;
            else if (snapshot.UnreadableCount > 0)
                state = MicState.Unknown;
            else
                state = MicState.Muted;

            return new MicrophoneStatus(state, snapshot.MutedCount, snapshot.LiveCount, snapshot.UnreadableCount, snapshot.Targets.Select(t => t.Name).ToList());
        }

        private Snapshot ReadSnapshot()
        {
            List<AudioEndpointInfo> targets = ResolveTargets();
            var readings = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (AudioEndpointInfo target in targets)
                readings[target.Id] = TryReadMute(target.Id);
            return new Snapshot(targets, readings);
        }

        private List<AudioEndpointInfo> ResolveTargets()
        {
            List<AudioEndpointInfo> active = provider.GetActiveCaptureEndpoints()
                .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (IsAllDevicesMode)
                return active;
            if (IsDefaultMode)
                return ResolveDefaultTargets(active);
            return active.Where(e => string.Equals(e.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<AudioEndpointInfo> ResolveDefaultTargets(List<AudioEndpointInfo> active)
        {
            var targets = new List<AudioEndpointInfo>();
            try
            {
                foreach (EndpointRole role in DefaultRoles)
                {
                    string id = provider.GetDefaultCaptureEndpointId(role);
                    AudioEndpointInfo endpoint = id == null ? null : active.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (endpoint != null && !targets.Contains(endpoint))
                        targets.Add(endpoint);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not resolve the default microphones; controlling every active microphone instead", ex);
                return active;
            }

            // Safety net: if Windows reports no usable default while microphones are active, control all of them.
            return targets.Count > 0 ? targets : active;
        }

        private bool? TryReadMute(string deviceId)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return provider.GetMute(deviceId);
                }
                catch (Exception ex)
                {
                    if (attempt >= 2)
                    {
                        Log.Warn($"Could not read the mute state of {deviceId}", ex);
                        return null;
                    }
                }
            }
        }

        private void Apply(Snapshot snapshot, bool mute, bool onlyDifferent)
        {
            foreach (AudioEndpointInfo target in snapshot.Targets)
            {
                if (onlyDifferent && snapshot.Readings[target.Id] == mute)
                    continue;

                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        provider.SetMute(target.Id, mute);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt >= 2)
                        {
                            Log.Warn($"Could not {(mute ? "mute" : "unmute")} {target.Name} ({target.Id})", ex);
                            break;
                        }
                    }
                }
            }
        }

        private string TryGetDefaultConsoleId()
        {
            try
            {
                return provider.GetDefaultCaptureEndpointId(EndpointRole.Console);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the default recording device", ex);
                return null;
            }
        }

        private void EnsureWatching(Snapshot snapshot)
        {
            var ids = new HashSet<string>(snapshot.Targets.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
            if (!rewatchRequested && ids.SetEquals(watchedIds))
                return;

            try
            {
                provider.WatchMuteChanges(ids);
                watchedIds = ids;
                rewatchRequested = false;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not watch microphone mute changes", ex);
            }
        }

        private MicrophoneStatus Fail()
        {
            lastKnownMute.Clear();
            rewatchRequested = true;
            return MicrophoneStatus.ReadFailure;
        }

        private MicrophoneStatus Publish(MicrophoneStatus status)
        {
            if (!status.Equals(Status))
            {
                if (status.State != Status.State)
                    Log.Info($"Microphone state: {Status.State} -> {status}");
                Status = status;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            return status;
        }

        private static string Normalize(string deviceId) => string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

        private sealed class Snapshot
        {
            public Snapshot(List<AudioEndpointInfo> targets, Dictionary<string, bool?> readings)
            {
                Targets = targets;
                Readings = readings;
                MutedCount = readings.Values.Count(v => v == true);
                LiveCount = readings.Values.Count(v => v == false);
                UnreadableCount = readings.Values.Count(v => v == null);
            }

            public List<AudioEndpointInfo> Targets { get; }

            public Dictionary<string, bool?> Readings { get; }

            public int MutedCount { get; }

            public int LiveCount { get; }

            public int UnreadableCount { get; }

            public bool IsMixed => MutedCount > 0 && LiveCount > 0;

            public Dictionary<string, bool> Readable()
            {
                var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in Readings)
                {
                    if (pair.Value.HasValue)
                        result[pair.Key] = pair.Value.Value;
                }
                return result;
            }
        }
    }
}
