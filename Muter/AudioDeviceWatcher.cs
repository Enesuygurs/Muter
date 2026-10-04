using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

// Monitors audio capture device(s) for mute state changes, device changes, and disconnections.
public class AudioDeviceWatcher : IMMNotificationClient
{
    public const string ALL_DEVICES_ID = "ALL_DEVICES";

    #region Fields and Events

    private readonly MMDeviceEnumerator deviceEnumerator;
    private MMDevice device;
    private string currentDeviceId;
    private readonly List<MMDevice> allDevices = new List<MMDevice>();
    private string targetDeviceId;
    private bool lastMuteState;
    private readonly Timer debounceTimer;
    private readonly object syncLock = new object();
    private bool isWatching = false;

    // Fired when the audio device's mute state or active device has changed.
    public event EventHandler AudioDeviceChanged;

    public bool IsAllDevicesMode
    {
        get
        {
            lock (syncLock)
            {
                return targetDeviceId == ALL_DEVICES_ID;
            }
        }
    }

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

    public bool HasActiveDevices
    {
        get
        {
            lock (syncLock)
            {
                if (targetDeviceId == ALL_DEVICES_ID)
                {
                    return allDevices.Count > 0 && allDevices.Any(IsDeviceAlive);
                }
                return device != null && IsDeviceAlive(device);
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (syncLock)
            {
                if (targetDeviceId == ALL_DEVICES_ID)
                {
                    if (allDevices.Count == 0) return false;
                    foreach (var d in allDevices)
                    {
                        try
                        {
                            if (!d.AudioEndpointVolume.Mute) return false;
                        }
                        catch { }
                    }
                    return true;
                }
                else
                {
                    try
                    {
                        return device != null && device.AudioEndpointVolume.Mute;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }
    }

    public string DeviceDisplayName
    {
        get
        {
            lock (syncLock)
            {
                if (targetDeviceId == ALL_DEVICES_ID)
                {
                    return "All Devices";
                }
                if (device != null)
                {
                    try
                    {
                        return device.FriendlyName;
                    }
                    catch { }
                }
                return "Microphone";
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
            UnsubscribeAllDevicesList();
            debounceTimer?.Stop();
        }
    }

    // Switches the targeted audio device (null or empty string means system default; ALL_DEVICES_ID means all devices).
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

    // Toggles mute state for target device(s).
    public void ToggleMute()
    {
        lock (syncLock)
        {
            if (targetDeviceId == ALL_DEVICES_ID)
            {
                bool anyUnmuted = false;
                foreach (var d in allDevices)
                {
                    try
                    {
                        if (!d.AudioEndpointVolume.Mute)
                        {
                            anyUnmuted = true;
                            break;
                        }
                    }
                    catch { }
                }

                bool targetMute = anyUnmuted; // If any mic is live, mute all. If all mics are muted, unmute all.
                foreach (var d in allDevices)
                {
                    try
                    {
                        d.AudioEndpointVolume.Mute = targetMute;
                    }
                    catch { }
                }
                lastMuteState = targetMute;
            }
            else
            {
                if (device != null && IsDeviceAlive(device))
                {
                    device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
                    lastMuteState = device.AudioEndpointVolume.Mute;
                }
            }
        }
    }

    #endregion

    #region Private Methods & Event Handlers

    private void RefreshDeviceInternal(bool fireEventIfChanged)
    {
        try
        {
            if (targetDeviceId == ALL_DEVICES_ID)
            {
                RefreshAllDevicesInternal(fireEventIfChanged);
            }
            else
            {
                RefreshSingleDeviceInternal(fireEventIfChanged);
            }
        }
        catch
        {
            UnsubscribeDevice(device);
            device = null;
            currentDeviceId = null;
            UnsubscribeAllDevicesList();
            if (fireEventIfChanged)
            {
                AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void RefreshSingleDeviceInternal(bool fireEventIfChanged)
    {
        UnsubscribeAllDevicesList();

        MMDevice newDevice = GetTargetAudioDevice();
        string newId = SafeGetDeviceId(newDevice);

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
            if (newDevice != null && newDevice != device)
            {
                try { newDevice.Dispose(); } catch { }
            }

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

    private void RefreshAllDevicesInternal(bool fireEventIfChanged)
    {
        UnsubscribeDevice(device);
        device = null;
        currentDeviceId = null;

        List<MMDevice> newDeviceList = new List<MMDevice>();
        try
        {
            MMDeviceCollection endpoints = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            if (endpoints != null)
            {
                for (int i = 0; i < endpoints.Count; i++)
                {
                    try
                    {
                        MMDevice ep = endpoints[i];
                        if (IsDeviceAlive(ep))
                        {
                            newDeviceList.Add(ep);
                        }
                        else
                        {
                            try { ep.Dispose(); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        List<string> oldIds = new List<string>();
        foreach (var d in allDevices)
        {
            string id = SafeGetDeviceId(d);
            if (!string.IsNullOrEmpty(id)) oldIds.Add(id);
        }

        List<string> newIds = new List<string>();
        foreach (var d in newDeviceList)
        {
            string id = SafeGetDeviceId(d);
            if (!string.IsNullOrEmpty(id)) newIds.Add(id);
        }

        bool listsEqual = (oldIds.Count == newIds.Count) && !oldIds.Except(newIds).Any();

        if (listsEqual && allDevices.Count > 0)
        {
            foreach (var d in newDeviceList)
            {
                try { d.Dispose(); } catch { }
            }

            bool anyUnmuted = false;
            foreach (var d in allDevices)
            {
                try
                {
                    if (!d.AudioEndpointVolume.Mute)
                    {
                        anyUnmuted = true;
                        break;
                    }
                }
                catch { }
            }
            bool currentMute = !anyUnmuted;
            if (currentMute != lastMuteState)
            {
                lastMuteState = currentMute;
                if (fireEventIfChanged)
                {
                    AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            return;
        }

        UnsubscribeAllDevicesList();
        allDevices.AddRange(newDeviceList);

        bool anyUnmutedNew = false;
        for (int i = allDevices.Count - 1; i >= 0; i--)
        {
            var d = allDevices[i];
            try
            {
                d.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
                if (!d.AudioEndpointVolume.Mute)
                {
                    anyUnmutedNew = true;
                }
            }
            catch
            {
                UnsubscribeDevice(d);
                allDevices.RemoveAt(i);
            }
        }

        lastMuteState = (allDevices.Count > 0) ? !anyUnmutedNew : false;

        if (fireEventIfChanged)
        {
            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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

    private void UnsubscribeAllDevicesList()
    {
        foreach (var dev in allDevices)
        {
            UnsubscribeDevice(dev);
        }
        allDevices.Clear();
    }

    private void VolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        }
        catch { }
    }

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