using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.UI;
using Xunit;

namespace Muter.Tests.Hardware
{
    // Push to talk end to end: the real global hotkey, the real keyboard state and this computer's real microphones
    // (checked through NAudio). F13 is used: it is on very few keyboards, so the test does not get in anyone's way.
    [Collection("Hardware")]
    public sealed class PushToTalkHardwareTests : IDisposable
    {
        private const byte VK_F13 = 0x7C;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private readonly HardwareFixture hw;
        private readonly List<string> ids;

        public PushToTalkHardwareTests(HardwareFixture hw)
        {
            this.hw = hw;
            ids = hw.DeviceIds();
            Assert.True(ids.Count > 0, "These tests need at least one active microphone.");
        }

        public void Dispose()
        {
            keybd_event(VK_F13, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);
            hw.Restore();
        }

        private bool AllMuted() => ids.All(hw.Read);

        private bool AllLive() => ids.All(id => !hw.Read(id));

        [Fact]
        public void Holding_the_real_key_opens_the_real_microphones_and_letting_go_closes_them()
        {
            hw.SetAll(false);
            TestSetup.RunSta(() =>
            {
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F13;
                settings.hotkeyModifiers = 0;
                settings.selectedDeviceId = MicrophoneController.AllDevicesId;
                settings.pushToTalk = true;
                using (var app = new TrayApplicationContext(settings, () => new CoreAudioEndpointProvider(), new GlobalHotkey(),
                    new StartupRegistration(Application.ExecutablePath, @"Software\MuterTests\PttHardware"), TestSetup.Silent))
                {
                    Assert.True(app.WaitForAudio());
                    Assert.True(AllMuted(), "push to talk starts muted");

                    for (int round = 0; round < 5; round++)
                    {
                        keybd_event(VK_F13, 0, 0, UIntPtr.Zero);
                        Assert.True(TestSetup.PumpUntil(AllLive, 3000), $"round {round}: live while held");
                        TestSetup.PumpUntil(() => false, 250);
                        Assert.True(AllLive(), $"round {round}: still live while held");
                        Assert.True(app.WaitForAudio());
                        Assert.Equal(MicState.Live, app.Status.State);

                        keybd_event(VK_F13, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        Assert.True(TestSetup.PumpUntil(AllMuted, 3000), $"round {round}: muted after letting go");
                        Assert.True(app.WaitForAudio());
                        Assert.Equal(MicState.Muted, app.Status.State);
                    }

                    // Quick taps: every one ends muted.
                    for (int tap = 0; tap < 10; tap++)
                    {
                        keybd_event(VK_F13, 0, 0, UIntPtr.Zero);
                        TestSetup.PumpUntil(() => false, 20);
                        keybd_event(VK_F13, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    }
                    Assert.True(TestSetup.PumpUntil(() => !app.PushToTalkHeld && AllMuted(), 3000), "muted after quick taps");
                    Assert.True(app.WaitForAudio());
                    Assert.True(AllMuted());
                    Assert.Equal(MicState.Muted, app.Status.State);
                }
            });
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
    }
}
