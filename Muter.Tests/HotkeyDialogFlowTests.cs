using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // The hotkey dialog: key capture logic, the Set Key flow (including the "already used" error) and the tray flow
    // that releases the hotkey while the dialog is open. Message boxes are answered through UI Automation.
    public sealed class HotkeyDialogFlowTests : IDisposable
    {
        public HotkeyDialogFlowTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        [Fact]
        public void Captures_combinations_ignores_lone_modifiers_and_applies_the_choice()
        {
            var applied = new List<Hotkey>();
            List<string> boxes = WithMessageBoxCloser(() => TestSetup.RunSta(() =>
            {
                using (var form = new HotkeyForm(new Hotkey(Keys.F9, HotkeyModifiers.None), h => { applied.Add(h); return null; }))
                {
                    form.Show();
                    TextBox box = Field<TextBox>(form, "key");
                    Assert.Equal("F9", box.Text);

                    Invoke(form, "key_Click");
                    Assert.Equal("Press a key...", box.Text, ignoreCase: true);
                    Assert.True(PressKey(form, Keys.ShiftKey | Keys.Shift)); // modifier alone: keep listening
                    Assert.True(PressKey(form, Keys.ControlKey | Keys.Control | Keys.Alt));
                    Assert.Equal("Press a key...", box.Text, ignoreCase: true);
                    Assert.True(PressKey(form, Keys.K | Keys.Control | Keys.Alt));
                    Assert.Equal("CONTROL + ALT + K", box.Text);

                    // Keys that normally move focus or close dialogs are captured too.
                    Invoke(form, "key_Click");
                    Assert.True(PressKey(form, Keys.Tab | Keys.Shift));
                    Assert.Equal("SHIFT + TAB", box.Text);
                    Invoke(form, "key_Click");
                    Assert.True(PressKey(form, Keys.NumPad1 | Keys.Control));
                    Assert.Equal("CONTROL + NUMPAD1", box.Text);

                    Invoke(form, "setButton_Click");
                    Assert.Equal(DialogResult.OK, form.DialogResult);
                }
            }));

            Assert.Equal(new[] { new Hotkey(Keys.NumPad1, HotkeyModifiers.Control) }, applied);
            Assert.Equal(new[] { "Shortcut key changed to Control + NumPad1." }, boxes);
        }

        [Fact]
        public void Set_key_without_choosing_or_with_a_taken_combination_keeps_the_dialog_open()
        {
            int calls = 0;
            List<string> boxes = WithMessageBoxCloser(() => TestSetup.RunSta(() =>
            {
                using (var form = new HotkeyForm(new Hotkey(Keys.F9, HotkeyModifiers.None), h => { calls++; return "F8 is already used by another application."; }))
                {
                    form.Show();
                    Invoke(form, "key_Click");
                    Invoke(form, "setButton_Click"); // still waiting for a key
                    Assert.Equal(0, calls);

                    Assert.True(PressKey(form, Keys.F8));
                    Invoke(form, "setButton_Click");
                    Assert.Equal(1, calls);
                    Assert.Equal(DialogResult.None, form.DialogResult);
                    Assert.True(form.Visible);
                }
            }));

            Assert.Equal(2, boxes.Count);
            Assert.StartsWith("Please click the box", boxes[0]);
            Assert.Equal("F8 is already used by another application.", boxes[1]);
        }

        [Fact]
        public void Tray_flow_releases_the_hotkey_while_choosing_and_saves_only_successful_changes()
        {
            var hotkey = new FakeGlobalHotkey { Taken = new Hotkey(Keys.F8, HotkeyModifiers.None) };
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

                    // Open the dialog from the menu, check the hotkey is released, then close the dialog window.
                    var closer = new Thread(() =>
                    {
                        AutomationElement dialog = WaitFor("Hotkey");
                        releasedWhileOpen = !hotkey.IsRegistered;
                        ((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
                    });
                    closer.Start();
                    TrayApplicationContextTestsAccess.Click(app, "Hotkey");
                    closer.Join(10000);
                    Assert.True(hotkey.IsRegistered, "restored after closing without a change");
                    Assert.Equal(new Hotkey(Keys.F9, HotkeyModifiers.None), hotkey.Current);

                    // The callback the dialog uses: a taken combination is rejected and nothing is saved.
                    MethodInfo apply = typeof(TrayApplicationContext).GetMethod("TryApplyHotkey", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.Contains("already used", (string)apply.Invoke(app, new object[] { new Hotkey(Keys.F8, HotkeyModifiers.None) }));
                    Assert.Equal((int)Keys.F9, settings.hotkeyKey);

                    Assert.Null(apply.Invoke(app, new object[] { new Hotkey(Keys.F7, HotkeyModifiers.Control | HotkeyModifiers.Shift) }));
                    Assert.Equal((int)Keys.F7, settings.hotkeyKey);
                    Assert.Equal((int)(HotkeyModifiers.Control | HotkeyModifiers.Shift), settings.hotkeyModifiers);
                    Assert.Equal(new Hotkey(Keys.F7, HotkeyModifiers.Control | HotkeyModifiers.Shift), hotkey.Current);
                    Assert.StartsWith("Hotkey (Control + Shift + F7)", TrayItemText(app, "Hotkey"));
                }
            });
            Assert.True(releasedWhileOpen, "the hotkey must be released while a new one is being chosen");
        }

        private static string TrayItemText(TrayApplicationContext app, string prefix)
        {
            app.RebuildMenu();
            foreach (ToolStripItem item in app.Menu.Items)
            {
                if (item.Text.StartsWith(prefix, StringComparison.Ordinal))
                    return item.Text;
            }
            return null;
        }

        private static bool PressKey(Form form, Keys keyData)
        {
            var msg = new Message { Msg = 0x0100 };
            MethodInfo method = typeof(HotkeyForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)method.Invoke(form, new object[] { msg, keyData });
        }

        private static void Invoke(Form form, string handler)
        {
            typeof(HotkeyForm).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { form, EventArgs.Empty });
        }

        private static T Field<T>(Form form, string name) => (T)typeof(HotkeyForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);

        // Runs the action while a helper thread answers every message box of this process with OK, returning their texts.
        private static List<string> WithMessageBoxCloser(Action action)
        {
            var texts = new List<string>();
            var stop = new ManualResetEventSlim();
            var helper = new Thread(() =>
            {
                int pid = Process.GetCurrentProcess().Id;
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                    new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));
                while (!stop.IsSet)
                {
                    foreach (AutomationElement box in AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition))
                    {
                        AutomationElement text = box.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                        AutomationElement ok = box.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                        if (text == null || ok == null)
                            continue;
                        lock (texts)
                            texts.Add(text.Current.Name);
                        ((InvokePattern)ok.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        Thread.Sleep(200); // let the box close before searching again
                    }
                    Thread.Sleep(50);
                }
            });
            helper.Start();
            try
            {
                action();
            }
            finally
            {
                stop.Set();
                helper.Join(5000);
            }
            return texts;
        }

        private static AutomationElement WaitFor(string windowName)
        {
            int pid = Process.GetCurrentProcess().Id;
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                new PropertyCondition(AutomationElement.NameProperty, windowName));
            for (int i = 0; i < 100; i++)
            {
                AutomationElement found = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
                if (found != null)
                    return found;
                Thread.Sleep(50);
            }
            throw new TimeoutException($"Window '{windowName}' did not appear");
        }
    }
}
