using NAudio.CoreAudioApi;
using System;
using System.Timers;

// Monitors the default audio capture device for mute state changes.
public class AudioDeviceWatcher
{
    #region Fields and Events

    private readonly MMDeviceEnumerator deviceEnumerator;
    private MMDevice device;
    private bool lastMuteState;
    private readonly Timer debounceTimer;

    // Fired when the audio device's mute state has changed.
    public event EventHandler AudioDeviceChanged;

    #endregion

    #region Constructor

    // Initializes the audio device watcher.
    public AudioDeviceWatcher()
    {
        deviceEnumerator = new MMDeviceEnumerator();
        device = GetDefaultAudioDevice();

        if (device != null)
        {
            lastMuteState = device.AudioEndpointVolume.Mute;
        }

        debounceTimer = new Timer(100) { AutoReset = false };
        debounceTimer.Elapsed += OnDebounceTimerElapsed;
    }

    #endregion

    #region Public Methods

    // Subscribes to device notifications to start monitoring.
    public void StartWatching()
    {
        RefreshDevice();
        device.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
    }

    // Unsubscribes from device notifications to stop monitoring.
    public void StopWatching()
    {
        device.AudioEndpointVolume.OnVolumeNotification -= VolumeNotification;
        debounceTimer?.Stop();
    }

    // Refreshes the audio device reference if the system's default device has changed.
    public void RefreshDevice()
    {
        MMDevice newDevice = GetDefaultAudioDevice();

        if (newDevice == null || (device != null && newDevice.ID == device.ID)) return;

        if (device != null)
        {
            device.AudioEndpointVolume.OnVolumeNotification -= VolumeNotification;
        }

        device = newDevice;
        device.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
        lastMuteState = device.AudioEndpointVolume.Mute;
        AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Private Methods & Event Handlers

    // Safely gets the default audio capture device.
    private MMDevice GetDefaultAudioDevice()
    {
        try
        {
            return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch (Exception)
        {
            return null;
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
    private void OnDebounceTimerElapsed(object sender, ElapsedEventArgs e) => AudioDeviceChanged?.Invoke(this, EventArgs.Empty);

    #endregion
}