using Muter.Audio;
using Muter.Localization;

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
            Strings text = Strings.Current;
            string title = allDevicesMode ? text.AllMicrophones : text.Microphone;
            string device = allDevicesMode
                ? text.DeviceAll
                : status.DeviceNames.Count == 0 ? text.Microphone
                : status.DeviceNames.Count == 1 ? status.DeviceNames[0]
                : $"{status.DeviceNames[0]} +{status.DeviceNames.Count - 1}";

            switch (status.State)
            {
                case MicState.Muted:
                    return new StatusPresentation(title, text.StateOff, StatusVisual.Muted, FormatTooltip($"{device}: {text.StateOff}"));
                case MicState.Live:
                    string detail = status.MutedCount > 0 ? string.Format(text.TipPartlyLive, status.LiveCount, status.DeviceCount) : text.StateOn;
                    return new StatusPresentation(title, text.StateOn, StatusVisual.Live, FormatTooltip($"{device}: {detail}"));
                case MicState.NoDevice:
                    return new StatusPresentation(title, text.StateNoDevice, StatusVisual.NoDevice, FormatTooltip(text.StateNoDevice));
                default:
                    return new StatusPresentation(title, text.StateUnknown, StatusVisual.Unknown, FormatTooltip(text.TipUnknown));
            }
        }

        // Shown while an audio call is stuck: the last known state may no longer be true.
        public static StatusPresentation NotResponding(bool allDevicesMode)
        {
            Strings text = Strings.Current;
            return new StatusPresentation(allDevicesMode ? text.AllMicrophones : text.Microphone, text.StateUnknown, StatusVisual.Unknown,
                FormatTooltip(text.TipNotResponding));
        }

        private static string FormatTooltip(string text)
        {
            string tooltip = "Muter - " + text;
            return tooltip.Length <= MaxTooltipLength ? tooltip : tooltip.Substring(0, MaxTooltipLength - 3) + "...";
        }
    }

    internal static class StatusVisualExtensions
    {
        public static MicState ToState(this StatusVisual visual)
        {
            switch (visual)
            {
                case StatusVisual.Live: return MicState.Live;
                case StatusVisual.Muted: return MicState.Muted;
                case StatusVisual.NoDevice: return MicState.NoDevice;
                default: return MicState.Unknown;
            }
        }
    }
}
