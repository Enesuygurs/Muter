using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Muter.Hotkeys;
using Xunit;

namespace Muter.Tests
{
    // Registers a real system-wide hotkey (Ctrl+Shift+F24, which no keyboard has) and injects key presses.
    public class GlobalHotkeyTests
    {
        private static readonly Hotkey TestHotkey = new Hotkey(Keys.F24, HotkeyModifiers.Control | HotkeyModifiers.Shift);

        public GlobalHotkeyTests() => TestSetup.Init();

        [Fact]
        public void Registers_receives_presses_once_per_press_and_detects_conflicts()
        {
            TestSetup.RunSta(() =>
            {
                using (var first = new GlobalHotkey())
                using (var second = new GlobalHotkey())
                {
                    int pressed = 0;
                    first.Pressed += (s, e) => pressed++;

                    Assert.True(first.TrySet(TestHotkey, out string error), error);
                    Assert.True(first.IsRegistered);

                    // Another application already owns it.
                    Assert.False(second.TrySet(TestHotkey, out error));
                    Assert.Contains("already used", error);
                    Assert.True(first.IsRegistered);

                    // One press = one toggle, even with key auto-repeat (MOD_NOREPEAT).
                    SendCombo(repeats: 3);
                    Assert.True(TestSetup.PumpUntil(() => pressed >= 1, 2000));
                    TestSetup.PumpUntil(() => false, 300);
                    Assert.Equal(1, pressed);

                    SendCombo(repeats: 1);
                    Assert.True(TestSetup.PumpUntil(() => pressed >= 2, 2000));

                    // Suspended: the combination is free for others and presses are not received.
                    first.Suspend();
                    Assert.False(first.IsRegistered);
                    Assert.True(second.TrySet(TestHotkey, out error), error);
                    second.Suspend();
                    Assert.True(first.Resume(out error), error);

                    // Changing to an invalid hotkey keeps the old one registered.
                    Assert.False(first.TrySet(new Hotkey(Keys.ShiftKey, HotkeyModifiers.None), out _));
                    Assert.True(first.IsRegistered);
                    Assert.Equal(TestHotkey, first.Current);
                }
            });
        }

        private static void SendCombo(int repeats)
        {
            var inputs = new System.Collections.Generic.List<INPUT> { Key(Keys.ControlKey, false), Key(Keys.ShiftKey, false) };
            for (int i = 0; i < repeats; i++)
                inputs.Add(Key(Keys.F24, false));
            inputs.Add(Key(Keys.F24, true));
            inputs.Add(Key(Keys.ShiftKey, true));
            inputs.Add(Key(Keys.ControlKey, true));
            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            Assert.Equal((uint)inputs.Count, sent);
        }

        private static INPUT Key(Keys key, bool up) => new INPUT
        {
            type = 1,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = up ? 2u : 0u } },
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // Sized by MOUSEINPUT, the largest member, so sizeof(INPUT) matches Windows (40 bytes x64, 28 bytes x86).
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
