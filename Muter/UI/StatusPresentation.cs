using Muter.Audio;

namespace Muter.UI
{
    public enum StatusVisual
    {
        Live,
        Muted,
        NoDevice,
        Unknown,
    }

    // Texts and visuals for a microphone status. The muted visuals are used for MicState.Muted only; Unknown looks
    // like Live so an unverified state can never be mistaken for muted.
    internal sealed class StatusPresentation
    {
        public const int MaxTooltipLength = 63;

        private StatusPresentation(string title, string stateText, StatusVisual visual, string tooltip)
        {
            Title = title;
            StateText = stateText;
            Visual = visual;
            Tooltip = tooltip;
        }

        public string Title { get; }

        public string StateText { get; }

        public StatusVisual Visual { get; }

        public string Tooltip { get; }

        public bool ShowsMuted => Visual == StatusVisual.Muted;

        public static StatusPresentation From(MicrophoneStatus status, bool allDevicesMode)
        {
            string title = allDevicesMode ? "All Devices" : "Microphone";
            string device = allDevicesMode
                ? "All Devices"
                : status.DeviceNames.Count == 0 ? "Microphone"
                : status.DeviceNames.Count == 1 ? status.DeviceNames[0]
                : $"{status.DeviceNames[0]} +{status.DeviceNames.Count - 1}";

            switch (status.State)
            {
                case MicState.Muted:
                    return new StatusPresentation(title, "OFF", StatusVisual.Muted, FormatTooltip($"{device}: OFF"));
                case MicState.Live:
                    string detail = status.MutedCount > 0 ? $"ON ({status.LiveCount} of {status.DeviceCount} live)" : "ON";
                    return new StatusPresentation(title, "ON", StatusVisual.Live, FormatTooltip($"{device}: {detail}"));
                case MicState.NoDevice:
                    return new StatusPresentation(title, "No Device", StatusVisual.NoDevice, FormatTooltip("No Device"));
                default:
                    return new StatusPresentation(title, "UNKNOWN", StatusVisual.Unknown, FormatTooltip("state unknown, assume ON"));
            }
        }

        private static string FormatTooltip(string text)
        {
            string tooltip = "Muter - " + text;
            return tooltip.Length <= MaxTooltipLength ? tooltip : tooltip.Substring(0, MaxTooltipLength - 3) + "...";
        }
    }
}
