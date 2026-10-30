using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Muter.Hotkeys
{
    // Push to talk: after the hotkey is pressed, watches the physical key and reports once when it is let go.
    //
    // It polls the key state on a thread-pool timer, so a busy or frozen UI thread cannot delay the release. Anything
    // that hides the key from it (a locked or switched desktop, an error reading the state) counts as released: when
    // in doubt the microphone is muted, never left on.
    internal sealed class KeyReleaseWatcher : IDisposable
    {
        public const int PollIntervalMs = 15;

        private readonly Func<Keys, bool> isKeyDown;
        private readonly Action released;
        private readonly object sync = new object();
        private readonly System.Threading.Timer timer;
        private Keys key;
        private bool watching;
        private bool disposed;

        // isKeyDown: tests replace the real key state. released runs on a thread-pool thread.
        public KeyReleaseWatcher(Action released, Func<Keys, bool> isKeyDown = null)
        {
            this.released = released ?? throw new ArgumentNullException(nameof(released));
            this.isKeyDown = isKeyDown ?? IsPhysicallyDown;
            timer = new System.Threading.Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool Watching
        {
            get
            {
                lock (sync)
                    return watching;
            }
        }

        public void Start(Keys key)
        {
            lock (sync)
            {
                if (disposed)
                    return;
                this.key = key & Keys.KeyCode;
                watching = true;
                timer.Change(PollIntervalMs, PollIntervalMs);
            }
        }

        // Stops watching without reporting a release. Returns whether it was watching.
        public bool Stop()
        {
            lock (sync)
            {
                bool was = watching;
                watching = false;
                if (!disposed)
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                return was;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
                watching = false;
                timer.Dispose();
            }
        }

        private void Check()
        {
            lock (sync)
            {
                if (!watching)
                    return;
                bool down;
                try
                {
                    down = isKeyDown(key);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read the push-to-talk key; treating it as released", ex);
                    down = false;
                }
                if (down)
                    return;
                watching = false;
                timer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            try
            {
                released();
            }
            catch (Exception ex)
            {
                Log.Error("Handling the push-to-talk release failed", ex);
            }
        }

        private static bool IsPhysicallyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
