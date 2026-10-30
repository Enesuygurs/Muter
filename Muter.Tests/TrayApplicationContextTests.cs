using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Overlay;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // The whole application wired together (real forms, tray icon and menu) with a fake audio system and hotkey.
    public sealed class TrayApplicationContextTests : IDisposable
    {
        private const string StartupKey = @"Software\MuterTests\TrayRun";

        public TrayApplicationContextTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        private static Settings NewSettings(Action<Settings> configure = null)
        {
            var settings = new Settings();
            settings.Reset();
            settings.hotkeyKey = (int)Keys.F9;
            settings.hotkeyModifiers = 0;
            configure?.Invoke(settings);
            return settings;
        }

        private static void WithApp(FakeAudioEndpointProvider provider, Settings settings, Action<TrayApplicationContext, FakeGlobalHotkey> test, FakeGlobalHotkey hotkey = null)
        {
            TestSetup.RunSta(() =>
            {
                hotkey = hotkey ?? new FakeGlobalHotkey();
                var startup = new StartupRegistration(Application.ExecutablePath, StartupKey);
                using (var app = new TrayApplicationContext(settings, provider, hotkey, startup))
                    test(app, hotkey);
                Assert.True(provider.Disposed);
                Assert.True(hotkey.Disposed);
            });
        }

        private static ToolStripMenuItem FindItem(TrayApplicationContext app, params string[] path)
        {
            app.RebuildMenu();
            ToolStripItemCollection items = app.Menu.Items;
            ToolStripMenuItem found = null;
            foreach (string text in path)
            {
                found = items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text.StartsWith(text, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Menu item not found: " + string.Join(" > ", path));
                items = found.DropDownItems;
            }
            return found;
        }

        private static void Click(TrayApplicationContext app, params string[] path) => FindItem(app, path).PerformClick();

        private static FakeAudioEndpointProvider OneLiveMic()
        {
            var provider = new FakeAudioEndpointProvider();
            provider.Add("a", name: "Headset");
            provider.SetDefault("a");
            return provider;
        }

        [Fact]
        public void Hotkey_updates_tray_icon_tooltip_overlay_and_mini_indicator()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(s => s.miniIndicatorEnabled = true), (app, hotkey) =>
            {
                Assert.True(hotkey.IsRegistered);
                Assert.Equal(new Hotkey(Keys.F9, HotkeyModifiers.None), hotkey.Current);
                Assert.Same(AppResources.TrayLive, app.TrayIcon.Icon);
                Assert.Equal("Muter - Headset: ON", app.TrayIcon.Text);
                Assert.True(app.OverlayWindow.Visible);
                Assert.True(app.MiniIndicator.Visible);

                hotkey.Press();
                Assert.True(provider.Get("a").Muted);
                Assert.Same(AppResources.TrayMuted, app.TrayIcon.Icon);
                Assert.Equal("Muter - Headset: OFF", app.TrayIcon.Text);
                Assert.True(app.OverlayWindow.Presentation.ShowsMuted);
                Assert.True(app.MiniIndicator.Presentation.ShowsMuted);
                Assert.True(app.OverlayWindow.Visible);

                hotkey.Press();
                Assert.False(provider.Get("a").Muted);
                Assert.Same(AppResources.TrayLive, app.TrayIcon.Icon);
                Assert.False(app.OverlayWindow.Presentation.ShowsMuted);
            });
        }

        [Fact]
        public void External_change_notified_from_another_thread_reaches_the_ui()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                hotkey.Press();
                Assert.Same(AppResources.TrayMuted, app.TrayIcon.Icon);

                provider.Get("a").Muted = false; // unmuted by another application
                var thread = new Thread(provider.RaiseMuteStateChanged);
                thread.Start();
                thread.Join();
                Assert.True(TestSetup.PumpUntil(() => app.TrayIcon.Icon == AppResources.TrayLive, 500));
                Assert.Equal(MicState.Live, app.Controller.Status.State);
            });
        }

        [Fact]
        public void External_change_without_notification_is_caught_by_polling()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                hotkey.Press();
                provider.Get("a").Muted = false; // no notification at all
                Assert.True(TestSetup.PumpUntil(() => app.Controller.Status.State == MicState.Live, TrayApplicationContext.PollIntervalMs * 2 + 500));
                Assert.Same(AppResources.TrayLive, app.TrayIcon.Icon);
            });
        }

        [Fact]
        public void Device_removal_notification_updates_the_state()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(s => s.selectedDeviceId = "a"), (app, hotkey) =>
            {
                provider.Get("a").Active = false;
                new Thread(provider.RaiseDevicesChanged).Start();
                Assert.True(TestSetup.PumpUntil(() => app.Controller.Status.State == MicState.NoDevice, 900));
                Assert.Same(AppResources.TrayNoDevice, app.TrayIcon.Icon);
                Assert.Equal("Muter - No Device", app.TrayIcon.Text);
            });
        }

        [Fact]
        public void Devices_menu_switches_modes_and_saves_the_choice()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            provider.Add("b", name: "Array");
            Settings settings = NewSettings();
            WithApp(provider, settings, (app, hotkey) =>
            {
                Assert.True(FindItem(app, "Devices", "Default (System)").Checked);

                Click(app, "Devices", "All Devices");
                Assert.True(app.Controller.IsAllDevicesMode);
                Assert.Equal(MicrophoneController.AllDevicesId, settings.selectedDeviceId);
                Assert.True(FindItem(app, "Devices", "All Devices").Checked);
                Assert.Equal("All Devices", app.OverlayWindow.Presentation.Title);

                Click(app, "Devices", "Array");
                Assert.Equal("b", settings.selectedDeviceId);
                Assert.Equal("Array", settings.selectedDeviceName);
                hotkey.Press();
                Assert.True(provider.Get("b").Muted);
                Assert.False(provider.Get("a").Muted);

                provider.Get("b").Active = false;
                Assert.Equal("Array (Disconnected)", FindItem(app, "Devices", "Array (Disconnected)").Text);
            });
        }

        [Fact]
        public void Sync_menu_item_toggles_on_and_off()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            Settings settings = NewSettings(s => s.selectedDeviceId = MicrophoneController.AllDevicesId);
            WithApp(provider, settings, (app, hotkey) =>
            {
                Assert.True(FindItem(app, "Sync Devices").Checked);
                Click(app, "Sync Devices");
                Assert.False(settings.syncDevices);
                Assert.False(app.Controller.SyncDevices);
                Assert.False(FindItem(app, "Sync Devices").Checked);
                Click(app, "Sync Devices");
                Assert.True(settings.syncDevices);
                Assert.True(app.Controller.SyncDevices);
            });
        }

        [Fact]
        public void Hidden_overlay_stays_hidden_but_tray_still_updates()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            Settings settings = NewSettings();
            WithApp(provider, settings, (app, hotkey) =>
            {
                Click(app, "Overlay", "Show Overlay");
                Assert.False(settings.showGui);
                Assert.False(app.OverlayWindow.Visible);

                hotkey.Press();
                Assert.False(app.OverlayWindow.Visible);
                Assert.Same(AppResources.TrayMuted, app.TrayIcon.Icon);
            });
        }

        [Fact]
        public void Overlay_fades_after_the_display_time_or_stays_when_always_visible()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            Settings settings = NewSettings();
            WithApp(provider, settings, (app, hotkey) =>
            {
                Click(app, "Overlay", "Display Time", "0.5 s");
                Assert.Equal(500, settings.overlayDurationMs);
                hotkey.Press();
                Assert.True(app.OverlayWindow.Visible);
                Assert.True(TestSetup.PumpUntil(() => !app.OverlayWindow.Visible, 2000));

                Click(app, "Overlay", "Display Time", "Always Visible");
                Assert.True(app.OverlayWindow.Visible);
                hotkey.Press();
                Assert.False(TestSetup.PumpUntil(() => !app.OverlayWindow.Visible, 1500));
                Assert.Equal(1.0, app.OverlayWindow.Opacity, 3);
            });
        }

        [Fact]
        public void Position_size_and_monitor_menus_move_the_overlay()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            Settings settings = NewSettings();
            WithApp(provider, settings, (app, hotkey) =>
            {
                Screen primary = Screen.PrimaryScreen;
                Click(app, "Overlay", "Position", "Top Left");
                Assert.Equal("TopLeft", settings.overlayPosition);
                Assert.True(primary.WorkingArea.Contains(app.OverlayWindow.Bounds));
                Point topLeft = app.OverlayWindow.Location;

                Click(app, "Overlay", "Position", "Bottom Right");
                Assert.True(primary.WorkingArea.Contains(app.OverlayWindow.Bounds));
                Assert.True(app.OverlayWindow.Left > topLeft.X && app.OverlayWindow.Top > topLeft.Y);

                Size medium = app.OverlayWindow.Size;
                Click(app, "Overlay", "Size", "Large");
                Assert.Equal("Large", settings.overlaySize);
                Assert.Equal(Math.Round(medium.Width * 1.5), app.OverlayWindow.Width, 0);
                Click(app, "Overlay", "Size", "Small");
                Assert.Equal(Math.Round(medium.Width * 0.75), app.OverlayWindow.Width, 0);

                foreach (Screen screen in Screen.AllScreens)
                {
                    Click(app, "Overlay", "Monitor", "Display " + new string(screen.DeviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()));
                    Assert.Equal(screen.DeviceName, settings.overlayMonitor);
                    Assert.True(screen.WorkingArea.Contains(app.OverlayWindow.Bounds), screen.DeviceName);
                }

                Click(app, "Overlay", "Monitor", "Primary");
                Assert.True(primary.WorkingArea.Contains(app.OverlayWindow.Bounds));
            });
        }

        [Fact]
        public void Mini_indicator_menu_and_unlock_and_reset()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            Settings settings = NewSettings();
            WithApp(provider, settings, (app, hotkey) =>
            {
                Assert.False(app.MiniIndicator.Visible);
                Click(app, "Overlay", "Mini Indicator");
                Assert.True(settings.miniIndicatorEnabled);
                Assert.True(app.MiniIndicator.Visible);

                Click(app, "Overlay", "Mini Indicator Position", "Top Left");
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Assert.True(area.Contains(app.MiniIndicator.Bounds));
                Assert.True(app.MiniIndicator.Left < area.Left + 100 && app.MiniIndicator.Top < area.Top + 100);

                Size medium = app.MiniIndicator.Size;
                Click(app, "Overlay", "Mini Indicator Size", "Large");
                Assert.True(app.MiniIndicator.Width > medium.Width);

                Click(app, "Overlay", "Unlock Positions");
                Assert.True(app.PositionsUnlocked);
                Assert.True(app.OverlayWindow.MoveMode && app.MiniIndicator.MoveMode);
                Assert.True(app.OverlayWindow.Visible);
                Assert.False(TestSetup.PumpUntil(() => !app.OverlayWindow.Visible, 1500)); // stays while unlocked

                Click(app, "Overlay", "Unlock Positions");
                Assert.False(app.OverlayWindow.MoveMode || app.MiniIndicator.MoveMode);

                settings.overlayPosition = "Custom";
                Click(app, "Overlay", "Reset Positions");
                Assert.Equal("LowerCenter", settings.overlayPosition);
                Assert.Equal("BottomRight", settings.miniIndicatorPosition);
            });
        }

        [Fact]
        public void Run_startup_menu_item_toggles_the_registry_entry()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                Assert.False(FindItem(app, "Run Startup").Checked);
                Click(app, "Run Startup");
                Assert.True(FindItem(app, "Run Startup").Checked);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey))
                    Assert.Equal("\"" + Application.ExecutablePath + "\"", key.GetValue("Muter"));
                Click(app, "Run Startup");
                Assert.False(FindItem(app, "Run Startup").Checked);
            });
        }

        [Fact]
        public void Taken_hotkey_does_not_break_startup()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey { Taken = new Hotkey(Keys.F9, HotkeyModifiers.None) };
            WithApp(provider, NewSettings(), (app, h) =>
            {
                Assert.False(h.IsRegistered);
                Assert.Equal(MicState.Live, app.Controller.Status.State);
                app.ToggleMute(); // tray click still works
                Assert.True(provider.Get("a").Muted);
            }, hotkey);
        }

        [Fact]
        public void Close_request_from_outside_exits_cleanly()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey();
            TestSetup.RunSta(() =>
            {
                var app = new TrayApplicationContext(NewSettings(), provider, hotkey, new StartupRegistration(Application.ExecutablePath, StartupKey));
                app.OverlayWindow.Close(); // what taskkill or Windows sign-out sends
                Assert.True(TestSetup.PumpUntil(() => provider.Disposed, 1000));
                Assert.True(hotkey.Disposed);
                Assert.False(app.TrayIcon.Visible);
            });
        }

        [Fact]
        public void Exit_releases_everything()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey();
            TestSetup.RunSta(() =>
            {
                var app = new TrayApplicationContext(NewSettings(), provider, hotkey, new StartupRegistration(Application.ExecutablePath, StartupKey));
                Click(app, "Exit");
                Assert.True(provider.Disposed);
                Assert.True(hotkey.Disposed);
                Assert.False(app.TrayIcon.Visible);
                Assert.True(app.OverlayWindow.IsDisposed);
                app.ToggleMute(); // after exit: ignored, no exception
                provider.RaiseMuteStateChanged();
                Application.DoEvents();
            });
        }
    }
}
