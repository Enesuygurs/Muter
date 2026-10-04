using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Timers;

// Monitors the audio capture device for mute state changes, device changes, and disconnections.
public class AudioDeviceWatcher : IMMNotificationClient
{
    #region Fields and Events

    private readonly MMDeviceEnumerator deviceEnumerator;
    private MMDevice device;
    private string currentDeviceId;
    private string targetDeviceId;
    private bool lastMuteState;
    private readonly Timer debounceTimer;
    private readonly object syncLock = new object();
    private bool isWatching = false;

    // Fired when the audio device's mute state or active device has changed.
    public event EventHandler AudioDeviceChanged;

    public MMDevice CurrentDevice
    {
        get
        {
            lock (syncLock)
            {
                if (device != null && !IsDeviceAlive(device))
                {
                    UnsubscribeDevice(device);
                    device = null;
                    currentDeviceId = null;
                }
                return device;
            }
        }
    }

    public string TargetDeviceId
    {
        get
        {
            lock (syncLock)
            {
                return targetDeviceId;
            }
        }
    }

    #endregion

    #region Constructor

    // Initializes the audio device watcher with default or specific device.
    public AudioDeviceWatcher(string initialDeviceId = null)
    {
        deviceEnumerator = new MMDeviceEnumerator();
        targetDeviceId = initialDeviceId;

        debounceTimer = new Timer(100) { AutoReset = false };
        debounceTimer.Elapsed += OnDebounceTimerElapsed;

        RefreshDeviceInternal(fireEventIfChanged: false);
    }

    #endregion

    #region Public Methods

    // Subscribes to device notifications to start monitoring.
    public void StartWatching()
    {
        lock (syncLock)
        {
            if (isWatching) return;
            isWatching = true;

            try
            {
                deviceEnumerator.RegisterEndpointNotificationCallback(this);
            }
            catch { }

            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    // Unsubscribes from device notifications to stop monitoring.
    public void StopWatching()
    {
        lock (syncLock)
        {
            if (!isWatching) return;
            isWatching = false;

            try
            {
                deviceEnumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch { }

            UnsubscribeDevice(device);
            device = null;
            currentDeviceId = null;
            debounceTimer?.Stop();
        }
    }

    // Switches the targeted audio device (null or empty string means system default).
    public void SetTargetDevice(string deviceId)
    {
        lock (syncLock)
        {
            targetDeviceId = deviceId;
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    // Refreshes the audio device reference.
    public void RefreshDevice()
    {
        lock (syncLock)
        {
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    #endregion

    #region Private Methods & Event Handlers

    private void RefreshDeviceInternal(bool fireEventIfChanged)
    {
        try
        {
            MMDevice newDevice = GetTargetAudioDevice();
            string newId = SafeGetDeviceId(newDevice);

            // If current device is disconnected or dead, clear it
            if (device != null && (!IsDeviceAlive(device) || string.IsNullOrEmpty(currentDeviceId)))
            {
                UnsubscribeDevice(device);
                device = null;
                currentDeviceId = null;
            }

            bool isSame = (device == null && newDevice == null) ||
                          (device != null && newDevice != null && currentDeviceId != null && currentDeviceId == newId);

            if (isSame)
            {
                if (device != null)
                {
                    try
                    {
                        bool currentMute = device.AudioEndpointVolume.Mute;
                        if (currentMute != lastMuteState)
                        {
                            lastMuteState = currentMute;
                            if (fireEventIfChanged)
                            {
                                AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
                            }
                        }
                    }
                    catch
                    {
                        // Device disconnected during mute query
                        UnsubscribeDevice(device);
                        device = null;
                        currentDeviceId = null;
                        if (fireEventIfChanged)
                        {
                            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                return;
            }

            UnsubscribeDevice(device);
            device = newDevice;
            currentDeviceId = newId;

            if (device != null)
            {
                try
                {
                    device.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
                    lastMuteState = device.AudioEndpointVolume.Mute;
                }
                catch
                {
                    UnsubscribeDevice(device);
                    device = null;
                    currentDeviceId = null;
                }
            }

            if (fireEventIfChanged)
            {
                AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            UnsubscribeDevice(device);
            device = null;
            currentDeviceId = null;
            if (fireEventIfChanged)
            {
                AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // Safely gets the target capture device (specified or default).
    private MMDevice GetTargetAudioDevice()
    {
        if (string.IsNullOrEmpty(targetDeviceId))
        {
            try
            {
                MMDevice defaultEndpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                if (IsDeviceAlive(defaultEndpoint))
                {
                    return defaultEndpoint;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }
        else
        {
            try
            {
                MMDevice specificDevice = deviceEnumerator.GetDevice(targetDeviceId);
                if (IsDeviceAlive(specificDevice))
                {
                    return specificDevice;
                }
            }
            catch
            {
                // Device not found or disconnected
            }
            return null;
        }
    }

    private static string SafeGetDeviceId(MMDevice dev)
    {
        if (dev == null) return null;
        try
        {
            return dev.ID;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDeviceAlive(MMDevice dev)
    {
        if (dev == null) return false;
        try
        {
            return dev.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    private void UnsubscribeDevice(MMDevice dev)
    {
        if (dev != null)
        {
            try
            {
                dev.AudioEndpointVolume.OnVolumeNotification -= VolumeNotification;
            }
            catch { }
            try
            {
                dev.Dispose();
            }
            catch { }
        }
    }

    // Handles volume/mute change notifications and triggers debouncing.
    private void VolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            if (data.Muted != lastMuteState)
            {
                lastMuteState = data.Muted;
                debounceTimer.Stop();
                debounceTimer.Start();
            }
        }
        catch { }
    }

    // Fires the AudioDeviceChanged event after the debounce delay.
    private void OnDebounceTimerElapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    #endregion

    #region IMMNotificationClient Implementation

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        try
        {
            if (flow == DataFlow.Capture && role == Role.Communications)
            {
                lock (syncLock)
                {
                    if (string.IsNullOrEmpty(targetDeviceId))
                    {
                        RefreshDeviceInternal(fireEventIfChanged: true);
                    }
                }
            }
        }
        catch { }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        try
        {
            lock (syncLock)
            {
                RefreshDeviceInternal(fireEventIfChanged: true);
            }
        }
        catch { }
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        try
        {
            lock (syncLock)
            {
                RefreshDeviceInternal(fireEventIfChanged: true);
            }
        }
        catch { }
    }

    public void OnDeviceRemoved(string deviceId)
    {
        try
        {
            lock (syncLock)
            {
                RefreshDeviceInternal(fireEventIfChanged: true);
            }
        }
        catch { }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property changes don't require re-attaching
    }

    #endregion
}