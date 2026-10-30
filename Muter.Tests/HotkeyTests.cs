using System;
using System.Linq;
using System.Windows.Forms;
using Muter.Hotkeys;
using Xunit;

namespace Muter.Tests
{
    public class HotkeyTests
    {
        public HotkeyTests() => TestSetup.Init();

        [Theory]
        [InlineData("SHIFT + F", Keys.F, HotkeyModifiers.Shift)]
        [InlineData("Shift + F", Keys.F, HotkeyModifiers.Shift)]
        [InlineData("F9", Keys.F9, HotkeyModifiers.None)]
        [InlineData("CONTROL + NUMPAD1", Keys.NumPad1, HotkeyModifiers.Control)]
        [InlineData("CONTROL + ALT + OEMMINUS", Keys.OemMinus, HotkeyModifiers.Control | HotkeyModifiers.Alt)]
        [InlineData("control+alt+shift+pageup", Keys.PageUp, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
        [InlineData("SHIFT + ESCAPE", Keys.Escape, HotkeyModifiers.Shift)]
        [InlineData("SHIFT + SPACE", Keys.Space, HotkeyModifiers.Shift)]
        [InlineData("Ctrl + D1", Keys.D1, HotkeyModifiers.Control)]
        [InlineData("Ctrl + 1", Keys.D1, HotkeyModifiers.Control)]
        public void Parses_the_old_text_format_case_insensitively(string text, Keys key, HotkeyModifiers modifiers)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
            Assert.Equal(key, hotkey.Key);
            Assert.Equal(modifiers, hotkey.Modifiers);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("SHIFT + ")]
        [InlineData("SHIFT")]
        [InlineData("CONTROL")]
        [InlineData("Shift + ShiftKey")]
        [InlineData("Win + F")]
        [InlineData("Shift + NotAKey")]
        [InlineData("Shift + 65")]
        [InlineData("Shift + A, B")]
        [InlineData("Shift + LButton")]
        public void Rejects_invalid_text(string text)
        {
            Assert.False(Hotkey.TryParse(text, out _));
        }

        [Fact]
        public void Every_valid_key_round_trips_through_text_in_any_case()
        {
            Keys[] keys = Enum.GetValues(typeof(Keys)).Cast<Keys>().Where(Hotkey.IsValidKey).Distinct().ToArray();
            Assert.Contains(Keys.NumPad1, keys);
            foreach (Keys key in keys)
            {
                foreach (HotkeyModifiers modifiers in new[] { HotkeyModifiers.None, HotkeyModifiers.Shift, HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift })
                {
                    var hotkey = new Hotkey(key, modifiers);
                    string text = hotkey.ToString();
                    Assert.True(Hotkey.TryParse(text.ToUpperInvariant(), out Hotkey upper), text);
                    Assert.Equal(hotkey, upper);
                    Assert.True(Hotkey.TryParse(text.ToLowerInvariant(), out Hotkey lower), text);
                    Assert.Equal(hotkey, lower);
                }
            }
        }

        [Fact]
        public void Settings_values_are_validated()
        {
            Assert.True(Hotkey.TryFromSettings((int)Keys.F9, 0, out Hotkey f9));
            Assert.Equal(new Hotkey(Keys.F9, HotkeyModifiers.None), f9);
            Assert.False(Hotkey.TryFromSettings(0, 0, out _));
            Assert.False(Hotkey.TryFromSettings((int)Keys.ShiftKey, (int)HotkeyModifiers.Control, out _));
            Assert.False(Hotkey.TryFromSettings((int)Keys.F, 0x8, out _)); // unsupported modifier bit
            Assert.False(Hotkey.TryFromSettings((int)(Keys.F | Keys.Shift), 0, out _));
        }

        [Fact]
        public void Default_is_shift_f()
        {
            Assert.Equal("Shift + F", Hotkey.Default.ToString());
            Assert.True(Hotkey.Default.IsValid);
        }
    }
}
