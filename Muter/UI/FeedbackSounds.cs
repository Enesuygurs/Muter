using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Muter.UI
{
    internal enum FeedbackSound
    {
        Live,    // rising: the microphone is on
        Muted,   // falling: every controlled microphone is verified muted
        Warning, // low double buzz: the state could not be verified (treat it as on)
    }

    // Short tones played after a mute or unmute. They are generated here (no sound files) and played asynchronously.
    internal static class FeedbackSounds
    {
        private const int SampleRate = 44100;
        private static readonly Dictionary<FeedbackSound, SoundPlayer> players = new Dictionary<FeedbackSound, SoundPlayer>();

        public static void Play(FeedbackSound sound)
        {
            try
            {
                if (!players.TryGetValue(sound, out SoundPlayer player))
                {
                    player = new SoundPlayer(new MemoryStream(Wave(sound)));
                    player.Load();
                    players[sound] = player;
                }
                player.Play();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not play the feedback sound", ex);
            }
        }

        // 16-bit mono PCM WAV file.
        internal static byte[] Wave(FeedbackSound sound)
        {
            short[] samples = Samples(sound);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples.Length * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples.Length * 2);
                foreach (short sample in samples)
                    writer.Write(sample);
                return stream.ToArray();
            }
        }

        private static short[] Samples(FeedbackSound sound)
        {
            switch (sound)
            {
                case FeedbackSound.Live: return Notes((660, 70), (0, 15), (990, 90));
                case FeedbackSound.Muted: return Notes((990, 70), (0, 15), (587, 110));
                default: return Notes((330, 110), (0, 60), (330, 110));
            }
        }

        // Each note: frequency (0 = silence) and length in milliseconds.
        private static short[] Notes(params (double Frequency, int Ms)[] notes)
        {
            var samples = new List<short>();
            foreach (var note in notes)
            {
                int count = SampleRate * note.Ms / 1000;
                int attack = SampleRate * 4 / 1000;
                for (int i = 0; i < count; i++)
                {
                    if (note.Frequency <= 0)
                    {
                        samples.Add(0);
                        continue;
                    }
                    double t = (double)i / SampleRate;
                    // Soft start, exponential fade: a short "blip" without clicks.
                    double envelope = Math.Min(1.0, (double)i / attack) * Math.Exp(-3.5 * i / count) * Math.Min(1.0, (count - i) / (double)attack);
                    double wave = Math.Sin(2 * Math.PI * note.Frequency * t) + 0.25 * Math.Sin(4 * Math.PI * note.Frequency * t);
                    samples.Add((short)(wave / 1.25 * envelope * 0.30 * short.MaxValue));
                }
            }
            return samples.ToArray();
        }
    }
}
