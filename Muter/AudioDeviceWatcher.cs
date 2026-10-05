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

    private MMDeviceEnumerator deviceEnumerator;
    private bool isNotificationRegistered = false;
    private MMDevice device;
    private string currentDeviceId;
    private readonly List<MMDevice> allDevices = new List<MMDevice>();
    private string targetDeviceId;
    private bool lastMuteState;
    private readonly Timer debounceTimer;
    private readonly Timer volumeDebounceTimer;
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
                    return allDevices.Count > 0;
                }
                return device != null;
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
                            if (IsDeviceAlive(d) && !d.AudioEndpointVolume.Mute) return false;
                        }
                        catch { }
                    }
                    return true;
                }
                else
                {
                    try
                    {
                        return device != null && IsDeviceAlive(device) && device.AudioEndpointVolume.Mute;
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

        debounceTimer = new Timer(150) { AutoReset = false };
        debounceTimer.Elapsed += OnDebounceTimerElapsed;

        volumeDebounceTimer = new Timer(50) { AutoReset = false };
        volumeDebounceTimer.Elapsed += OnVolumeDebounceTimerElapsed;

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

            EnsureNotificationCallbackRegistered();
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

            if (isNotificationRegistered)
            {
                try
                {
                    deviceEnumerator?.UnregisterEndpointNotificationCallback(this);
                }
                catch { }
                isNotificationRegistered = false;
            }

            UnsubscribeDevice(device);
            device = null;
            currentDeviceId = null;
            UnsubscribeAllDevicesList();
            debounceTimer?.Stop();
            volumeDebounceTimer?.Stop();
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

    // Refreshes the audio device reference safely outside of notification callbacks.
    public void RefreshDevice()
    {
        lock (syncLock)
        {
            EnsureNotificationCallbackRegistered();
            RefreshDeviceInternal(fireEventIfChanged: true);
        }
    }

    private void EnsureNotificationCallbackRegistered()
    {
        if (isNotificationRegistered || !isWatching) return;
        try
        {
            if (deviceEnumerator == null)
                deviceEnumerator = new MMDeviceEnumerator();
            deviceEnumerator.RegisterEndpointNotificationCallback(this);
            isNotificationRegistered = true;
        }
        catch { }
    }

    // Toggles mute state for target device(s) and returns the new mute state.
    public bool ToggleMute()
    {
        lock (syncLock)
        {
            if (targetDeviceId == ALL_DEVICES_ID)
            {
                if (allDevices.Count == 0)
                {
                    RefreshAllDevicesInternal(fireEventIfChanged: false);
                }

                if (allDevices.Count == 0)
                {
                    return lastMuteState;
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
                return targetMute;
            }
            else
            {
                if (device == null)
                {
                    RefreshSingleDeviceInternal(fireEventIfChanged: false);
                }

                if (device != null)
                {
                    try
                    {
                        bool targetMute = !device.AudioEndpointVolume.Mute;
                        device.AudioEndpointVolume.Mute = targetMute;
                        lastMuteState = targetMute;
                        return targetMute;
                    }
                    catch { }
                }
                return lastMuteState;
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

        if (newDevice == null && device != null)
        {
            if (!IsDeviceAlive(device))
            {
                UnsubscribeDevice(device);
                device = null;
                currentDeviceId = null;
                if (fireEventIfChanged)
                {
                    AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            return;
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
                catch { }
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
            }
            catch { }

            try
            {
                lastMuteState = device.AudioEndpointVolume.Mute;
            }
            catch
            {
                lastMuteState = false;
            }
        }

        if (fireEventIfChanged)
        {
            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private MMDeviceCollection SafeEnumerateCaptureEndPoints()
    {
        try
        {
            if (deviceEnumerator == null)
                deviceEnumerator = new MMDeviceEnumerator();
            return deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        }
        catch
        {
            try
            {
                deviceEnumerator = new MMDeviceEnumerator();
                return deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            }
            catch
            {
                return null;
            }
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
            MMDeviceCollection endpoints = SafeEnumerateCaptureEndPoints();
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

        // If enumeration returned 0 devices, but we currently have devices, don't wipe out on a temporary glitch!
        if (newDeviceList.Count == 0 && allDevices.Count > 0)
        {
            return;
        }

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

        bool devicesUnchanged = (oldIds.Count == newIds.Count) && !oldIds.Except(newIds).Any();

        if (devicesUnchanged && allDevices.Count > 0)
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
        for (int i = 0; i < allDevices.Count; i++)
        {
            var d = allDevices[i];
            try
            {
                try
                {
                    d.AudioEndpointVolume.OnVolumeNotification += VolumeNotification;
                }
                catch { }

                try
                {
                    if (!d.AudioEndpointVolume.Mute)
                    {
                        anyUnmutedNew = true;
                    }
                }
                catch { }
            }
            catch { }
        }

        lastMuteState = (allDevices.Count > 0) ? !anyUnmutedNew : false;

        if (fireEventIfChanged)
        {
            AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private MMDevice GetTargetAudioDevice()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (deviceEnumerator == null)
                    deviceEnumerator = new MMDeviceEnumerator();

                if (string.IsNullOrEmpty(targetDeviceId))
                {
                    // 1. Try Communications role
                    try
                    {
                        MMDevice defaultEndpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        if (IsDeviceAlive(defaultEndpoint))
                        {
                            return defaultEndpoint;
                        }
                    }
                    catch { }

                    // 2. Try Console role (standard default recording device)
                    try
                    {
                        MMDevice defaultEndpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                        if (IsDeviceAlive(defaultEndpoint))
                        {
                            return defaultEndpoint;
                        }
                    }
                    catch { }

                    // 3. Try Multimedia role
                    try
                    {
                        MMDevice defaultEndpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                        if (IsDeviceAlive(defaultEndpoint))
                        {
                            return defaultEndpoint;
                        }
                    }
                    catch { }

                    // 4. Fallback: return first active capture endpoint
                    try
                    {
                        MMDeviceCollection endpoints = SafeEnumerateCaptureEndPoints();
                        if (endpoints != null && endpoints.Count > 0)
                        {
                            for (int i = 0; i < endpoints.Count; i++)
                            {
                                try
                                {
                                    MMDevice ep = endpoints[i];
                                    if (IsDeviceAlive(ep))
                                    {
                                        return ep;
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
            catch
            {
                try { deviceEnumerator = new MMDeviceEnumerator(); } catch { }
            }
        }
        return null;
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
            return (dev.State & DeviceState.Active) == DeviceState.Active;
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

    // Debounced refresh trigger - strictly non-blocking.
    private void TriggerDebouncedRefresh()
    {
        try
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        }
        catch { }
    }

    private void VolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            volumeDebounceTimer.Stop();
            volumeDebounceTimer.Start();
        }
        catch { }
    }

    // Handles volume/mute changes without expensive hardware re-enumeration.
    private void OnVolumeDebounceTimerElapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            lock (syncLock)
            {
                bool currentMute = IsMuted;
                if (currentMute != lastMuteState)
                {
                    lastMuteState = currentMute;
                    AudioDeviceChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch { }
    }

    // Executes on a ThreadPool worker thread safely outside of any OS/COM callback for hardware device changes.
    private void OnDebounceTimerElapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            RefreshDevice();
        }
        catch { }
    }

    #endregion

    #region IMMNotificationClient Implementation

    // In accordance with Windows MMDevice guidelines, IMMNotificationClient callbacks
    // must be non-blocking and must NEVER call EnumerateAudioEndPoints, release COM objects,
    // or wait on locks held across apartments to prevent system thread deadlocks.

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Capture)
        {
            TriggerDebouncedRefresh();
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        TriggerDebouncedRefresh();
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        TriggerDebouncedRefresh();
    }

    public void OnDeviceRemoved(string deviceId)
    {
        TriggerDebouncedRefresh();
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    #endregion
}