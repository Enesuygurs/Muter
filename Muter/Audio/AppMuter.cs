using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Muter.Audio
{
    // One app's audio session on one output device.
    internal sealed class AppAudioSession
    {
        public AppAudioSession(int processId, bool muted, object handle = null)
        {
            ProcessId = processId;
            Muted = muted;
            Handle = handle;
        }

        public int ProcessId { get; }

        public bool Muted { get; }

        // What the source needs to change it (the COM volume object for the real one).
        public object Handle { get; }
    }

    // The audio sessions of every output device. Sessions that ended and the Windows system sounds are left out.
    internal interface IAudioSessionSource
    {
        IReadOnlyList<AppAudioSession> GetSessions();

        void SetMute(AppAudioSession session, bool mute);
    }

    // A running process: its parent and its exe name.
    internal struct ProcessEntry
    {
        public ProcessEntry(int parentId, string name)
        {
            ParentId = parentId;
            Name = name ?? string.Empty;
        }

        public int ParentId { get; }

        public string Name { get; }
    }

    internal enum AppMuteResult
    {
        NoApp,      // nothing in front, or Muter itself
        NoSound,    // the app has no audio session (it never played anything)
        Muted,
        Unmuted,
    }

    // Mutes or unmutes the app in front. Apps such as browsers, Discord or Spotify play their sound from helper
    // processes, so the app is the process in front plus every process with the same exe name and every process
    // started by it. Toggle: if any of its sessions is audible, all are muted; if all are muted, all are unmuted.
    internal sealed class AppMuter
    {
        private readonly Func<IAudioSessionSource> sessions;
        private readonly Func<IReadOnlyDictionary<int, ProcessEntry>> processes;
        private readonly int ownProcessId;

        public AppMuter(Func<IAudioSessionSource> sessions = null, Func<IReadOnlyDictionary<int, ProcessEntry>> processes = null, int? ownProcessId = null)
        {
            this.sessions = sessions ?? (() => new CoreAudioSessionSource());
            this.processes = processes ?? ProcessTable.Snapshot;
            this.ownProcessId = ownProcessId ?? Process.GetCurrentProcess().Id;
        }

        public AppMuteResult Toggle(int foregroundProcessId)
        {
            if (foregroundProcessId <= 0 || foregroundProcessId == ownProcessId)
                return AppMuteResult.NoApp;

            HashSet<int> app = AppProcesses(foregroundProcessId, processes());
            app.Remove(ownProcessId);
            IAudioSessionSource source = sessions();
            try
            {
                List<AppAudioSession> mine = source.GetSessions().Where(s => app.Contains(s.ProcessId)).ToList();
                if (mine.Count == 0)
                    return AppMuteResult.NoSound;

                bool mute = mine.Any(s => !s.Muted);
                foreach (AppAudioSession session in mine)
                {
                    try
                    {
                        source.SetMute(session, mute);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Could not {(mute ? "mute" : "unmute")} an audio session of process {session.ProcessId}", ex);
                    }
                }
                return mute ? AppMuteResult.Muted : AppMuteResult.Unmuted;
            }
            finally
            {
                (source as IDisposable)?.Dispose();
            }
        }

        // The process in front, the processes with the same exe name and every process started by any of them.
        internal static HashSet<int> AppProcesses(int foregroundProcessId, IReadOnlyDictionary<int, ProcessEntry> processes)
        {
            var app = new HashSet<int> { foregroundProcessId };
            if (processes.TryGetValue(foregroundProcessId, out ProcessEntry front) && front.Name.Length > 0)
            {
                foreach (var pair in processes)
                {
                    if (string.Equals(pair.Value.Name, front.Name, StringComparison.OrdinalIgnoreCase))
                        app.Add(pair.Key);
                }
            }

            // Children, grandchildren... (a process id can be reused, so a loop in the parent chain is cut off).
            bool added = true;
            while (added)
            {
                added = false;
                foreach (var pair in processes)
                {
                    if (!app.Contains(pair.Key) && pair.Key != pair.Value.ParentId && app.Contains(pair.Value.ParentId) && pair.Value.ParentId != 0)
                        added |= app.Add(pair.Key);
                }
            }
            return app;
        }
    }

    // The real sessions, read through Core Audio on every active output device.
    internal sealed class CoreAudioSessionSource : IAudioSessionSource, IDisposable
    {
        private const uint CLSCTX_ALL = 0x17;
        private readonly List<object> held = new List<object>();

        // Releases the session objects that were read.
        public void Dispose()
        {
            foreach (object session in held)
                CoreAudioInterop.Release(session);
            held.Clear();
        }

        public IReadOnlyList<AppAudioSession> GetSessions()
        {
            var result = new List<AppAudioSession>();
            IMMDeviceEnumerator enumerator = CoreAudioInterop.CreateDeviceEnumerator();
            if (!CoreAudioInterop.Succeeded(enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudioInterop.DEVICE_STATE_ACTIVE, out IMMDeviceCollection devices)))
                return result;
            try
            {
                devices.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (!CoreAudioInterop.Succeeded(devices.Item(i, out IMMDevice device)))
                        continue;
                    try
                    {
                        AddSessions(device, result);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Could not read the audio sessions of an output device", ex);
                    }
                    finally
                    {
                        CoreAudioInterop.Release(device);
                    }
                }
            }
            finally
            {
                CoreAudioInterop.Release(devices);
            }
            return result;
        }

        public void SetMute(AppAudioSession session, bool mute)
        {
            int hr = ((ISimpleAudioVolume)session.Handle).SetMute(mute, IntPtr.Zero);
            if (!CoreAudioInterop.Succeeded(hr))
                throw new COMException("SetMute failed", hr);
        }

        private void AddSessions(IMMDevice device, List<AppAudioSession> result)
        {
            Guid iid = AudioSessionInterop.IID_IAudioSessionManager2;
            if (!CoreAudioInterop.Succeeded(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object managerObject)))
                return;
            var manager = (IAudioSessionManager2)managerObject;
            IAudioSessionEnumerator sessions = null;
            try
            {
                if (!CoreAudioInterop.Succeeded(manager.GetSessionEnumerator(out sessions)))
                    return;
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (!CoreAudioInterop.Succeeded(sessions.GetSession(i, out object sessionObject)))
                        continue;
                    var control = (IAudioSessionControl2)sessionObject;
                    bool keep = false;
                    try
                    {
                        // S_OK (0) means it is the system sounds session.
                        if (control.IsSystemSoundsSession() == 0)
                            continue;
                        if (CoreAudioInterop.Succeeded(control.GetState(out int state)) && state == AudioSessionInterop.AudioSessionStateExpired)
                            continue;
                        if (!CoreAudioInterop.Succeeded(control.GetProcessId(out uint processId)) || processId == 0)
                            continue;
                        var volume = (ISimpleAudioVolume)sessionObject;
                        if (!CoreAudioInterop.Succeeded(volume.GetMute(out bool muted)))
                            continue;
                        result.Add(new AppAudioSession((int)processId, muted, volume));
                        held.Add(sessionObject);
                        keep = true;
                    }
                    finally
                    {
                        if (!keep)
                            CoreAudioInterop.Release(sessionObject);
                    }
                }
            }
            finally
            {
                CoreAudioInterop.Release(sessions);
                CoreAudioInterop.Release(manager);
            }
        }
    }

    // Parent and exe name of every running process (one Toolhelp snapshot).
    internal static class ProcessTable
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        public static IReadOnlyDictionary<int, ProcessEntry> Snapshot()
        {
            var result = new Dictionary<int, ProcessEntry>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == InvalidHandle)
                return result;
            try
            {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                    result[(int)entry.th32ProcessID] = new ProcessEntry((int)entry.th32ParentProcessID, entry.szExeFile);
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
        private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
        private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }

    // The process of the window in front. Store apps run in ApplicationFrameHost, which hosts the app's own window.
    internal static class ForegroundApp
    {
        public static int ProcessId()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return 0;
            GetWindowThreadProcessId(window, out uint processId);
            if (IsFrameHost(processId))
            {
                uint hosted = 0;
                EnumChildWindows(window, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out uint childProcess);
                    if (childProcess != processId)
                    {
                        hosted = childProcess;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (hosted != 0)
                    processId = hosted;
            }
            return (int)processId;
        }

        private static bool IsFrameHost(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return string.Equals(process.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // For the log: "chrome.exe" or the process id.
        public static string Describe(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return process.ProcessName + ".exe";
            }
            catch (Exception)
            {
                return "process " + processId;
            }
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
    }
}
