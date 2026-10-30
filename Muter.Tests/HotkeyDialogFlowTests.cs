using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Muter.UI.Controls;
using Xunit;

namespace Muter.Tests
{
    // The hotkey dialog: key capture, the inline validation and errors, the dialog keys and the tray flow that releases
    // the hotkey while a new one is being chosen.
    public sealed class HotkeyDialogFlowTests : IDisposable
    {
        private static readonly Hotkey Current = new Hotkey(Keys.F9, HotkeyModifiers.None);

        public HotkeyDialogFlowTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        private static void WithDialog(Action<HotkeyForm> test, Func<Hotkey, string> apply = null, Hotkey? current = null)
        {
            TestSetup.RunSta(() =>
            {
                using (var form = new HotkeyForm(current ?? Current, apply ?? (_ => null)))
                {
                    form.Show();
                    Application.DoEvents();
                    test(form);
                }
            });
        }

        [Fact]
        public void Opens_listening_and_shows_the_current_hotkey()
        {
            WithDialog(form =>
            {
                Assert.Equal("F9", form.CapturedText);
                Assert.Equal(KeyCaptureState.Idle, form.CaptureState);
                Assert.False(form.CanSave);
                Assert.StartsWith("Current hotkey", form.StatusText);
                Assert.Equal("Muter Hotkey", form.Text);
            });
        }

        [Fact]
        public void Shows_held_modifiers_then_captures_the_combination()
        {
            WithDialog(form =>
            {
                Assert.True(form.HandleKey(Keys.ControlKey | Keys.Control));
                Assert.True(form.HandleKey(Keys.ShiftKey | Keys.Control | Keys.Shift));
                Assert.Equal(KeyCaptureState.Pending, form.CaptureState);
                Assert.Equal("Ctrl + Shift", form.CapturedText);
                Assert.False(form.CanSave);

                Assert.True(form.HandleKey(Keys.K | Keys.Control | Keys.Shift));
                Assert.Equal(KeyCaptureState.Valid, form.CaptureState);
                Assert.Equal("Ctrl + Shift + K", form.CapturedText);
                Assert.Equal(new Hotkey(Keys.K, HotkeyModifiers.Control | HotkeyModifiers.Shift), form.Candidate);
                Assert.True(form.CanSave);
                Assert.Equal("Press Save to use Ctrl + Shift + K.", form.StatusText);
            });
        }

        [Fact]
        public void Saving_a_valid_combination_applies_it_and_closes()
        {
            var applied = new List<Hotkey>();
            WithDialog(form =>
            {
                form.HandleKey(Keys.F7 | Keys.Control | Keys.Alt);
                form.Save();
                Assert.Equal(DialogResult.OK, form.DialogResult);
                Assert.False(form.Visible);
            }, h => { applied.Add(h); return null; });

            Assert.Equal(new[] { new Hotkey(Keys.F7, HotkeyModifiers.Control | HotkeyModifiers.Alt) }, applied);
        }

        [Fact]
        public void A_combination_that_blocks_typing_is_explained_and_cannot_be_saved()
        {
            int calls = 0;
            WithDialog(form =>
            {
                form.HandleKey(Keys.F | Keys.Shift);
                Assert.Equal(KeyCaptureState.Invalid, form.CaptureState);
                Assert.False(form.CanSave);
                Assert.StartsWith("Add Ctrl or Alt", form.StatusText);
                form.Save();
                Assert.True(form.Visible);

                form.HandleKey(Keys.F | Keys.Control | Keys.Shift); // fixed by adding Ctrl
                Assert.True(form.CanSave);
            }, h => { calls++; return null; });
            Assert.Equal(0, calls);
        }

        [Fact]
        public void A_combination_taken_by_another_app_keeps_the_dialog_open_with_the_reason()
        {
            WithDialog(form =>
            {
                form.HandleKey(Keys.F8 | Keys.Control);
                form.Save();
                Assert.True(form.Visible);
                Assert.Equal(DialogResult.None, form.DialogResult);
                Assert.Equal(KeyCaptureState.Invalid, form.CaptureState);
                Assert.Equal("Ctrl + F8 is already used by another app. Try another combination.", form.StatusText);
                Assert.False(form.CanSave);

                form.HandleKey(Keys.F7 | Keys.Control);
                Assert.True(form.CanSave);
            }, h => h.Key == Keys.F8 ? HotkeyText.Format(h) + " is already used by another app. Try another combination." : null);
        }

        [Fact]
        public void Choosing_the_current_hotkey_again_does_not_enable_save()
        {
            WithDialog(form =>
            {
                form.HandleKey(Keys.F9);
                Assert.False(form.CanSave);
                Assert.StartsWith("This is your current hotkey", form.StatusText);
            });
        }

