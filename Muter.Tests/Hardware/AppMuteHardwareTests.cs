using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Muter.Audio;
using Muter.UI;
using Xunit;

namespace Muter.Tests.Hardware
{
    // The real per-app sessions of Windows (Core Audio), on this computer's speakers. Only the test's own session is
    // muted and unmuted: it opens a stream with the sound output Muter uses for its feedback sounds, so nothing else (no
    // microphone, no other app) is touched.
    public sealed class AppMuteHardwareTests
    {
        private static readonly int Me = Process.GetCurrentProcess().Id;

        private static AppAudioSession[] MySessions()
        {
            var source = new CoreAudioSessionSource();
            try
            {
                return source.GetSessions().Where(s => s.ProcessId == Me).ToArray();
            }
            finally
            {
                source.Dispose();
            }
        }

        [Fact]
        public void The_session_of_the_app_in_front_is_muted_and_unmuted_for_real()
        {
            var output = new SoundOutput();
            output.Prepare(); // opens a stream: this process now has an audio session
            try
            {
                Assert.True(SpinWait.SpinUntil(() => MySessions().Length > 0, 5000), "the test process has an audio session");
                Assert.All(MySessions(), s => Assert.False(s.Muted));

                // As if this process were the app in front (Muter's own process id is a different one here).
                var muter = new AppMuter(ownProcessId: -1);
                Assert.Equal(AppMuteResult.Muted, muter.Toggle(Me));
                Assert.All(MySessions(), s => Assert.True(s.Muted));

                Assert.Equal(AppMuteResult.Unmuted, muter.Toggle(Me));
                Assert.All(MySessions(), s => Assert.False(s.Muted));
            }
            finally
            {
                // Never leave it muted.
                var source = new CoreAudioSessionSource();
                foreach (AppAudioSession session in source.GetSessions().Where(s => s.ProcessId == Me && s.Muted))
                    source.SetMute(session, false);
                source.Dispose();
                output.Release();
            }
        }

        [Fact]
        public void The_window_in_front_and_the_process_table_can_be_read()
        {
            var processes = ProcessTable.Snapshot();
            Assert.True(processes.Count > 10);
            Assert.True(processes.ContainsKey(Me));
            Assert.Equal(Process.GetCurrentProcess().ProcessName + ".exe", processes[Me].Name, StringComparer.OrdinalIgnoreCase);
            ForegroundApp.ProcessId(); // whatever is in front: must not throw
        }
    }
}
