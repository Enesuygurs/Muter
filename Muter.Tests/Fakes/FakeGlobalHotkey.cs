using System;
using Muter.Hotkeys;

namespace Muter.Tests.Fakes
{
    internal sealed class FakeGlobalHotkey : IGlobalHotkey
    {
        public event EventHandler Pressed;

        public Hotkey Current { get; private set; }

        public bool IsRegistered { get; private set; }

        public bool Disposed { get; private set; }

        // Combinations "taken by another application".
        public Hotkey? Taken { get; set; }

        public bool TrySet(Hotkey hotkey, out string error)
        {
            if (Taken.HasValue && Taken.Value.Equals(hotkey))
            {
                error = $"{hotkey} is already used by another application.";
                return false;
            }
            Current = hotkey;
            IsRegistered = true;
            error = null;
            return true;
        }

        public void Suspend() => IsRegistered = false;

        public bool Resume(out string error)
        {
            error = null;
            IsRegistered = true;
            return true;
        }

        public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            Disposed = true;
            IsRegistered = false;
        }
    }
}
