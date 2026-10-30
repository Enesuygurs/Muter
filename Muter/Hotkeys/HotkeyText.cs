using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Localization;

namespace Muter.Hotkeys
{
    // Human-readable hotkey names ("Ctrl + Shift + F9") and the rules for which combinations can be chosen.
    public static class HotkeyText
    {
        private static readonly Dictionary<Keys, string> Names = new Dictionary<Keys, string>
        {
            [Keys.Back] = "Backspace",
            [Keys.Return] = "Enter",
            [Keys.Escape] = "Esc",
            [Keys.Space] = "Space",
            [Keys.Prior] = "Page Up",
            [Keys.Next] = "Page Down",
            [Keys.Delete] = "Del",
            [Keys.Insert] = "Ins",
            [Keys.Capital] = "Caps Lock",
            [Keys.NumLock] = "Num Lock",
            [Keys.Scroll] = "Scroll Lock",
            [Keys.Snapshot] = "Print Screen",
            [Keys.Apps] = "Menu",
            [Keys.Left] = "←",
            [Keys.Right] = "→",
            [Keys.Up] = "↑",
            [Keys.Down] = "↓",
            [Keys.Multiply] = "Num *",
            [Keys.Add] = "Num +",
            [Keys.Subtract] = "Num -",
            [Keys.Divide] = "Num /",
            [Keys.Decimal] = "Num .",
            [Keys.VolumeMute] = "Volume Mute",
            [Keys.VolumeDown] = "Volume Down",
            [Keys.VolumeUp] = "Volume Up",
            [Keys.MediaPlayPause] = "Play/Pause",
            [Keys.MediaNextTrack] = "Next Track",
            [Keys.MediaPreviousTrack] = "Previous Track",
            [Keys.MediaStop] = "Stop",
        };

        // "Ctrl + Shift + F9"
        public static string Format(Hotkey hotkey) => string.Join(" + ", Parts(hotkey));

        // The keycaps of a hotkey, modifiers first: { "Ctrl", "Shift", "F9" }.
        public static IReadOnlyList<string> Parts(Hotkey hotkey)
        {
            var parts = new List<string>(ModifierParts(hotkey.Modifiers));
            parts.Add(KeyName(hotkey.Key));
            return parts;
        }

        public static IReadOnlyList<string> ModifierParts(HotkeyModifiers modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            return parts;
        }

        public static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num " + (key - Keys.NumPad0);
            if (Names.TryGetValue(key, out string name))
                return name;
            if (IsOemKey(key))
            {
                // Punctuation keys differ per keyboard layout: show the character this keyboard prints.
                uint ch = MapVirtualKey((uint)key, MAPVK_VK_TO_CHAR) & 0x7FFF;
                if (ch > 32)
                    return char.ToUpper((char)ch).ToString();
            }
            return key.ToString();
        }

        // Null when the hotkey may be used; otherwise why not.
        public static string Problem(Hotkey hotkey)
        {
            if (!hotkey.IsValid)
                return Strings.Current.HotkeyInvalidKey;
            if (IsReservedBySystem(hotkey))
                return string.Format(Strings.Current.HotkeyReserved, Format(hotkey));
            bool ctrlOrAlt = (hotkey.Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) != 0;
            if (!ctrlOrAlt && !CanBeUsedWithoutCtrlOrAlt(hotkey.Key))
                return string.Format(Strings.Current.HotkeyNeedsCtrlOrAlt, Format(hotkey));
            return null;
        }

        // Keys that type nothing, so taking them over does not get in the way.
        public static bool CanBeUsedWithoutCtrlOrAlt(Keys key)
        {
            return (key >= Keys.F1 && key <= Keys.F24)
                || key == Keys.Pause
                || key == Keys.Scroll
                || (key >= Keys.BrowserBack && key <= Keys.LaunchApplication2);
        }

        private static bool IsReservedBySystem(Hotkey hotkey)
        {
            if (hotkey.Modifiers == HotkeyModifiers.Alt)
                return hotkey.Key == Keys.F4 || hotkey.Key == Keys.Space || hotkey.Key == Keys.Tab || hotkey.Key == Keys.Escape;
            if (hotkey.Modifiers == HotkeyModifiers.Control)
                return hotkey.Key == Keys.Escape;
            if (hotkey.Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Shift))
                return hotkey.Key == Keys.Escape;
            return false;
        }

        private static bool IsOemKey(Keys key) => (key >= Keys.OemSemicolon && key <= Keys.Oemtilde) || (key >= Keys.OemOpenBrackets && key <= Keys.OemBackslash);

        private const uint MAPVK_VK_TO_CHAR = 2;

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}
