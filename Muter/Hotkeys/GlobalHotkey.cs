using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Localization;

namespace Muter.Hotkeys
{
    public interface IGlobalHotkey : IDisposable
    {
        event EventHandler Pressed;

        // The configured hotkey (it may be temporarily unregistered while suspended).
        Hotkey Current { get; }

        bool IsRegistered { get; }

        // Registers a new hotkey. On failure the previous hotkey stays configured (and registered if it was).
        bool TrySet(Hotkey hotkey, out string error);

        // Temporarily releases the hotkey, e.g. while the user is picking a new one.
        void Suspend();

        // Registers Current again after Suspend. Returns false if another application took it meanwhile.
        bool Resume(out string error);
    }

    // System-wide hotkey registered on a hidden message-only window.
    internal sealed class GlobalHotkey : NativeWindow, IGlobalHotkey
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 1;
        private const uint MOD_NOREPEAT = 0x4000;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public GlobalHotkey()
        {
            CreateHandle(new CreateParams { Caption = "Muter hotkey", Parent = HWND_MESSAGE });
        }

        public event EventHandler Pressed;

        public Hotkey Current { get; private set; }

        public bool IsRegistered { get; private set; }

        public bool TrySet(Hotkey hotkey, out string error)
        {
            if (!hotkey.IsValid)
            {
                error = Strings.Current.HotkeyChooseValid;
                return false;
            }

            bool wasRegistered = IsRegistered;
            Unregister();
            if (Register(hotkey, out error))
            {
                Current = hotkey;
                return true;
            }

            if (wasRegistered && !Register(Current, out string restoreError))
                Log.Warn($"Could not restore the previous hotkey {Current}: {restoreError}");
            return false;
        }

        public void Suspend() => Unregister();

        public bool Resume(out string error)
        {
            error = null;
            if (IsRegistered)
                return true;
            if (!Current.IsValid)
            {
                error = Strings.Current.HotkeyNotConfigured;
                return false;
            }
            return Register(Current, out error);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                try
                {
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error("Handling the hotkey failed", ex);
                }
                return;
            }
            base.WndProc(ref m);
        }

        private bool Register(Hotkey hotkey, out string error)
        {
            if (RegisterHotKey(Handle, HotkeyId, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key))
            {
                IsRegistered = true;
                error = null;
                return true;
            }

            int code = Marshal.GetLastWin32Error();
            error = code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                ? string.Format(Strings.Current.HotkeyTaken, HotkeyText.Format(hotkey))
                : string.Format(Strings.Current.HotkeyRegisterFailed, HotkeyText.Format(hotkey), new Win32Exception(code).Message);
            Log.Warn(error);
            return false;
        }

        private void Unregister()
        {
            if (!IsRegistered)
                return;
            UnregisterHotKey(Handle, HotkeyId);
            IsRegistered = false;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
