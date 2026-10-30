using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Overlay;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    // Real window behaviour: extended styles, focus and dragging with injected mouse input.
    public sealed class OverlayWindowTests : IDisposable
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x8;

        public OverlayWindowTests() => TestSetup.Init();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);

        [Fact]
        public void Overlays_are_click_through_topmost_and_never_take_focus_until_unlocked()
        {
            TestSetup.RunSta(() =>
            {
                using (var app = CreateApp(out _, out Settings settings))
                {
                    app.SetMiniIndicatorEnabled(true);
                    IntPtr before = GetForegroundWindow();
                    app.ToggleMute();
                    app.WaitForAudio();
                    TestSetup.PumpUntil(() => false, 200);

                    foreach (Form form in new Form[] { app.OverlayWindow, app.MiniIndicator })
                    {
                        Assert.True(form.Visible);
                        int style = GetWindowLong(form.Handle, GWL_EXSTYLE);
                        Assert.True((style & WS_EX_TRANSPARENT) != 0, "click-through");
                        Assert.True((style & WS_EX_NOACTIVATE) != 0, "no-activate");
                        Assert.True((style & WS_EX_TOOLWINDOW) != 0, "hidden from Alt+Tab");
                        Assert.True((style & WS_EX_TOPMOST) != 0, "topmost");
                        Assert.NotEqual(form.Handle, GetForegroundWindow());
                    }
                    // (The foreground window itself may change meanwhile if someone is using the computer.)
                    Assert.NotEqual(app.OverlayWindow.Handle, GetForegroundWindow());
                    Assert.NotEqual(app.MiniIndicator.Handle, GetForegroundWindow());

                    app.SetPositionsUnlocked(true);
                    foreach (Form form in new Form[] { app.OverlayWindow, app.MiniIndicator })
                        Assert.True((GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) == 0, "draggable while unlocked");

                    app.SetPositionsUnlocked(false);
                    foreach (Form form in new Form[] { app.OverlayWindow, app.MiniIndicator })
                        Assert.True((GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0, "click-through again");
                }
            });
        }

        [Fact]
        public void Dragging_an_unlocked_overlay_saves_a_custom_position_and_keeps_focus()
        {
            Point originalCursor = Cursor.Position;
            try
            {
                TestSetup.RunSta(() =>
                {
                    using (var app = CreateApp(out _, out Settings settings))
                    {
                        app.SetPositionsUnlocked(true);
                        TestSetup.PumpUntil(() => false, 200);
                        Rectangle start, moved;

                        // The drag uses the real mouse, so someone using the computer at the same moment can disturb
                        // it; a disturbed attempt is repeated (up to 3 times).
                        for (int attempt = 1; ; attempt++)
                        {
                            start = app.OverlayWindow.Bounds;
                            var grab = new Point(start.Left + start.Width / 2, start.Top + 10);

                            // Input comes from another thread: while the button is down Windows runs a modal move loop
                            // on the UI thread, exactly as with a real mouse.
                            string underCursor = null;
                            var mouse = new System.Threading.Thread(() =>
                            {
                                MoveMouse(grab);
                                System.Threading.Thread.Sleep(150);
                                underCursor = Describe(WindowFromPoint(grab)) + " cursor=" + Cursor.Position;
                                MouseButton(down: true);
                                for (int i = 1; i <= 10; i++)
                                {
                                    System.Threading.Thread.Sleep(30);
                                    MoveMouse(new Point(grab.X - 30 * i, grab.Y - 20 * i));
                                }
                                System.Threading.Thread.Sleep(100);
                                MouseButton(down: false);
                            });
                            mouse.Start();

                            bool saved = TestSetup.PumpUntil(() => !mouse.IsAlive && settings.overlayPosition == "Custom", 5000);
                            moved = app.OverlayWindow.Bounds;
                            int dx = moved.Left - start.Left;
                            int dy = moved.Top - start.Top;
                            if (saved && dx >= -310 && dx <= -290 && dy >= -210 && dy <= -190)
                                break;

                            Assert.True(attempt < 3,
                                $"drag failed 3 times; last: saved={saved} moved by ({dx},{dy}). overlay {app.OverlayWindow.Handle} visible={app.OverlayWindow.Visible} " +
                                $"exstyle=0x{GetWindowLong(app.OverlayWindow.Handle, GWL_EXSTYLE):X} under cursor: {underCursor}; foreground: {Describe(GetForegroundWindow())}");
                            app.ResetPositions();
                            TestSetup.PumpUntil(() => false, 300);
                        }

                        Screen screen = Screen.FromRectangle(moved);
                        Assert.Equal(screen.DeviceName, settings.overlayCustomMonitor);
                        Assert.Equal(moved.Left - screen.WorkingArea.Left, settings.overlayCustomX);
                        Assert.Equal(moved.Top - screen.WorkingArea.Top, settings.overlayCustomY);
                        Assert.NotEqual(app.OverlayWindow.Handle, GetForegroundWindow()); // dragging never activates it

                        // Locking again keeps the dragged position.
                        app.SetPositionsUnlocked(false);
                        app.ToggleMute();
                        app.WaitForAudio();
                        Assert.Equal(moved.Location, app.OverlayWindow.Location);
                    }
                });
            }
            finally
            {
                Cursor.Position = originalCursor;
            }
        }

        private static TrayApplicationContext CreateApp(out FakeAudioEndpointProvider provider, out Settings settings)
        {
            provider = new FakeAudioEndpointProvider();
            provider.Add("a", name: "Headset");
            provider.SetDefault("a");
            settings = new Settings();
            settings.Reset();
            settings.overlayDurationMs = 5000;
            FakeAudioEndpointProvider providerCopy = provider;
            var app = new TrayApplicationContext(settings, () => providerCopy, new FakeGlobalHotkey(), new StartupRegistration(Application.ExecutablePath, @"Software\MuterTests\OverlayRun"), TestSetup.Silent);
            app.WaitForAudio();
            return app;
        }

        private static void MoveMouse(Point screenPoint)
        {
            Rectangle desktop = SystemInformation.VirtualScreen;
            var input = new INPUT
            {
                type = 0,
                mi = new MOUSEINPUT
                {
                    dx = (int)Math.Round((screenPoint.X - desktop.Left) * 65535.0 / (desktop.Width - 1)),
                    dy = (int)Math.Round((screenPoint.Y - desktop.Top) * 65535.0 / (desktop.Height - 1)),
                    dwFlags = 0x0001 | 0x8000 | 0x4000, // MOVE | ABSOLUTE | VIRTUALDESK
                },
            };
            Assert.Equal(1u, SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))));
        }

        private static void MouseButton(bool down)
        {
            var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = down ? 0x0002u : 0x0004u } };
            Assert.Equal(1u, SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
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

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static string Describe(IntPtr hWnd)
        {
            var name = new System.Text.StringBuilder(256);
            GetClassName(hWnd, name, name.Capacity);
            GetWindowThreadProcessId(hWnd, out uint pid);
            string process;
            try
            {
                process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch (ArgumentException)
            {
                process = "?";
            }
            return $"{hWnd} {name} ({process})";
        }
    }

    internal static class TrayApplicationContextTestsAccess
    {
        public static void Click(TrayApplicationContext app, params string[] path)
        {
            app.RebuildMenu();
            ToolStripItemCollection items = app.Menu.Items;
            ToolStripMenuItem found = null;
            foreach (string text in path)
            {
                found = null;
                foreach (ToolStripItem item in items)
                {
                    if (item is ToolStripMenuItem menuItem && menuItem.Text.StartsWith(text, StringComparison.Ordinal))
                    {
                        found = menuItem;
                        break;
                    }
                }
                Assert.NotNull(found);
                items = found.DropDownItems;
            }
            found.PerformClick();
        }
    }
}
