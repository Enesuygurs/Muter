using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using Muter.Audio;

namespace Muter.UI
{
    // Plays the feedback sounds through WASAPI on its own thread, without delay.
    //
    // Opening an output stream is what makes a sound late (measured 300-450 ms on a USB headset, PlaySound has the
    // same delay), while starting one that is already open takes a few milliseconds. So the stream is opened once and
    // kept ready but stopped: a stopped stream plays nothing and does not keep the computer awake. Each sound is
    // written into it and started, then it is stopped again. It is opened again when the default output changes, and
    // if anything fails the sound is played with PlaySound instead.
    internal sealed class SoundOutput
    {
        private const long BufferDuration = 5000000; // 500 ms in 100 ns units, longer than any sound
        private const uint CLSCTX_ALL = 0x17;

        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly Dictionary<FeedbackSound, byte[]> rendered = new Dictionary<FeedbackSound, byte[]>();
        private readonly Dictionary<FeedbackSound, SoundPlayer> fallbackPlayers = new Dictionary<FeedbackSound, SoundPlayer>();
        private readonly Timer stopTimer;
        private IMMDeviceEnumerator enumerator;
        private IAudioClient client;
        private IAudioRenderClient render;
        private string deviceId;
        private Format format;
        private int playing; // counts the sounds started
        private int stopFor; // the sound the pending stop belongs to

