using System.Windows.Forms;
using Muter.Hotkeys;
using Xunit;

namespace Muter.Tests
{
    public class HotkeyTextTests
    {
        public HotkeyTextTests() => TestSetup.Init();

        [Theory]
        [InlineData(Keys.F9, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl + Shift + F9")]
        [InlineData(Keys.K, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Ctrl + Alt + Shift + K")]
        [InlineData(Keys.D1, HotkeyModifiers.Alt, "Alt + 1")]
        [InlineData(Keys.NumPad7, HotkeyModifiers.Control, "Ctrl + Num 7")]
        [InlineData(Keys.Next, HotkeyModifiers.Control, "Ctrl + Page Down")]
        [InlineData(Keys.Escape, HotkeyModifiers.Shift, "Shift + Esc")]
        [InlineData(Keys.Up, HotkeyModifiers.Alt, "Alt + ↑")]
        [InlineData(Keys.F9, HotkeyModifiers.None, "F9")]
        public void Formats_readable_names(Keys key, HotkeyModifiers modifiers, string expected)
        {
            Assert.Equal(expected, HotkeyText.Format(new Hotkey(key, modifiers)));
        }

        [Fact]
        public void Punctuation_keys_show_a_character_of_the_current_keyboard_layout()
        {
            foreach (Keys key in new[] { Keys.OemMinus, Keys.Oemcomma, Keys.OemPeriod })
            {
                Assert.Single(HotkeyText.KeyName(key)); // not the enum name ("OemMinus")
            }
        }

        [Theory]
        [InlineData(Keys.F9, HotkeyModifiers.None)]
        [InlineData(Keys.F24, HotkeyModifiers.Shift)]
        [InlineData(Keys.Pause, HotkeyModifiers.None)]
        [InlineData(Keys.MediaPlayPause, HotkeyModifiers.None)]
        [InlineData(Keys.F, HotkeyModifiers.Control)]
        [InlineData(Keys.Space, HotkeyModifiers.Control)]
        [InlineData(Keys.Return, HotkeyModifiers.Alt)]
        [InlineData(Keys.M, HotkeyModifiers.Control | HotkeyModifiers.Shift)]
        public void Allows_combinations_that_do_not_get_in_the_way(Keys key, HotkeyModifiers modifiers)
        {
            Assert.Null(HotkeyText.Problem(new Hotkey(key, modifiers)));
        }

        [Theory]
        [InlineData(Keys.F, HotkeyModifiers.Shift)]
        [InlineData(Keys.F, HotkeyModifiers.None)]
        [InlineData(Keys.D5, HotkeyModifiers.None)]
        [InlineData(Keys.Space, HotkeyModifiers.Shift)]
        [InlineData(Keys.Return, HotkeyModifiers.None)]
        [InlineData(Keys.Left, HotkeyModifiers.None)]
        [InlineData(Keys.Delete, HotkeyModifiers.Shift)]
        [InlineData(Keys.OemMinus, HotkeyModifiers.None)]
        [InlineData(Keys.NumPad1, HotkeyModifiers.None)]
        public void Rejects_keys_that_would_block_typing(Keys key, HotkeyModifiers modifiers)
        {
            Assert.StartsWith("Add Ctrl or Alt", HotkeyText.Problem(new Hotkey(key, modifiers)));
        }

        [Theory]
        [InlineData(Keys.F4, HotkeyModifiers.Alt)]
        [InlineData(Keys.Space, HotkeyModifiers.Alt)]
        [InlineData(Keys.Tab, HotkeyModifiers.Alt)]
        [InlineData(Keys.Escape, HotkeyModifiers.Control)]
        [InlineData(Keys.Escape, HotkeyModifiers.Control | HotkeyModifiers.Shift)]
        public void Rejects_combinations_windows_uses(Keys key, HotkeyModifiers modifiers)
        {
            Assert.Contains("reserved by Windows", HotkeyText.Problem(new Hotkey(key, modifiers)));
        }

        [Fact]
        public void The_default_is_allowed_and_the_old_default_is_not()
        {
            Assert.Null(HotkeyText.Problem(Hotkey.Default));
            Assert.NotNull(HotkeyText.Problem(Hotkey.LegacyDefault));
            Assert.NotNull(HotkeyText.Problem(new Hotkey(Keys.ShiftKey, HotkeyModifiers.Control)));
        }
    }
}
