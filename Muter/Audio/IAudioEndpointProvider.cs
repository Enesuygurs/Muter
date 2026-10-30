using System;
using System.Collections.Generic;

namespace Muter.Audio
{
    public enum EndpointRole
    {
        Console,
        Multimedia,
        Communications,
    }

    public sealed class AudioEndpointInfo
    {
        public AudioEndpointInfo(string id, string name)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = string.IsNullOrEmpty(name) ? "Microphone" : name;
        }

        public string Id { get; }

        public string Name { get; }
    }

    public class AudioDeviceException : Exception
    {
        public AudioDeviceException(string message, int hresult = 0, bool enumeratorFailure = false)
            : base(hresult == 0 ? message : $"{message} (HRESULT 0x{hresult:X8})")
        {
            HResult = hresult;
            EnumeratorFailure = enumeratorFailure;
        }

        // True when the device enumerator itself failed (e.g. audio service restarted), so recreating it may help.
        public bool EnumeratorFailure { get; }
    }

    // Abstraction over the system's capture devices. Every read goes to the device itself (no cached state), so a
    // result can never be stale. All methods must be called on the thread that created the provider; the events
    // may be raised on any thread.
    public interface IAudioEndpointProvider : IDisposable
    {
        // Raised when devices are added, removed, enabled/disabled or the default capture device changes.
        event EventHandler DevicesChanged;

        // Raised when the mute/volume state of a watched device changes (from any application).
        event EventHandler MuteStateChanged;

        // Active capture endpoints. Throws AudioDeviceException if the list cannot be read completely.
        IReadOnlyList<AudioEndpointInfo> GetActiveCaptureEndpoints();

        // Id of the default capture endpoint for the role, or null when there is none. Throws on failure.
        string GetDefaultCaptureEndpointId(EndpointRole role);

        // Current mute state read from the device. Throws AudioDeviceException on failure or if the device is not active.
        bool GetMute(string deviceId);

        // Sets the mute state on the device. Throws AudioDeviceException on failure.
        void SetMute(string deviceId, bool mute);

        // Replaces the set of devices whose mute changes raise MuteStateChanged.
        void WatchMuteChanges(IEnumerable<string> deviceIds);
    }
}
