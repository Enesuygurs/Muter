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
        MMDevice newDevice = GetTargetAudioDevice();

        bool isSame = (device == null && newDevice == null) ||
                      (device != null && newDevice != null && device.ID == newDevice.ID);

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
                    // Device might have just disconnected
                    UnsubscribeDevice(device);
                    device = null;
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

        if (device != null)
        {
            try
            {
                device.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
                lastMuteState = device.AudioEndpointVolume.Mute;
            }
            catch
            {
                device = null;
            }
        }

        if (fireEventIfChanged)
        {
            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Safely gets the target capture device (specified or default).
    private MMDevice GetTargetAudioDevice()
    {
        if (string.IsNullOrEmpty(targetDeviceId))
        {
            try
            {
                return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch
            {
                return null;
            }
        }
        else
        {
            try
            {
                MMDevice specificDevice = deviceEnumerator.GetDevice(targetDeviceId);
                if (specificDevice != null && specificDevice.State == DeviceState.Active)
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

    private void UnsubscribeDevice(MMDevice dev)
    {
        if (dev != null)
        {
            try
            {
                dev.AudioEndpointVolume.OnVolumeNotification -= VolumeNotification;
            }
            catch { }
        }
    }

    // Handles volume/mute change notifications and triggers debouncing.
    private void VolumeNotification(AudioVolumeNotificationData data)
    {
        if (data.Muted != lastMuteState)
        {
            lastMuteState = data.Muted;
            debounceTimer.Stop();
            debounceTimer.Start();
        }
    }

    // Fires the AudioDeviceChanged event after the debounce delay.
    private void OnDebounceTimerElapsed(object sender, ElapsedEventArgs e)
    {
        AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region IMMNotificationClient Implementation

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
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

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        lock (syncLock)
        {
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        lock (syncLock)
        {
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    public void OnDeviceRemoved(string deviceId)
    {
        lock (syncLock)
        {
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property changes don't require re-attaching
    }

    #endregion
}