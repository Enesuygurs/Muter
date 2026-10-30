using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Muter.Audio
{
    // IAudioEndpointProvider backed by the Windows MMDevice API. Every query opens the device fresh and releases it
    // immediately, so readings never come from a stale COM object. Callbacks only raise events; the caller re-reads.
    internal sealed class CoreAudioEndpointProvider : IAudioEndpointProvider
    {
        private readonly NotificationClient notificationClient;
        private readonly VolumeCallback volumeCallback;
        private readonly Dictionary<string, IAudioEndpointVolume> watchedVolumes = new Dictionary<string, IAudioEndpointVolume>(StringComparer.OrdinalIgnoreCase);
        private IMMDeviceEnumerator enumerator;
        private bool notificationsRegistered;
        private bool disposed;

        public CoreAudioEndpointProvider()
        {
            notificationClient = new NotificationClient(this);
            volumeCallback = new VolumeCallback(this);
            enumerator = CoreAudioInterop.CreateDeviceEnumerator();
            RegisterDeviceNotifications();
        }

        public event EventHandler DevicesChanged;

        public event EventHandler MuteStateChanged;

        public IReadOnlyList<AudioEndpointInfo> GetActiveCaptureEndpoints()
        {
            return WithEnumeratorRetry(EnumerateActiveCapture);
        }

        public string GetDefaultCaptureEndpointId(EndpointRole role)
        {
            return WithEnumeratorRetry(e =>
            {
                IMMDevice device = null;
                try
                {
                    int hr = e.GetDefaultAudioEndpoint(EDataFlow.Capture, ToERole(role), out device);
                    if (hr == CoreAudioInterop.E_NOTFOUND)
                        return null;
                    Check(hr, "GetDefaultAudioEndpoint", enumeratorFailure: true);
                    return GetId(device);
                }
                finally
                {
                    CoreAudioInterop.Release(device);
                }
            });
        }

        public bool GetMute(string deviceId)
        {
            return WithEndpointVolume(deviceId, volume =>
            {
                Check(volume.GetMute(out bool mute), "GetMute");
                return mute;
            });
        }

        public void SetMute(string deviceId, bool mute)
        {
            WithEndpointVolume(deviceId, volume =>
            {
                Guid context = Guid.Empty;
                Check(volume.SetMute(mute, ref context), "SetMute");
                return true;
            });
        }

        public void WatchMuteChanges(IEnumerable<string> deviceIds)
        {
            ThrowIfDisposed();

            // Always re-register: a device that was unplugged and plugged back keeps its id but invalidates the old
            // registration. Watching is only a speed-up; correctness comes from re-reading the devices.
            UnwatchAll();
            foreach (string id in new HashSet<string>(deviceIds, StringComparer.OrdinalIgnoreCase))
            {
                IMMDevice device = null;
                object volumeObject = null;
                try
                {
                    Check(enumerator.GetDevice(id, out device), "GetDevice", enumeratorFailure: true);
                    Guid iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                    Check(device.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out volumeObject), "Activate");
                    var volume = (IAudioEndpointVolume)volumeObject;
                    Check(volume.RegisterControlChangeNotify(volumeCallback), "RegisterControlChangeNotify");
                    watchedVolumes[id] = volume;
                    volumeObject = null; // now owned by watchedVolumes
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not watch mute changes of device {id}", ex);
                }
                finally
                {
                    CoreAudioInterop.Release(volumeObject);
                    CoreAudioInterop.Release(device);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            UnwatchAll();
            UnregisterDeviceNotifications();
            // Not released explicitly: the enumerator is a process-wide singleton whose wrapper may be shared.
            enumerator = null;
        }

        private static List<AudioEndpointInfo> EnumerateActiveCapture(IMMDeviceEnumerator e)
        {
            IMMDeviceCollection collection = null;
            try
            {
                Check(e.EnumAudioEndpoints(EDataFlow.Capture, CoreAudioInterop.DEVICE_STATE_ACTIVE, out collection), "EnumAudioEndpoints", enumeratorFailure: true);
                Check(collection.GetCount(out uint count), "IMMDeviceCollection.GetCount");

                var result = new List<AudioEndpointInfo>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        // A device that cannot be read must fail the whole query. Silently skipping it could make
                        // "all microphones muted" true while that microphone is actually live.
                        Check(collection.Item(i, out device), "IMMDeviceCollection.Item");
                        result.Add(new AudioEndpointInfo(GetId(device), TryGetFriendlyName(device)));
                    }
                    finally
                    {
                        CoreAudioInterop.Release(device);
                    }
                }
                return result;
            }
            finally
            {
                CoreAudioInterop.Release(collection);
            }
        }

        private T WithEndpointVolume<T>(string deviceId, Func<IAudioEndpointVolume, T> action)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("Device id is required.", nameof(deviceId));

            return WithEnumeratorRetry(e =>
            {
                IMMDevice device = null;
                object volumeObject = null;
                try
                {
                    Check(e.GetDevice(deviceId, out device), "GetDevice", enumeratorFailure: true);
                    Check(device.GetState(out uint state), "GetState");
                    if ((state & CoreAudioInterop.DEVICE_STATE_ACTIVE) == 0)
                        throw new AudioDeviceException($"Device is not active (state 0x{state:X})");

                    Guid iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                    Check(device.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out volumeObject), "Activate(IAudioEndpointVolume)");
                    return action((IAudioEndpointVolume)volumeObject);
                }
                finally
                {
                    CoreAudioInterop.Release(volumeObject);
                    CoreAudioInterop.Release(device);
                }
            });
        }

        // Runs an operation; if the enumerator itself failed (for example after the audio service restarted),
        // recreates it once and retries.
        private T WithEnumeratorRetry<T>(Func<IMMDeviceEnumerator, T> operation)
        {
            ThrowIfDisposed();
            try
            {
                return operation(enumerator);
            }
            catch (Exception ex) when (IsEnumeratorFailure(ex))
            {
                Log.Warn("Audio device enumerator failed; recreating it", ex);
                RecreateEnumerator();
                return operation(enumerator);
            }
        }

        private static bool IsEnumeratorFailure(Exception ex)
        {
            return (ex is AudioDeviceException ade && ade.EnumeratorFailure) || ex is COMException || ex is InvalidComObjectException;
        }

        // Releasing the wrapper first makes the next creation return a fresh object after an audio service restart.
        private void RecreateEnumerator()
        {
            UnregisterDeviceNotifications();
            CoreAudioInterop.Release(enumerator);
            enumerator = CoreAudioInterop.CreateDeviceEnumerator();
            RegisterDeviceNotifications();
        }

        private void RegisterDeviceNotifications()
        {
            int hr = enumerator.RegisterEndpointNotificationCallback(notificationClient);
            notificationsRegistered = hr == CoreAudioInterop.S_OK;
            if (!notificationsRegistered)
                Log.Warn($"Could not register for device notifications (HRESULT 0x{hr:X8}); relying on polling");
        }

        private void UnregisterDeviceNotifications()
        {
            if (!notificationsRegistered || enumerator == null)
                return;
            notificationsRegistered = false;
            try
            {
                enumerator.UnregisterEndpointNotificationCallback(notificationClient);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not unregister device notifications", ex);
            }
        }

        private void UnwatchAll()
        {
            foreach (var pair in watchedVolumes)
            {
                try
                {
                    pair.Value.UnregisterControlChangeNotify(volumeCallback);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not unregister mute notifications of device {pair.Key}", ex);
                }
                CoreAudioInterop.Release(pair.Value);
            }
            watchedVolumes.Clear();
        }

        private static string GetId(IMMDevice device)
        {
            Check(device.GetId(out IntPtr idPointer), "GetId");
            try
            {
                string id = Marshal.PtrToStringUni(idPointer);
                if (string.IsNullOrEmpty(id))
                    throw new AudioDeviceException("Device returned an empty id");
                return id;
            }
            finally
            {
                Marshal.FreeCoTaskMem(idPointer);
            }
        }

        private static string TryGetFriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            var value = new PropVariant();
            try
            {
                if (device.OpenPropertyStore(CoreAudioInterop.STGM_READ, out store) != CoreAudioInterop.S_OK || store == null)
                    return null;
                PropertyKey key = CoreAudioInterop.PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out value) != CoreAudioInterop.S_OK)
                    return null;
                return value.VarType == CoreAudioInterop.VT_LPWSTR && value.Data != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Data)
                    : null;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read a device name", ex);
                return null;
            }
            finally
            {
                CoreAudioInterop.PropVariantClear(ref value);
                CoreAudioInterop.Release(store);
            }
        }

        private static ERole ToERole(EndpointRole role)
        {
            switch (role)
            {
                case EndpointRole.Console: return ERole.Console;
                case EndpointRole.Multimedia: return ERole.Multimedia;
                case EndpointRole.Communications: return ERole.Communications;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static void Check(int hr, string operation, bool enumeratorFailure = false)
        {
            if (hr != CoreAudioInterop.S_OK)
                throw new AudioDeviceException(operation + " failed", hr, enumeratorFailure);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CoreAudioEndpointProvider));
        }

        private void RaiseDevicesChanged()
        {
            try
            {
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warn("DevicesChanged handler failed", ex);
            }
        }

        private void RaiseMuteStateChanged()
        {
            try
            {
                MuteStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warn("MuteStateChanged handler failed", ex);
            }
        }

        // Called by Windows on its own threads. Per the MMDevice API rules these must return quickly and must not
        // call back into the enumerator, so they only raise an event.
        [ComVisible(true)]
        private sealed class NotificationClient : IMMNotificationClient
        {
            private readonly CoreAudioEndpointProvider owner;

            public NotificationClient(CoreAudioEndpointProvider owner) => this.owner = owner;

            public int OnDeviceStateChanged(string deviceId, uint newState)
            {
                owner.RaiseDevicesChanged();
                return CoreAudioInterop.S_OK;
            }

            public int OnDeviceAdded(string deviceId)
            {
                owner.RaiseDevicesChanged();
                return CoreAudioInterop.S_OK;
            }

            public int OnDeviceRemoved(string deviceId)
            {
                owner.RaiseDevicesChanged();
                return CoreAudioInterop.S_OK;
            }

            public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
            {
                if (flow == EDataFlow.Capture || flow == EDataFlow.All)
                    owner.RaiseDevicesChanged();
                return CoreAudioInterop.S_OK;
            }

            public int OnPropertyValueChanged(string deviceId, PropertyKey key)
            {
                return CoreAudioInterop.S_OK;
            }
        }

        [ComVisible(true)]
        private sealed class VolumeCallback : IAudioEndpointVolumeCallback
        {
            private readonly CoreAudioEndpointProvider owner;

            public VolumeCallback(CoreAudioEndpointProvider owner) => this.owner = owner;

            public int OnNotify(IntPtr notifyData)
            {
                owner.RaiseMuteStateChanged();
                return CoreAudioInterop.S_OK;
            }
        }
    }
}