        [Fact]
        public void Reset_to_default_offers_the_default_hotkey()
        {
            WithDialog(form =>
            {
                Invoke(form, "ResetButton_Click");
                Assert.Equal(Hotkey.Default, form.Candidate);
                Assert.Equal("Ctrl + Shift + F9", form.CapturedText);
                Assert.True(form.CanSave);
            });
            WithDialog(form =>
            {
                Invoke(form, "ResetButton_Click");
                Assert.False(form.CanSave); // already the default
            }, current: Hotkey.Default);
        }

        [Fact]
        public void Plain_dialog_keys_operate_the_dialog_but_with_ctrl_or_alt_they_are_captured()
        {
            WithDialog(form =>
            {
                Assert.False(form.HandleKey(Keys.Tab));
                Assert.False(form.HandleKey(Keys.Tab | Keys.Shift));
                Assert.False(form.HandleKey(Keys.Return));
                Assert.False(form.HandleKey(Keys.Escape));
                Assert.False(form.HandleKey(Keys.Space));
                Assert.False(form.HandleKey(Keys.F4 | Keys.Alt));
                Assert.Null(form.Candidate);

                Assert.True(form.HandleKey(Keys.Space | Keys.Control));
                Assert.Equal("Ctrl + Space", form.CapturedText);
                Assert.True(form.HandleKey(Keys.Return | Keys.Alt));
                Assert.Equal("Alt + Enter", form.CapturedText);
            });
        }

        [Fact]
        public void Enter_saves_and_escape_cancels()
        {
            var applied = new List<Hotkey>();
            WithDialog(form =>
            {
                form.HandleKey(Keys.F6 | Keys.Control);
                Assert.True(ProcessDialogKey(form, Keys.Return));
                Assert.Equal(DialogResult.OK, form.DialogResult);
            }, h => { applied.Add(h); return null; });
            Assert.Single(applied);

            WithDialog(form =>
            {
                ProcessDialogKey(form, Keys.Return); // nothing new chosen: Save is disabled
                Assert.True(form.Visible);
                Assert.True(ProcessDialogKey(form, Keys.Escape));
                Assert.Equal(DialogResult.Cancel, form.DialogResult);
                Assert.False(form.Visible);
            });
        }

