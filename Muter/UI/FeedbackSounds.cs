using System;
using System.Collections.Generic;
using System.IO;

namespace Muter.UI
{
    internal enum FeedbackSound
    {
        Live,    // rising: the microphone is on
        Muted,   // falling: every controlled microphone is verified muted
        Warning, // low double buzz: the state could not be verified (treat it as on)
    }

    // Short tones played after a mute or unmute. They are generated here (no sound files) and played by SoundOutput.
    internal static class FeedbackSounds
    {
        private const int WaveSampleRate = 44100;
        private static readonly Lazy<SoundOutput> output = new Lazy<SoundOutput>(() => new SoundOutput());

        public static void Play(FeedbackSound sound) => output.Value.Play(sound);

        // Gets the output ready ahead of time, so even the first sound is not late.
        public static void Prepare() => output.Value.Prepare();

        public static void Release()
        {
            if (output.IsValueCreated)
                output.Value.Release();
        }

        // 16-bit mono PCM WAV file (the PlaySound fallback).
        internal static byte[] Wave(FeedbackSound sound)
        {
            float[] samples = Samples(sound, WaveSampleRate);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples.Length * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(WaveSampleRate);
                writer.Write(WaveSampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples.Length * 2);
                foreach (float sample in samples)
                    writer.Write((short)(sample * short.MaxValue));
                return stream.ToArray();
            }
        }

        // Mono samples (-1..1) at the given rate.
        internal static float[] Samples(FeedbackSound sound, int sampleRate)
        {
            switch (sound)
            {
                case FeedbackSound.Live: return Notes(sampleRate, (660, 70), (0, 15), (990, 90));
                case FeedbackSound.Muted: return Notes(sampleRate, (990, 70), (0, 15), (587, 110));
                default: return Notes(sampleRate, (330, 110), (0, 60), (330, 110));
            }
        }

        // Each note: frequency (0 = silence) and length in milliseconds.
        private static float[] Notes(int sampleRate, params (double Frequency, int Ms)[] notes)
        {
            var samples = new List<float>();
            int attack = sampleRate * 4 / 1000;
            foreach (var note in notes)
            {
                int count = sampleRate * note.Ms / 1000;
                for (int i = 0; i < count; i++)
                {
                    if (note.Frequency <= 0)
                    {
                        samples.Add(0);
                        continue;
                    }
                    double t = (double)i / sampleRate;
                    // Soft start, exponential fade: a short "blip" without clicks.
                    double envelope = Math.Min(1.0, (double)i / attack) * Math.Exp(-3.5 * i / count) * Math.Min(1.0, (count - i) / (double)attack);
                    double wave = Math.Sin(2 * Math.PI * note.Frequency * t) + 0.25 * Math.Sin(4 * Math.PI * note.Frequency * t);
                    samples.Add((float)(wave / 1.25 * envelope * 0.30));
                }
            }
            return samples.ToArray();
        }
    }
}
