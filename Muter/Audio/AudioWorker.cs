using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Muter.Audio
{
    // Why a status was produced.
    internal enum StatusSource
    {
        Refresh,    // a poll or a notification
        Setting,    // a settings change (target devices, sync)
        MuteAction, // the user muted or unmuted (hotkey, tray click)
    }

    // Runs every Core Audio call on one dedicated thread, so a slow or hung driver (e.g. a Bluetooth headset switching
    // profiles) can never freeze the UI: the UI keeps running and shows the state as unknown while a call is stuck.
    // The provider and the controller are created and used on this thread only.
    internal sealed class AudioWorker : IDisposable
    {
        private const int DeviceChangeFollowUpMs = 500;

        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly Func<IAudioEndpointProvider> providerFactory;
        private readonly Thread thread;
        private readonly Timer followUpTimer;
        private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);
        private IAudioEndpointProvider provider;
        private MicrophoneController controller;
        private string targetDeviceId;
        private bool syncDevices;
        private int refreshQueued;
        private long busySince;
        private volatile bool disposed;

        public AudioWorker(Func<IAudioEndpointProvider> providerFactory, string targetDeviceId, bool syncDevices)
        {
            this.providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            this.targetDeviceId = targetDeviceId;
            this.syncDevices = syncDevices;
            followUpTimer = new Timer(_ => RequestRefresh(), null, Timeout.Infinite, Timeout.Infinite);
            thread = new Thread(Run) { IsBackground = true, Name = "Muter audio" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        // Raised on the worker thread after every operation: the fresh status and what produced it.
        public event Action<MicrophoneStatus, StatusSource> StatusAvailable;

        // How long the current operation has been running (zero when idle).
        public TimeSpan BusyFor
        {
            get
            {
                long since = Interlocked.Read(ref busySince);
                return since == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - since) / Stopwatch.Frequency);
            }
        }

        public void RequestRefresh()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0)
                return;
            if (!Enqueue(() =>
            {
                Interlocked.Exchange(ref refreshQueued, 0);
                Report(Controller()?.Refresh(), StatusSource.Refresh);
            }))
            {
                Interlocked.Exchange(ref refreshQueued, 0);
            }
        }

        // Devices may have changed (e.g. after sleep): re-subscribe to notifications and re-read.
        public void NotifyDevicesChanged()
        {
            Enqueue(() => controller?.NotifyDevicesChanged());
            RequestRefresh();
        }

        // Every call is one toggle; presses are never merged.
        public void Toggle() => Enqueue(() => Report(Controller()?.ToggleMute(), StatusSource.MuteAction));

        public void SetTarget(string deviceId) => Enqueue(() =>
        {
            targetDeviceId = deviceId;
            Report(Controller()?.SetTarget(deviceId), StatusSource.Setting);
        });

        public void SetSync(bool enabled) => Enqueue(() =>
        {
            syncDevices = enabled;
            MicrophoneController c = Controller();
            if (c != null)
                c.SyncDevices = enabled;
            Report(c?.Refresh(), StatusSource.Setting);
        });

        // Lists the microphones for the menu. Throws TimeoutException if the audio system does not answer in time.
        public IReadOnlyList<AudioEndpointInfo> GetDevices(int timeoutMs)
        {
            IReadOnlyList<AudioEndpointInfo> result = null;
            Exception failure = null;
            using (var done = new ManualResetEventSlim())
            {
                bool queued = Enqueue(() =>
                {
                    try
                    {
                        result = Controller()?.GetCaptureDevices() ?? throw new AudioDeviceException("The audio system is not available");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        // The caller may have given up and disposed the event already.
                        try { done.Set(); } catch (ObjectDisposedException) { }
                    }
                });
                if (!queued || !done.Wait(timeoutMs))
                    throw new TimeoutException("The audio system did not answer in time.");
            }
            if (failure != null)
                throw failure;
            return result;
        }

        // For tests: waits until every queued operation has finished.
        public bool WaitIdle(int timeoutMs)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int quietChecks = 0;
            while (DateTime.UtcNow < end)
            {
                // Two quiet checks in a row: an item taken from the queue a moment ago has certainly started by then.
                quietChecks = queue.Count == 0 && idle.IsSet ? quietChecks + 1 : 0;
                if (quietChecks >= 2)
                    return true;
                Thread.Sleep(15);
            }
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            followUpTimer.Dispose();
            queue.CompleteAdding();
            // A hung driver call must not block exit; the thread is a background thread.
            if (!thread.Join(2000))
                Log.Warn("The audio thread did not stop in time");
        }

        private bool Enqueue(Action action)
        {
            if (disposed)
                return false;
            try
            {
                queue.Add(action);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // shutting down
            }
        }

        private void Run()
        {
            try
            {
                foreach (Action action in queue.GetConsumingEnumerable())
                {
                    idle.Reset();
                    Interlocked.Exchange(ref busySince, Stopwatch.GetTimestamp());
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Audio operation failed", ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref busySince, 0);
                        idle.Set();
                    }
                }
            }
            finally
            {
                DisposeProvider();
            }
        }

        // Creates the provider on first use; if the audio system cannot be opened, retries on the next operation.
        private MicrophoneController Controller()
        {
            if (controller != null)
                return controller;
            try
            {
                provider = providerFactory();
                provider.DevicesChanged += OnDevicesChanged;
                provider.MuteStateChanged += OnMuteStateChanged;
                controller = new MicrophoneController(provider, targetDeviceId, syncDevices);
                return controller;
            }
            catch (Exception ex)
            {
                Log.Error("The Windows audio system could not be opened", ex);
                DisposeProvider();
                return null;
            }
        }

        private void Report(MicrophoneStatus status, StatusSource source)
        {
            try
            {
                StatusAvailable?.Invoke(status ?? MicrophoneStatus.ReadFailure, source);
            }
            catch (Exception ex)
            {
                Log.Error("Reporting the microphone state failed", ex);
            }
        }

        // Windows threads: only queue work.
        private void OnDevicesChanged(object sender, EventArgs e)
        {
            NotifyDevicesChanged();
            // Devices often settle a moment after the notification (e.g. a new default being assigned).
            try
            {
                followUpTimer.Change(DeviceChangeFollowUpMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnMuteStateChanged(object sender, EventArgs e) => RequestRefresh();

        private void DisposeProvider()
        {
            if (provider == null)
                return;
            provider.DevicesChanged -= OnDevicesChanged;
            provider.MuteStateChanged -= OnMuteStateChanged;
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not release the audio devices", ex);
            }
            provider = null;
            controller = null;
        }
    }
}
