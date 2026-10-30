using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace Muter.Tests.Hardware
{
    // Reads and writes the real microphones through NAudio - an implementation independent of Muter's own interop -
    // and restores every microphone's original mute state when the tests finish.
    public sealed class HardwareFixture : IDisposable
    {
        private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();

        public HardwareFixture()
        {
            TestSetup.Init();
            OriginalMute = DeviceIds().ToDictionary(id => id, Read);
        }

        public IReadOnlyDictionary<string, bool> OriginalMute { get; }

        public List<string> DeviceIds()
        {
            var ids = new List<string>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                ids.Add(device.ID);
                device.Dispose();
            }
            return ids;
        }

        public Dictionary<string, string> Names()
        {
            var names = new Dictionary<string, string>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                names[device.ID] = device.FriendlyName;
                device.Dispose();
            }
            return names;
        }

        public string DefaultId(Role role)
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, role))
                return null;
            using (MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role))
                return device.ID;
        }

        public bool Read(string id)
        {
            using (MMDevice device = enumerator.GetDevice(id))
                return device.AudioEndpointVolume.Mute;
        }

        public void Write(string id, bool mute)
        {
            using (MMDevice device = enumerator.GetDevice(id))
                device.AudioEndpointVolume.Mute = mute;
        }

        public void SetAll(bool mute)
        {
            foreach (string id in DeviceIds())
                Write(id, mute);
        }

        public void Restore()
        {
            foreach (var pair in OriginalMute)
            {
                try
                {
                    Write(pair.Key, pair.Value);
                }
                catch
                {
                    // device unplugged during the run
                }
            }
        }

        public void Dispose()
        {
            Restore();
            foreach (var pair in OriginalMute)
            {
                try
                {
                    if (Read(pair.Key) != pair.Value)
                        throw new InvalidOperationException($"Could not restore the mute state of {pair.Key}");
                }
                catch (ArgumentException)
                {
                }
            }
            enumerator.Dispose();
        }
    }
}

namespace Muter.Tests.Hardware
{
    // One shared fixture for every hardware test class. NAudio must create the (process-wide singleton) device
    // enumerator before Muter's code does, because NAudio's wrapper cannot attach to an existing generic one.
    [Xunit.CollectionDefinition("Hardware")]
    public sealed class HardwareCollection : Xunit.ICollectionFixture<HardwareFixture>
    {
    }
}