        public SoundOutput()
        {
            stopTimer = new Timer(_ =>
            {
                int sound = Volatile.Read(ref stopFor);
                Enqueue(() => StopIfFinished(sound));
            }, null, Timeout.Infinite, Timeout.Infinite);
            var thread = new Thread(Run) { IsBackground = true, Name = "Muter sound" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        // Opens the stream ahead of time, so the first sound is not late either.
        public void Prepare() => Enqueue(() => Safely(() => EnsureClient()));

        public void Release() => Enqueue(ReleaseClient);

        public void Play(FeedbackSound sound) => Enqueue(() => PlayNow(sound));

        private void Enqueue(Action action)
        {
            try
            {
                queue.Add(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Run()
        {
            foreach (Action action in queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Warn("Sound output failed", ex);
                }
            }
        }

        private void PlayNow(FeedbackSound sound)
        {
            try
            {
                if (EnsureClient())
                {
                    if (!rendered.TryGetValue(sound, out byte[] data))
                        rendered[sound] = data = format.Encode(FeedbackSounds.Samples(sound, format.SampleRate));

                    Check(client.Stop(), "Stop");
                    Check(client.Reset(), "Reset");
                    Check(client.GetBufferSize(out uint bufferFrames), "GetBufferSize");
                    uint frames = Math.Min(bufferFrames, (uint)(data.Length / format.BlockAlign));
                    Check(render.GetBuffer(frames, out IntPtr buffer), "GetBuffer");
                    Marshal.Copy(data, 0, buffer, (int)frames * format.BlockAlign);
                    Check(render.ReleaseBuffer(frames, 0), "ReleaseBuffer");
                    Check(client.Start(), "Start");

                    Volatile.Write(ref stopFor, ++playing);
                    stopTimer.Change((int)(frames * 1000L / format.SampleRate) + 100, Timeout.Infinite);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not play the feedback sound through WASAPI; using PlaySound", ex);
                ReleaseClient();
            }
            PlayFallback(sound);
        }

        // A stop that fired just as the next sound was started must not cut that one off.
        private void StopIfFinished(int sound)
        {
            if (client == null || sound != playing)
                return;
            Safely(() =>
            {
                client.Stop();
                client.Reset();
            });
        }

        // True when the stream of the current default output is open.
        private bool EnsureClient()
        {
            if (enumerator == null)
                enumerator = CoreAudioInterop.CreateDeviceEnumerator();

            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out IMMDevice device);
            if (hr == CoreAudioInterop.E_NOTFOUND || device == null)
            {
                ReleaseClient();
                return false; // no output device
            }
            Check(hr, "GetDefaultAudioEndpoint");
            try
            {
                Check(device.GetId(out IntPtr idPointer), "GetId");
                string id = Marshal.PtrToStringUni(idPointer);
                AudioClientInterop.CoTaskMemFree(idPointer);
                if (client != null && id == deviceId)
                    return true;

                ReleaseClient();
                Guid iid = AudioClientInterop.IID_IAudioClient;
                Check(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object instance), "Activate(IAudioClient)");
                client = (IAudioClient)instance;
                Check(client.GetMixFormat(out IntPtr mix), "GetMixFormat");
                try
                {
                    format = Format.Read(mix);
                    if (format == null)
                    {
                        Log.Warn("The output format is not supported for the feedback sounds; using PlaySound");
                        ReleaseClient();
                        return false;
                    }
                    Check(client.Initialize(AudioClientInterop.AUDCLNT_SHAREMODE_SHARED, AudioClientInterop.AUDCLNT_STREAMFLAGS_NOPERSIST, BufferDuration, 0, mix, IntPtr.Zero), "Initialize");
                }
                finally
                {
                    AudioClientInterop.CoTaskMemFree(mix);
                }
                Guid renderIid = AudioClientInterop.IID_IAudioRenderClient;
                Check(client.GetService(ref renderIid, out object service), "GetService(IAudioRenderClient)");
                render = (IAudioRenderClient)service;
                deviceId = id;
                rendered.Clear();
                return true;
            }
            catch
            {
                ReleaseClient();
                throw;
            }
            finally
            {
                CoreAudioInterop.Release(device);
            }
        }

        private void ReleaseClient()
        {
            if (client != null)
                Safely(() => client.Stop());
            CoreAudioInterop.Release(render);
            CoreAudioInterop.Release(client);
            render = null;
            client = null;
            deviceId = null;
            rendered.Clear();
        }

        private void PlayFallback(FeedbackSound sound)
        {
            Safely(() =>
            {
                if (!fallbackPlayers.TryGetValue(sound, out SoundPlayer player))
                {
                    player = new SoundPlayer(new MemoryStream(FeedbackSounds.Wave(sound)));
                    player.Load();
                    fallbackPlayers[sound] = player;
                }
                player.Play();
            });
        }

        private static void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Warn("Sound output failed", ex);
            }
        }

        private static void Check(int hr, string call)
        {
            if (!CoreAudioInterop.Succeeded(hr))
                throw new COMException(call + " failed", hr);
        }

        // The shared-mode mix format of the output: 32-bit float or 16/24/32-bit PCM.
        private sealed class Format
        {
            public int SampleRate { get; private set; }
            public int Channels { get; private set; }
            public int BitsPerSample { get; private set; }
            public bool IsFloat { get; private set; }
            public int BlockAlign { get; private set; }

            public static Format Read(IntPtr waveFormat)
            {
                ushort tag = (ushort)Marshal.ReadInt16(waveFormat, 0);
                var format = new Format
                {
                    Channels = Marshal.ReadInt16(waveFormat, 2),
                    SampleRate = Marshal.ReadInt32(waveFormat, 4),
                    BlockAlign = Marshal.ReadInt16(waveFormat, 12),
                    BitsPerSample = Marshal.ReadInt16(waveFormat, 14),
                };
                if (tag == AudioClientInterop.WAVE_FORMAT_EXTENSIBLE)
                {
                    var subtype = (Guid)Marshal.PtrToStructure(waveFormat + 24, typeof(Guid));
                    tag = subtype == AudioClientInterop.SubtypeFloat ? AudioClientInterop.WAVE_FORMAT_IEEE_FLOAT
                        : subtype == AudioClientInterop.SubtypePcm ? AudioClientInterop.WAVE_FORMAT_PCM
                        : (ushort)0;
                }
                format.IsFloat = tag == AudioClientInterop.WAVE_FORMAT_IEEE_FLOAT && format.BitsPerSample == 32;
                bool pcm = tag == AudioClientInterop.WAVE_FORMAT_PCM && (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32);
                bool valid = format.Channels > 0 && format.SampleRate > 0 && format.BlockAlign == format.Channels * format.BitsPerSample / 8;
                return valid && (format.IsFloat || pcm) ? format : null;
            }

            // Mono samples (-1..1) into interleaved frames of this format, the same value on every channel.
            public byte[] Encode(float[] samples)
            {
                int bytes = BitsPerSample / 8;
                var data = new byte[samples.Length * BlockAlign];
                int offset = 0;
                foreach (float sample in samples)
                {
                    byte[] value = IsFloat
                        ? BitConverter.GetBytes(sample)
                        : BitConverter.GetBytes((int)Math.Round(Math.Max(-1f, Math.Min(1f, sample)) * ((1L << (BitsPerSample - 1)) - 1)) << (32 - BitsPerSample));
                    for (int channel = 0; channel < Channels; channel++)
                    {
                        // PCM: the top bytes of the left-aligned 32-bit value (little endian).
                        Buffer.BlockCopy(value, IsFloat ? 0 : 4 - bytes, data, offset, bytes);
                        offset += bytes;
                    }
                }
                return data;
            }
        }
    }
}
