using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Muter.Hotkeys
{
    // Values match the RegisterHotKey MOD_* flags.
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
    }

    public struct Hotkey : IEquatable<Hotkey>
    {
        // A combination that cannot block typing in other applications (unlike a letter key).
        public static readonly Hotkey Default = new Hotkey(Keys.F9, HotkeyModifiers.Control | HotkeyModifiers.Shift);

        // What versions before 1.1 used when no hotkey had been saved; existing users keep it.
        public static readonly Hotkey LegacyDefault = new Hotkey(Keys.F, HotkeyModifiers.Shift);

        public Hotkey(Keys key, HotkeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public Keys Key { get; }

        public HotkeyModifiers Modifiers { get; }

        public bool IsValid => IsValidKey(Key) && (Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift)) == 0;

        // A key that can be the non-modifier part of a hotkey.
        public static bool IsValidKey(Keys key)
        {
            if (key == Keys.None || (key & Keys.Modifiers) != 0 || (key & Keys.KeyCode) != key)
                return false;

            switch (key)
            {
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.LButton:
                case Keys.RButton:
                case Keys.MButton:
                case Keys.XButton1:
                case Keys.XButton2:
                    return false;
                default:
                    return true;
            }
        }

        // Builds a hotkey from the values stored in settings; returns false if they do not form a valid hotkey.
        public static bool TryFromSettings(int key, int modifiers, out Hotkey hotkey)
        {
            hotkey = new Hotkey((Keys)key, (HotkeyModifiers)modifiers);
            return hotkey.IsValid;
        }

        // Parses the text format used by older versions ("SHIFT + F", "Control + Alt + NumPad1"), case-insensitively.
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] parts = text.Split('+');
            var modifiers = HotkeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToUpperInvariant())
                {
                    case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                    case "CONTROL":
                    case "CTRL": modifiers |= HotkeyModifiers.Control; break;
                    case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                    default: return false;
                }
            }

            string keyText = parts[parts.Length - 1].Trim();
            // Reject numbers and comma lists, which Enum.TryParse would otherwise turn into arbitrary key codes.
            if (keyText.Length == 0 || keyText.IndexOf(',') >= 0 || (char.IsDigit(keyText[0]) && keyText.Length > 1))
                return false;
            if (keyText.Length == 1 && char.IsDigit(keyText[0]))
                keyText = "D" + keyText;
            if (!Enum.TryParse(keyText, ignoreCase: true, result: out Keys key) || !Enum.IsDefined(typeof(Keys), key))
                return false;

            hotkey = new Hotkey(key, modifiers);
            return hotkey.IsValid;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Control");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join(" + ", parts);
        }

        public bool Equals(Hotkey other) => Key == other.Key && Modifiers == other.Modifiers;

        public override bool Equals(object obj) => obj is Hotkey other && Equals(other);

        public override int GetHashCode() => ((int)Key * 397) ^ (int)Modifiers;
    }
}
