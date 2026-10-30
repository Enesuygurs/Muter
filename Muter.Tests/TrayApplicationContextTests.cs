using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Hotkeys;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // The whole application wired together (real forms, tray icon, menu and audio thread) with a fake audio system and
    // a fake hotkey.
    public sealed class TrayApplicationContextTests : IDisposable
    {
        private const string StartupKey = @"Software\MuterTests\TrayRun";
        private const int WM_CLOSE = 0x0010;
        private const int WM_QUERYENDSESSION = 0x0011;

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

        private static TrayApplicationContext Start(FakeAudioEndpointProvider provider, Settings settings, FakeGlobalHotkey hotkey)
        {
            var app = new TrayApplicationContext(settings, () => provider, hotkey, new StartupRegistration(Application.ExecutablePath, StartupKey));
            Assert.True(app.WaitForAudio());
            return app;
        }

        private static void WithApp(FakeAudioEndpointProvider provider, Settings settings, Action<TrayApplicationContext, FakeGlobalHotkey> test, FakeGlobalHotkey hotkey = null)
        {
            TestSetup.RunSta(() =>
            {
                hotkey = hotkey ?? new FakeGlobalHotkey();
                using (TrayApplicationContext app = Start(provider, settings, hotkey))
                    test(app, hotkey);
                Assert.True(provider.Disposed, "audio devices released on exit");
                Assert.True(hotkey.Disposed, "hotkey released on exit");
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

        private static void Click(TrayApplicationContext app, params string[] path)
        {
            FindItem(app, path).PerformClick();
            app.WaitForAudio();
        }

        private static void Press(TrayApplicationContext app, FakeGlobalHotkey hotkey)
        {
            hotkey.Press();
            Assert.True(app.WaitForAudio());
        }

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
                Assert.Equal(StatusVisual.Live, VisualOf(app.TrayIcon.Icon));
                Assert.Equal("Muter - Headset: ON", app.TrayIcon.Text);
                Assert.True(app.OverlayWindow.Visible);
                Assert.True(app.MiniIndicator.Visible);

                Press(app, hotkey);
                Assert.True(provider.Get("a").Muted);
                Assert.Equal(StatusVisual.Muted, VisualOf(app.TrayIcon.Icon));
                Assert.Equal("Muter - Headset: OFF", app.TrayIcon.Text);
                Assert.True(app.OverlayWindow.Presentation.ShowsMuted);
                Assert.True(app.MiniIndicator.Presentation.ShowsMuted);
                Assert.True(app.OverlayWindow.Visible);

                Press(app, hotkey);
                Assert.False(provider.Get("a").Muted);
                Assert.Equal(StatusVisual.Live, VisualOf(app.TrayIcon.Icon));
                Assert.False(app.OverlayWindow.Presentation.ShowsMuted);
            });
        }

        [Fact]
        public void Every_press_is_one_toggle_even_in_a_fast_burst()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                for (int i = 0; i < 7; i++)
                    hotkey.Press();
                Assert.True(app.WaitForAudio());
                Assert.True(provider.Get("a").Muted); // odd number of presses
                Assert.Equal(MicState.Muted, app.Status.State);
                Assert.Equal(7, provider.Get("a").Writes);
            });
        }

        [Fact]
        public void External_change_notified_from_another_thread_reaches_the_ui()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                Press(app, hotkey);
                Assert.Equal(StatusVisual.Muted, VisualOf(app.TrayIcon.Icon));

                provider.Get("a").Muted = false; // unmuted by another application
                var thread = new Thread(provider.RaiseMuteStateChanged);
                thread.Start();
                thread.Join();
                Assert.True(TestSetup.PumpUntil(() => VisualOf(app.TrayIcon.Icon) == StatusVisual.Live, 500));
                Assert.Equal(MicState.Live, app.Status.State);
            });
        }

        [Fact]
        public void External_change_without_notification_is_caught_by_polling()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                Press(app, hotkey);
                provider.Get("a").Muted = false; // no notification at all
                Assert.True(TestSetup.PumpUntil(() => app.Status.State == MicState.Live, TrayApplicationContext.PollIntervalMs * 2 + 500));
                Assert.Equal(StatusVisual.Live, VisualOf(app.TrayIcon.Icon));
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
                Assert.True(TestSetup.PumpUntil(() => app.Status.State == MicState.NoDevice, 900));
                Assert.Equal(StatusVisual.NoDevice, VisualOf(app.TrayIcon.Icon));
                Assert.Equal("Muter - No Device", app.TrayIcon.Text);
            });
        }

        [Fact]
        public void Hung_audio_call_shows_unknown_instead_of_the_last_state()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                Press(app, hotkey);
                Assert.Equal(StatusVisual.Muted, VisualOf(app.TrayIcon.Icon));

                using (var gate = new ManualResetEventSlim(false))
                {
                    provider.BlockReads = gate;   // the driver hangs...
                    provider.Get("a").Muted = false; // ...while the microphone becomes live
                    hotkey.Press();

                    Assert.True(TestSetup.PumpUntil(() => app.AudioNotResponding, TrayApplicationContext.NotRespondingMs + 2500));
                    Assert.Equal(StatusVisual.Live, VisualOf(app.TrayIcon.Icon)); // never the muted icon
                    Assert.Equal("Muter - audio system not responding, assume ON", app.TrayIcon.Text);
                    Assert.Equal(StatusVisual.Unknown, app.OverlayWindow.Presentation.Visual);
                    Assert.False(app.OverlayWindow.Presentation.ShowsMuted);
                    Assert.False(app.MiniIndicator.Presentation.ShowsMuted);
                    Assert.True(app.OverlayWindow.Visible);

                    // The UI thread stays responsive meanwhile.
                    Assert.True(FindItem(app, "Overlay").DropDownItems.Count > 0);

                    provider.BlockReads = null;
                    gate.Set();
                }
                Assert.True(app.WaitForAudio());
                Assert.False(app.AudioNotResponding);
                Assert.NotEqual(MicState.Unknown, app.Status.State);
                Assert.Equal(app.Status.State == MicState.Muted, provider.Get("a").Muted);
            });
        }

        [Fact]
        public void A_tampered_tray_icon_is_corrected_by_the_next_poll()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                Press(app, hotkey);
                app.TrayIcon.Text = "stale";
                app.TrayIcon.Icon = AppResources.TrayLive; // e.g. an update Explorer never applied
                Assert.True(TestSetup.PumpUntil(() => app.TrayIcon.Text == "Muter - Headset: OFF" && VisualOf(app.TrayIcon.Icon) == StatusVisual.Muted,
                    TrayApplicationContext.PollIntervalMs * 2 + 500));
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
                Assert.True(app.IsAllDevicesMode);
                Assert.Equal(MicrophoneController.AllDevicesId, settings.selectedDeviceId);
                Assert.True(FindItem(app, "Devices", "All Devices").Checked);
                Assert.Equal("All Devices", app.OverlayWindow.Presentation.Title);
                Assert.Equal(2, app.Status.DeviceCount);

                Click(app, "Devices", "Array");
                Assert.Equal("b", settings.selectedDeviceId);
                Assert.Equal("Array", settings.selectedDeviceName);
                Press(app, hotkey);
                Assert.True(provider.Get("b").Muted);
                Assert.False(provider.Get("a").Muted);

                provider.Get("b").Active = false;
                Assert.Equal("Array (Disconnected)", FindItem(app, "Devices", "Array (Disconnected)").Text);
            });
        }

        [Fact]
        public void Devices_menu_does_not_hang_when_the_audio_system_hangs()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, NewSettings(), (app, hotkey) =>
            {
                using (var gate = new ManualResetEventSlim(false))
                {
                    provider.BlockReads = gate;
                    app.RefreshNow(); // the audio thread gets stuck in a read
                    Thread.Sleep(100);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Assert.NotNull(FindItem(app, "Devices", "(Failed to list devices)"));
                    Assert.True(watch.ElapsedMilliseconds < 2500, "menu opened in " + watch.ElapsedMilliseconds + " ms");
                    provider.BlockReads = null;
                    gate.Set();
                }
            });
        }

        [Fact]
        public void Sync_menu_item_toggles_on_and_off()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            provider.Add("b", muted: true);
            Settings settings = NewSettings(s =>
            {
                s.selectedDeviceId = MicrophoneController.AllDevicesId;
                s.syncDevices = false;
            });
            WithApp(provider, settings, (app, hotkey) =>
            {
                Assert.False(FindItem(app, "Sync Devices").Checked);
                Assert.Equal(MicState.Live, app.Status.State); // mixed, not synced

                Click(app, "Sync Devices");
                Assert.True(settings.syncDevices);
                Assert.True(FindItem(app, "Sync Devices").Checked);
                Assert.True(provider.Devices.All(d => d.Muted)); // aligned (no history: muted)
                Assert.Equal(MicState.Muted, app.Status.State);

                Click(app, "Sync Devices");
                Assert.False(settings.syncDevices);
                provider.Get("a").Muted = false;
                app.RefreshNow();
                app.WaitForAudio();
                Assert.True(provider.Get("b").Muted); // no longer synced
                Assert.Equal(MicState.Live, app.Status.State);
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

                Press(app, hotkey);
                Assert.False(app.OverlayWindow.Visible);
                Assert.Equal(StatusVisual.Muted, VisualOf(app.TrayIcon.Icon));
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
                Press(app, hotkey);
                Assert.True(app.OverlayWindow.Visible);
                Assert.True(TestSetup.PumpUntil(() => !app.OverlayWindow.Visible, 2000));

                Click(app, "Overlay", "Display Time", "Always Visible");
                Assert.True(app.OverlayWindow.Visible);
                Press(app, hotkey);
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
                Assert.Equal(MicState.Live, app.Status.State);
                app.ToggleMute(); // tray click still works
                app.WaitForAudio();
                Assert.True(provider.Get("a").Muted);
            }, hotkey);
        }

        [Fact]
        public void Audio_system_unavailable_at_start_shows_unknown_and_recovers()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            int attempts = 0;
            TestSetup.RunSta(() =>
            {
                Func<IAudioEndpointProvider> factory = () =>
                {
                    if (++attempts < 3)
                        throw new COMException("audio service not running");
                    return provider;
                };
                using (var app = new TrayApplicationContext(NewSettings(), factory, new FakeGlobalHotkey(), new StartupRegistration(Application.ExecutablePath, StartupKey)))
                {
                    app.WaitForAudio();
                    Assert.Equal(MicState.Unknown, app.Status.State);
                    Assert.Equal(StatusVisual.Live, VisualOf(app.TrayIcon.Icon));
                    Assert.Equal("Muter - state unknown, assume ON", app.TrayIcon.Text);
                    Assert.True(TestSetup.PumpUntil(() => app.Status.State == MicState.Live, TrayApplicationContext.PollIntervalMs * 3 + 500));
                }
            });
        }

        [Fact]
        public void Close_request_from_outside_exits_cleanly()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey();
            TestSetup.RunSta(() =>
            {
                TrayApplicationContext app = Start(provider, NewSettings(), hotkey);
                app.OverlayWindow.Close();
                Assert.True(TestSetup.PumpUntil(() => provider.Disposed, 3000));
                Assert.True(hotkey.Disposed);
                Assert.False(app.TrayIcon.Visible);
            });
        }

        [Fact]
        public void Posted_close_messages_like_taskkill_exit_cleanly_but_other_windows_close_normally()
        {
            foreach (bool viaCloseWindow in new[] { true, false })
            {
                FakeAudioEndpointProvider provider = OneLiveMic();
                var hotkey = new FakeGlobalHotkey();
                TestSetup.RunSta(() =>
                {
                    TrayApplicationContext app = Start(provider, NewSettings(s => s.showGui = false), hotkey);

                    // An ordinary form closes as usual and does not end the app.
                    var form = new Form();
                    PostMessage(form.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    TestSetup.PumpUntil(() => false, 300);
                    Assert.True(form.IsDisposed);
                    Assert.False(provider.Disposed);

                    // A window no longer registered with WinForms (a collected NativeWindow cleaning itself up) is
                    // destroyed as intended and does not end the app.
                    var orphan = new NativeWindow();
                    orphan.CreateHandle(new CreateParams());
                    IntPtr orphanHandle = orphan.Handle;
                    orphan.ReleaseHandle();
                    PostMessage(orphanHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    TestSetup.PumpUntil(() => false, 300);
                    Assert.False(IsWindow(orphanHandle));
                    Assert.False(provider.Disposed);

                    // taskkill: to our close window, or to any live helper window of ours (like the tray icon's,
                    // which must survive until the icon is removed): clean exit.
                    var helper = new NativeWindow();
                    helper.CreateHandle(new CreateParams());
                    PostMessage(viaCloseWindow ? app.CloseRequestWindow : helper.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    Assert.True(TestSetup.PumpUntil(() => provider.Disposed, 3000));
                    Assert.True(hotkey.Disposed);
                    Assert.False(app.TrayIcon.Visible);
                    Assert.True(IsWindow(helper.Handle));
                    helper.DestroyHandle();
                });
            }
        }

        [Fact]
        public void Never_blocks_windows_sign_out_or_shutdown()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey();
            TestSetup.RunSta(() =>
            {
                TrayApplicationContext app = Start(provider, NewSettings(), hotkey);
                foreach (Form window in new Form[] { app.OverlayWindow, app.MiniIndicator })
                    Assert.NotEqual(IntPtr.Zero, SendMessage(window.Handle, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero));
                Assert.True(TestSetup.PumpUntil(() => provider.Disposed, 3000));
            });
        }

        [Fact]
        public void Exit_releases_everything()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            var hotkey = new FakeGlobalHotkey();
            TestSetup.RunSta(() =>
            {
                TrayApplicationContext app = Start(provider, NewSettings(), hotkey);
                FindItem(app, "Exit").PerformClick();
                Assert.True(provider.Disposed);
                Assert.True(hotkey.Disposed);
                Assert.False(app.TrayIcon.Visible);
                Assert.True(app.OverlayWindow.IsDisposed);
                app.ToggleMute(); // after exit: ignored, no exception
                provider.RaiseMuteStateChanged();
                Application.DoEvents();
            });
        }

        private static StatusVisual VisualOf(Icon icon)
        {
            foreach (StatusVisual visual in new[] { StatusVisual.Live, StatusVisual.Muted, StatusVisual.NoDevice })
            {
                if (icon == AppResources.TrayIcon(visual, false) || icon == AppResources.TrayIcon(visual, true))
                    return visual; // Unknown deliberately uses the live icon, so it reports Live here
            }
            throw new InvalidOperationException("Unexpected tray icon");
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
    }
}
