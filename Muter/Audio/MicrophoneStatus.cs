using System;
using System.Collections.Generic;
using System.Linq;

namespace Muter.Audio
{
    public enum MicState
    {
        // No targeted microphone is connected.
        NoDevice,

        // At least one targeted microphone is verified to be live (unmuted).
        Live,

        // Every targeted microphone was read back from the device and is muted. Never reported on a guess.
        Muted,

        // The state could not be verified (read failure). Must be treated as live.
        Unknown,
    }

    public sealed class MicrophoneStatus : IEquatable<MicrophoneStatus>
    {
        public static readonly MicrophoneStatus ReadFailure = new MicrophoneStatus(MicState.Unknown, 0, 0, 0, Array.Empty<string>());

        public MicrophoneStatus(MicState state, int mutedCount, int liveCount, int unreadableCount, IReadOnlyList<string> deviceNames)
        {
            State = state;
            MutedCount = mutedCount;
            LiveCount = liveCount;
            UnreadableCount = unreadableCount;
            DeviceNames = deviceNames ?? Array.Empty<string>();
        }

        public MicState State { get; }

        public int MutedCount { get; }

        public int LiveCount { get; }

        public int UnreadableCount { get; }

        public int DeviceCount => MutedCount + LiveCount + UnreadableCount;

        public IReadOnlyList<string> DeviceNames { get; }

        public bool Equals(MicrophoneStatus other)
        {
            return other != null
                && State == other.State
                && MutedCount == other.MutedCount
                && LiveCount == other.LiveCount
                && UnreadableCount == other.UnreadableCount
                && DeviceNames.SequenceEqual(other.DeviceNames);
        }

        public override bool Equals(object obj) => Equals(obj as MicrophoneStatus);

        public override int GetHashCode() => ((int)State * 397) ^ (MutedCount * 31) ^ (LiveCount * 17) ^ UnreadableCount;

        public override string ToString() => $"{State} (muted {MutedCount}, live {LiveCount}, unreadable {UnreadableCount})";
    }
}
