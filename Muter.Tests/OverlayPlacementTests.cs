using System;
using System.Collections.Generic;
using System.Drawing;
using Muter.Overlay;
using Xunit;

namespace Muter.Tests
{
    public class OverlayPlacementTests
    {
        // This machine's layout: primary 2560x1440 with a 40px taskbar, secondary 1920x1200 to the right, offset down.
        private static readonly ScreenInfo Primary = new ScreenInfo(@"\\.\DISPLAY2", new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 0, 2560, 1400), true);
        private static readonly ScreenInfo Right = new ScreenInfo(@"\\.\DISPLAY1", new Rectangle(2560, 115, 1920, 1200), new Rectangle(2560, 115, 1920, 1160), false);
        private static readonly ScreenInfo Left = new ScreenInfo(@"\\.\DISPLAY3", new Rectangle(-1280, -200, 1280, 1024), new Rectangle(-1280, -160, 1280, 984), false);
        private static readonly IReadOnlyList<ScreenInfo> Screens = new[] { Primary, Right, Left };

        public OverlayPlacementTests() => TestSetup.Init();

        [Theory]
        [InlineData(OverlayAnchor.TopLeft, 16, 16)]
        [InlineData(OverlayAnchor.TopCenter, 1219, 16)]
        [InlineData(OverlayAnchor.TopRight, 2422, 16)]
        [InlineData(OverlayAnchor.Center, 1219, 624)]
        [InlineData(OverlayAnchor.LowerCenter, 1219, 975)]
        [InlineData(OverlayAnchor.BottomLeft, 16, 1233)]
        [InlineData(OverlayAnchor.BottomCenter, 1219, 1233)]
        [InlineData(OverlayAnchor.BottomRight, 2422, 1233)]
        public void Presets_are_placed_inside_the_working_area(OverlayAnchor anchor, int x, int y)
        {
            var size = new Size(122, 151);
            Point location = OverlayPlacement.ComputeLocation(Primary.WorkingArea, size, anchor, Point.Empty, 16);
            Assert.Equal(new Point(x, y), location);
            Assert.True(Primary.WorkingArea.Contains(new Rectangle(location, size)));
        }

        [Fact]
        public void Every_preset_fits_on_every_monitor_at_every_size()
        {
            foreach (ScreenInfo screen in Screens)
            foreach (OverlayAnchor anchor in Enum.GetValues(typeof(OverlayAnchor)))
            foreach (float scale in new[] { 0.75f, 1f, 1.5f, 2.25f, 3f })
            {
                var size = new Size((int)(122 * scale), (int)(151 * scale));
                Point location = OverlayPlacement.ComputeLocation(screen.WorkingArea, size, anchor, new Point(99999, -99999), 16);
                Assert.True(screen.WorkingArea.Contains(new Rectangle(location, size)), $"{anchor} {scale} {screen.DeviceName}");
            }
        }

        [Fact]
        public void Custom_offset_is_relative_to_the_working_area_and_clamped()
        {
            Point location = OverlayPlacement.ComputeLocation(Right.WorkingArea, new Size(100, 100), OverlayAnchor.Custom, new Point(300, 200), 16);
            Assert.Equal(new Point(2860, 315), location);

            Point clamped = OverlayPlacement.ComputeLocation(Right.WorkingArea, new Size(100, 100), OverlayAnchor.Custom, new Point(5000, 5000), 16);
            Assert.Equal(new Point(Right.WorkingArea.Right - 100, Right.WorkingArea.Bottom - 100), clamped);

            Point negative = OverlayPlacement.ComputeLocation(Left.WorkingArea, new Size(100, 100), OverlayAnchor.Custom, new Point(-50, -50), 16);
            Assert.Equal(new Point(-1280, -160), negative);
        }

        [Fact]
        public void Window_larger_than_the_area_is_pinned_to_the_top_left()
        {
            var area = new Rectangle(10, 20, 50, 50);
            Assert.Equal(new Point(10, 20), OverlayPlacement.Clamp(new Point(500, 500), new Size(80, 80), area));
        }

