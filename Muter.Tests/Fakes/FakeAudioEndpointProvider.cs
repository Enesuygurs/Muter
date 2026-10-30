using System;
using System.Collections.Generic;
using System.Linq;
using Muter.Audio;

namespace Muter.Tests.Fakes
{
    internal sealed class FakeDevice
    {
        public FakeDevice(string id, string name, bool muted)
        {
            Id = id;
            Name = name;
            Muted = muted;
        }

        public string Id { get; }

        public string Name { get; }

        // Ground truth: what the hardware really is.
        public bool Muted { get; set; }

        public bool Active { get; set; } = true;

        // Number of upcoming reads/writes that fail; -1 = fail forever.
        public int FailReads { get; set; }

        public int FailWrites { get; set; }

        // Writes report success but the device does not change (misbehaving driver).
        public bool IgnoreWrites { get; set; }

        public int Writes { get; set; }

        public override string ToString() => $"{Id}{(Active ? "" : " (inactive)")}: {(Muted ? "muted" : "LIVE")}";
    }

    // In-memory IAudioEndpointProvider with failure injection. Thread-safe: the app uses it from its audio thread.
    internal sealed class FakeAudioEndpointProvider : IAudioEndpointProvider
    {
        private readonly object sync = new object();

        // When set, reads wait for it: simulates a hung audio driver.
        public System.Threading.ManualResetEventSlim BlockReads { get; set; }

        // Like a race with a device that just vanished: defaults are reported even for inactive devices.
        public bool ReportInactiveDefaults { get; set; }

        public List<FakeDevice> Devices { get; } = new List<FakeDevice>();

        public Dictionary<EndpointRole, string> Defaults { get; } = new Dictionary<EndpointRole, string>();

        public bool FailEnumeration { get; set; }

        public bool FailDefaults { get; set; }

        public List<HashSet<string>> WatchCalls { get; } = new List<HashSet<string>>();

        public bool Disposed { get; private set; }

        public event EventHandler DevicesChanged;

        public event EventHandler MuteStateChanged;

        public FakeDevice Add(string id, bool muted = false, string name = null)
        {
            var device = new FakeDevice(id, name ?? "Mic " + id, muted);
            lock (sync)
                Devices.Add(device);
            return device;
        }

        public FakeDevice Get(string id) => Devices.First(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        public void SetDefault(string id, params EndpointRole[] roles)
        {
            foreach (EndpointRole role in roles.Length == 0 ? new[] { EndpointRole.Console, EndpointRole.Multimedia, EndpointRole.Communications } : roles)
                Defaults[role] = id;
        }

        public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseMuteStateChanged() => MuteStateChanged?.Invoke(this, EventArgs.Empty);

        public IReadOnlyList<AudioEndpointInfo> GetActiveCaptureEndpoints()
        {
            if (FailEnumeration)
                throw new AudioDeviceException("enumeration failed (fake)", unchecked((int)0x80004005), enumeratorFailure: true);
            lock (sync)
                return Devices.Where(d => d.Active).Select(d => new AudioEndpointInfo(d.Id, d.Name)).ToList();
        }

        public string GetDefaultCaptureEndpointId(EndpointRole role)
        {
            if (FailDefaults)
                throw new AudioDeviceException("default lookup failed (fake)", unchecked((int)0x80004005), enumeratorFailure: true);
            // Like Windows: a default device is always an active one.
            lock (sync)
            {
                return Defaults.TryGetValue(role, out string id) && (ReportInactiveDefaults || Devices.Any(d => d.Active && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
                    ? id
                    : null;
            }
        }

        public bool GetMute(string deviceId)
        {
            BlockReads?.Wait();
            FakeDevice device = Find(deviceId);
            if (device.FailReads != 0)
            {
                if (device.FailReads > 0)
                    device.FailReads--;
                throw new AudioDeviceException("read failed (fake)", unchecked((int)0x88890004));
            }
            return device.Muted;
        }

        public void SetMute(string deviceId, bool mute)
        {
            FakeDevice device = Find(deviceId);
            if (device.FailWrites != 0)
            {
                if (device.FailWrites > 0)
                    device.FailWrites--;
                throw new AudioDeviceException("write failed (fake)", unchecked((int)0x88890004));
            }
            device.Writes++;
            if (!device.IgnoreWrites)
                device.Muted = mute;
        }

        public void WatchMuteChanges(IEnumerable<string> deviceIds)
        {
            lock (sync)
                WatchCalls.Add(new HashSet<string>(deviceIds, StringComparer.OrdinalIgnoreCase));
        }

        public void Dispose() => Disposed = true;

        private FakeDevice Find(string deviceId)
        {
            FakeDevice device;
            lock (sync)
                device = Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (device == null || !device.Active)
                throw new AudioDeviceException("device not active (fake)");
            return device;
        }
    }
}