        [Fact]
        public void Real_key_messages_reach_the_capture_logic()
        {
            // Posted WM_KEYDOWN/WM_SYSKEYDOWN with the thread's key state set: the same path as a real keyboard,
            // without touching the user's keyboard or foreground window.
            var applied = new List<Hotkey>();
            WithDialog(form =>
            {
                Control box = (Control)typeof(HotkeyForm).GetField("captureBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);

                PressViaMessages(box, Keys.K, Keys.Control | Keys.Alt);
                Assert.Equal("Ctrl + Alt + K", form.CapturedText);
                Assert.True(form.CanSave);

                PressViaMessages(box, Keys.F, Keys.Shift);
                Assert.Equal(KeyCaptureState.Invalid, form.CaptureState);

                PressViaMessages(box, Keys.F10, Keys.Alt); // F10 and Alt combinations arrive as system keys
                Assert.Equal("Alt + F10", form.CapturedText);

                PressViaMessages(box, Keys.Return, Keys.None); // Enter = Save
                Assert.Equal(DialogResult.OK, form.DialogResult);
            }, h => { applied.Add(h); return null; });
            Assert.Equal(new[] { new Hotkey(Keys.F10, HotkeyModifiers.Alt) }, applied);

            WithDialog(form =>
            {
                Control box = (Control)typeof(HotkeyForm).GetField("captureBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                PressViaMessages(box, Keys.Escape, Keys.None); // Esc = Cancel
                Assert.Equal(DialogResult.Cancel, form.DialogResult);
                Assert.False(form.Visible);
            });
        }

        private static void PressViaMessages(Control target, Keys key, Keys modifiers)
        {
            const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
            var state = new byte[256];
            state[(int)Keys.ControlKey] = (byte)((modifiers & Keys.Control) != 0 ? 0x80 : 0);
            state[(int)Keys.ShiftKey] = (byte)((modifiers & Keys.Shift) != 0 ? 0x80 : 0);
            state[(int)Keys.Menu] = (byte)((modifiers & Keys.Alt) != 0 ? 0x80 : 0);
            state[(int)key] = 0x80;
            Assert.True(SetKeyboardState(state));

            bool system = (modifiers & Keys.Alt) != 0 || key == Keys.F10;
            long context = (modifiers & Keys.Alt) != 0 ? 1L << 29 : 0;
            IntPtr handle = target.Handle; // the key may close the dialog (Enter, Esc)
            PostMessage(handle, system ? WM_SYSKEYDOWN : WM_KEYDOWN, (IntPtr)(int)key, (IntPtr)(1 | context));
            TestSetup.PumpUntil(() => false, 50);
            PostMessage(handle, system ? WM_SYSKEYUP : WM_KEYUP, (IntPtr)(int)key, (IntPtr)(1 | context | (3L << 30)));
            TestSetup.PumpUntil(() => false, 50);

            Assert.True(SetKeyboardState(new byte[256]));
            TestSetup.PumpUntil(() => false, 20);
        }

        [DllImport("user32.dll")]
        private static extern bool SetKeyboardState(byte[] state);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [Fact]
        public void Renders_every_state_without_errors()
        {
            WithDialog(form =>
            {
                foreach (Keys keys in new[] { Keys.ControlKey | Keys.Control, Keys.K | Keys.Control, Keys.F | Keys.Shift, Keys.Next | Keys.Control | Keys.Alt | Keys.Shift })
                {
                    form.HandleKey(keys);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                }
            });
        }

        [Fact]
        public void Tray_flow_releases_the_hotkey_while_choosing_and_saves_only_successful_changes()
        {
            var hotkey = new FakeGlobalHotkey { Taken = new Hotkey(Keys.F8, HotkeyModifiers.Control) };
            bool releasedWhileOpen = false;
            TestSetup.RunSta(() =>
            {
                var provider = new FakeAudioEndpointProvider();
                provider.Add("a");
                provider.SetDefault("a");
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;

                using (var app = new TrayApplicationContext(settings, () => provider, hotkey, new StartupRegistration(Application.ExecutablePath, @"Software\MuterTests\HotkeyRun")))
                {
                    Assert.True(hotkey.IsRegistered);

                    // Open the dialog from the settings window, check the hotkey is released, then close the dialog window.
                    Exception closerFailure = null;
                    var closer = new Thread(() =>
                    {
                        try
                        {
                            AutomationElement dialog = WaitFor("Muter Hotkey");
                            releasedWhileOpen = !hotkey.IsRegistered;
                            ((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
                        }
                        catch (Exception ex)
                        {
                            closerFailure = ex;
                            // Never leave the modal dialog on screen.
                            foreach (Form open in Application.OpenForms.Cast<Form>().Where(f => f is HotkeyForm).ToList())
                                open.BeginInvoke(new Action(open.Close));
                        }
                    });
                    closer.Start();
                    SettingsForm settingsWindow = app.OpenSettings();
                    settingsWindow.ChangeHotkeyButton.PerformClick(); // runs the dialog modally
                    closer.Join(10000);
                    Assert.Null(closerFailure);
                    Assert.True(hotkey.IsRegistered, "restored after closing without a change");
                    Assert.Equal(new Hotkey(Keys.F9, HotkeyModifiers.None), hotkey.Current);

                    // The callback the dialog uses: a taken combination is rejected and nothing is saved.
                    MethodInfo apply = typeof(TrayApplicationContext).GetMethod("TryApplyHotkey", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.Contains("already used", (string)apply.Invoke(app, new object[] { new Hotkey(Keys.F8, HotkeyModifiers.Control) }));
                    Assert.Equal((int)Keys.F9, settings.hotkeyKey);

                    Assert.Null(apply.Invoke(app, new object[] { new Hotkey(Keys.F7, HotkeyModifiers.Control | HotkeyModifiers.Shift) }));
                    Assert.Equal((int)Keys.F7, settings.hotkeyKey);
                    Assert.Equal((int)(HotkeyModifiers.Control | HotkeyModifiers.Shift), settings.hotkeyModifiers);
                    Assert.Equal(new Hotkey(Keys.F7, HotkeyModifiers.Control | HotkeyModifiers.Shift), hotkey.Current);
                    Assert.Equal("Ctrl + Shift + F7", string.Join(" + ", settingsWindow.HotkeyKeys.Keycaps)); // the open settings window follows
                }
            });
            Assert.True(releasedWhileOpen, "the hotkey must be released while a new one is being chosen");
        }

        private static bool ProcessDialogKey(Form form, Keys keyData)
        {
            MethodInfo method = typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)method.Invoke(form, new object[] { keyData });
        }

        private static void Invoke(Form form, string handler)
        {
            typeof(HotkeyForm).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { form, EventArgs.Empty });
        }

        private static AutomationElement WaitFor(string windowName)
        {
            int pid = Process.GetCurrentProcess().Id;
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                new PropertyCondition(AutomationElement.NameProperty, windowName));
            for (int i = 0; i < 100; i++)
            {
                // Owned windows (a dialog over the settings window) are children of their owner in UI Automation.
                AutomationElement found = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
                if (found != null)
                    return found;
                foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
                {
                    found = window.FindFirst(TreeScope.Descendants, condition);
                    if (found != null)
                        return found;
                }
                Thread.Sleep(50);
            }
            throw new TimeoutException($"Window '{windowName}' did not appear");
        }
    }
}