        [Fact]
        public void Offset_round_trips()
        {
            var location = new Point(3000, 400);
            Point offset = OverlayPlacement.ToCustomOffset(location, Right.WorkingArea);
            Assert.Equal(location, OverlayPlacement.ComputeLocation(Right.WorkingArea, new Size(50, 50), OverlayAnchor.Custom, offset, 16));
        }

        [Fact]
        public void Resolves_primary_named_and_missing_monitors()
        {
            var options = new OverlayPlacementOptions();
            Assert.Same(Primary, OverlayPlacement.ResolveScreen(Screens, options, Point.Empty));

            options.Monitor = @"\\.\display1";
            Assert.Same(Right, OverlayPlacement.ResolveScreen(Screens, options, Point.Empty));

            options.Monitor = @"\\.\DISPLAY9"; // unplugged
            Assert.Same(Primary, OverlayPlacement.ResolveScreen(Screens, options, Point.Empty));
        }

        [Fact]
        public void Follow_mouse_uses_the_monitor_under_the_cursor()
        {
            var options = new OverlayPlacementOptions { Monitor = OverlayPlacement.FollowMouseMonitor };
            Assert.Same(Right, OverlayPlacement.ResolveScreen(Screens, options, new Point(3000, 500)));
            Assert.Same(Left, OverlayPlacement.ResolveScreen(Screens, options, new Point(-10, 0)));
            Assert.Same(Primary, OverlayPlacement.ResolveScreen(Screens, options, new Point(100, 100)));
            // Cursor in the dead zone above the right monitor: nearest monitor.
            Assert.Same(Right, OverlayPlacement.ResolveScreen(Screens, options, new Point(3000, 50)));
        }

        [Fact]
        public void Custom_position_stays_on_its_monitor_and_falls_back_when_it_is_gone()
        {
            var options = new OverlayPlacementOptions { Anchor = OverlayAnchor.Custom, CustomMonitor = @"\\.\DISPLAY1", Monitor = OverlayPlacement.FollowMouseMonitor };
            Assert.Same(Right, OverlayPlacement.ResolveScreen(Screens, options, new Point(100, 100)));

            options.CustomMonitor = @"\\.\DISPLAY7";
            options.Monitor = OverlayPlacement.PrimaryMonitor;
            Assert.Same(Primary, OverlayPlacement.ResolveScreen(Screens, options, new Point(3000, 500)));
        }

        [Fact]
        public void Screen_for_bounds_picks_the_largest_overlap()
        {
            Assert.Same(Right, OverlayPlacement.ScreenForBounds(Screens, new Rectangle(2500, 300, 122, 151)));
            Assert.Same(Primary, OverlayPlacement.ScreenForBounds(Screens, new Rectangle(2500, 300, 100, 151)));
            Assert.Same(Right, OverlayPlacement.ScreenForBounds(Screens, new Rectangle(9000, 300, 10, 10)));
        }

        [Fact]
        public void Enum_settings_fall_back_on_bad_values()
        {
            Assert.Equal(OverlayAnchor.TopRight, OverlayPlacement.ParseEnum("topright", OverlayAnchor.LowerCenter));
            Assert.Equal(OverlayAnchor.LowerCenter, OverlayPlacement.ParseEnum("nonsense", OverlayAnchor.LowerCenter));
            Assert.Equal(OverlayAnchor.LowerCenter, OverlayPlacement.ParseEnum("42", OverlayAnchor.LowerCenter));
            Assert.Equal(OverlaySize.Medium, OverlayPlacement.ParseEnum<OverlaySize>(null, OverlaySize.Medium));
            Assert.Equal(1.5f, OverlayPlacement.SizeFactor(OverlaySize.Large));
            Assert.Equal(0.75f, OverlayPlacement.SizeFactor(OverlaySize.Small));
        }
    }
}
