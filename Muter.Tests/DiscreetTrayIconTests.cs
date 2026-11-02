using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Audio;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // The discreet tray icon: one plain colour like Windows' own icons, following the taskbar's light or dark mode.
    // It must still tell live and muted apart, and never look muted when the state is unknown.
    public sealed class DiscreetTrayIconTests : IDisposable
    {
        private const string StartupKey = @"Software\MuterTests\DiscreetRun";
        private static readonly StatusVisual[] Visuals = { StatusVisual.Live, StatusVisual.Muted, StatusVisual.NoDevice };
        private static readonly TrayIconStyle[] Plain = { TrayIconStyle.White, TrayIconStyle.Black };

        public DiscreetTrayIconTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        private static Color[] Pixels(Icon icon)
        {
            using (Bitmap bitmap = icon.ToBitmap())
            {
                var pixels = new Color[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y);
                return pixels;
            }
        }

        [Fact]
        public void Every_plain_icon_is_one_colour_on_a_transparent_background()
        {
            foreach (TrayIconStyle style in Plain)
            {
                foreach (StatusVisual visual in Visuals)
                {
                    Icon icon = AppResources.TrayIcon(visual, false, style);
                    Assert.Equal(SystemInformation.SmallIconSize, icon.Size);
                    Color[] visible = Pixels(icon).Where(p => p.A > 40).ToArray();
                    Assert.True(visible.Length > 15, $"{style} {visual} draws something");
                    Assert.True(Pixels(icon).Count(p => p.A == 0) > visible.Length, $"{style} {visual} has a transparent background");
                    Assert.All(visible, p => Assert.InRange(p.GetSaturation(), 0f, 0.05f)); // no green, no red
                    if (style == TrayIconStyle.White)
                        Assert.All(visible, p => Assert.True(p.GetBrightness() > 0.9f));
                    else
                        Assert.All(visible, p => Assert.True(p.GetBrightness() < 0.15f));
                }
            }
        }

        [Fact]
        public void The_three_states_look_different_in_every_style()
        {
            foreach (TrayIconStyle style in new[] { TrayIconStyle.Colored, TrayIconStyle.White, TrayIconStyle.Black })
            {
                Color[] live = Pixels(AppResources.TrayIcon(StatusVisual.Live, false, style));
                Color[] muted = Pixels(AppResources.TrayIcon(StatusVisual.Muted, false, style));
                Color[] none = Pixels(AppResources.TrayIcon(StatusVisual.NoDevice, false, style));
                Assert.False(live.SequenceEqual(muted), $"{style}: live and muted differ");
                Assert.False(live.SequenceEqual(none), $"{style}: live and no device differ");
                Assert.False(muted.SequenceEqual(none), $"{style}: muted and no device differ");
            }
        }

        [Fact]
        public void Unknown_looks_like_live_never_muted()
        {
            foreach (TrayIconStyle style in Plain)
            {
                Assert.Same(AppResources.TrayIcon(StatusVisual.Live, false, style), AppResources.TrayIcon(StatusVisual.Unknown, false, style));
                Assert.Same(AppResources.TrayIcon(StatusVisual.Live, true, style), AppResources.TrayIcon(StatusVisual.Unknown, true, style));
            }
        }

        [Fact]
        public void The_alternate_icon_is_the_same_image_in_another_object()
        {
            foreach (TrayIconStyle style in Plain)
            {
                Icon icon = AppResources.TrayIcon(StatusVisual.Muted, false, style);
                Icon alternate = AppResources.TrayIcon(StatusVisual.Muted, true, style);
                Assert.NotSame(icon, alternate);
                Assert.Equal(Pixels(icon), Pixels(alternate));
            }
        }

        private static void WithApp(FakeAudioEndpointProvider provider, Action<Settings> configure, Action<TrayApplicationContext> test)
        {
            TestSetup.RunSta(() =>
            {
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                configure?.Invoke(settings);
                using (var app = new TrayApplicationContext(settings, () => provider, new FakeGlobalHotkey(),
                    new StartupRegistration(Application.ExecutablePath, StartupKey), TestSetup.Silent))
                {
                    Assert.True(app.WaitForAudio());
                    test(app);
                }
            });
        }

        private static void AssertTrayShows(TrayApplicationContext app, StatusVisual visual, TrayIconStyle style)
        {
            Icon shown = app.TrayIcon.Icon;
            Assert.True(shown == AppResources.TrayIcon(visual, false, style) || shown == AppResources.TrayIcon(visual, true, style),
                $"tray should show {style} {visual}");
        }

        private static FakeAudioEndpointProvider OneLiveMic()
        {
            var provider = new FakeAudioEndpointProvider();
            provider.Add("a");
            provider.SetDefault("a");
            return provider;
        }

        [Fact]
        public void Turned_on_the_tray_shows_the_plain_icon_of_the_taskbar_and_the_state()
        {
            WithApp(OneLiveMic(), null, app =>
            {
                Assert.Equal(TrayIconStyle.Colored, app.TrayStyle);
                AssertTrayShows(app, StatusVisual.Live, TrayIconStyle.Colored);

                app.SetDiscreetTrayIcon(true);
                TrayIconStyle plain = TaskbarTheme.IsLight() ? TrayIconStyle.Black : TrayIconStyle.White;
                Assert.Equal(plain, app.TrayStyle);
                Assert.True(app.Settings.discreetTrayIcon);
                AssertTrayShows(app, StatusVisual.Live, plain);

                app.ToggleMute();
                Assert.True(app.WaitForAudio());
                AssertTrayShows(app, StatusVisual.Muted, plain);

                app.SetDiscreetTrayIcon(false);
                AssertTrayShows(app, StatusVisual.Muted, TrayIconStyle.Colored);
            });
        }

        [Fact]
        public void An_unreadable_microphone_shows_the_plain_live_icon()
        {
            FakeAudioEndpointProvider provider = OneLiveMic();
            WithApp(provider, s => s.discreetTrayIcon = true, app =>
            {
                app.ToggleMute();
                Assert.True(app.WaitForAudio());
                provider.Get("a").FailReads = -1;
                app.RefreshNow();
                Assert.True(app.WaitForAudio());
                Assert.Equal(MicState.Unknown, app.Status.State);
                Assert.True(provider.Get("a").Muted); // really muted, but unverified
                AssertTrayShows(app, StatusVisual.Live, app.TrayStyle);
            });
        }

        [Fact]
        public void The_setting_starts_the_app_in_discreet_mode_and_the_switch_changes_it()
        {
            WithApp(OneLiveMic(), s => s.discreetTrayIcon = true, app =>
            {
                Assert.NotEqual(TrayIconStyle.Colored, app.TrayStyle);
                AssertTrayShows(app, StatusVisual.Live, app.TrayStyle);

                SettingsForm form = app.OpenSettings();
                Application.DoEvents();
                Assert.True(form.DiscreetTrayToggle.Checked);
                form.DiscreetTrayToggle.Toggle();
                Assert.False(app.Settings.discreetTrayIcon);
                AssertTrayShows(app, StatusVisual.Live, TrayIconStyle.Colored);
                form.Close();
            });
        }
    }
}
