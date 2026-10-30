using System;
using Muter.Audio;
using Muter.UI;
using Xunit;

namespace Muter.Tests
{
    public class StatusPresentationTests
    {
        public StatusPresentationTests() => TestSetup.Init();

        [Theory]
        [InlineData(MicState.Muted, StatusVisual.Muted, "OFF")]
        [InlineData(MicState.Live, StatusVisual.Live, "ON")]
        [InlineData(MicState.NoDevice, StatusVisual.NoDevice, "No Device")]
        [InlineData(MicState.Unknown, StatusVisual.Unknown, "UNKNOWN")]
        public void Maps_each_state(MicState state, StatusVisual visual, string text)
        {
            var status = new MicrophoneStatus(state, state == MicState.Muted ? 1 : 0, state == MicState.Live ? 1 : 0, state == MicState.Unknown ? 1 : 0, new[] { "Headset" });
            StatusPresentation p = StatusPresentation.From(status, allDevicesMode: false);
            Assert.Equal(visual, p.Visual);
            Assert.Equal(text, p.StateText);
            Assert.Equal(state == MicState.Muted, p.ShowsMuted);
        }

        [Fact]
        public void Only_the_muted_state_uses_muted_visuals()
        {
            foreach (MicState state in Enum.GetValues(typeof(MicState)))
            {
                StatusPresentation p = StatusPresentation.From(new MicrophoneStatus(state, 1, 1, 1, new[] { "a", "b" }), allDevicesMode: true);
                Assert.Equal(state == MicState.Muted, p.ShowsMuted);
                Assert.Equal(state == MicState.Muted, p.StateText == "OFF");
            }
        }

        [Fact]
        public void Tooltip_never_exceeds_the_windows_limit()
        {
            string longName = new string('x', 300);
            foreach (MicState state in Enum.GetValues(typeof(MicState)))
            {
                StatusPresentation p = StatusPresentation.From(new MicrophoneStatus(state, 2, 1, 0, new[] { longName, "b" }), allDevicesMode: false);
                Assert.True(p.Tooltip.Length <= 63, p.Tooltip);
                Assert.StartsWith("Muter - ", p.Tooltip);
            }
        }

        [Fact]
        public void Tooltip_shows_partial_mute_and_device_names()
        {
            StatusPresentation partial = StatusPresentation.From(new MicrophoneStatus(MicState.Live, 1, 1, 0, new[] { "Headset", "Array" }), allDevicesMode: false);
            Assert.Equal("Muter - Headset +1: ON (1 of 2 live)", partial.Tooltip);
            Assert.Equal("Microphone", partial.Title);

            StatusPresentation all = StatusPresentation.From(new MicrophoneStatus(MicState.Muted, 2, 0, 0, new[] { "Headset", "Array" }), allDevicesMode: true);
            Assert.Equal("Muter - All Devices: OFF", all.Tooltip);
            Assert.Equal("All Devices", all.Title);
        }
    }
}
