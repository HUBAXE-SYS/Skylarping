/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Audio in the format speech plays in. Servers answer in their own (OpenMOSS-TTS 24 kHz mono,
// MOSS-TTS-Local 48 kHz stereo), so everything is converted here. The resampler is NAudio's
// managed one, which unlike Media Foundation also runs on XP.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Skylarping.Audio
{
    public sealed class VoiceSampleException : Exception
    {
        public VoiceSampleException(string message) : base(message) { }
    }

    public static class SpeechAudio
    {
        public const int SampleRate = 24000;
        public const string SampleFilter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3";

        private const int MinSampleSeconds = 1;
        private const int MaxSampleSeconds = 30; // sent with every request, so it has to stay small

        /// <summary>A WAV file as samples in the format speech plays in.</summary>
        public static short[] Decode(byte[] wav)
        {
            using (var reader = new WaveFileReader(new MemoryStream(wav)))
                return Convert(reader.ToSampleProvider(), int.MaxValue);
        }

        /// <summary>
        /// Turns an audio file into the WAV clip kept as a character's voice sample.
        /// Throws <see cref="VoiceSampleException"/> if it can't be used.
        /// </summary>
        /// <param name="trimmed">Set when the file was longer than a sample can be.</param>
        public static byte[] LoadSample(string path, out bool trimmed)
        {
            short[] samples;
            try
            {
                bool mp3 = string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase);
                using (WaveStream reader = mp3 ? (WaveStream)new Mp3FileReader(path) : new WaveFileReader(path))
                    samples = Convert(reader.ToSampleProvider(), (MaxSampleSeconds + 1) * SampleRate); // to tell if any was cut
            }
            catch (VoiceSampleException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new VoiceSampleException("That audio file couldn't be read: " + ex.Message);
            }

            return MakeSample(samples, out trimmed);
        }

        /// <summary>How long a kept sample is. Only reads its size, so a damaged file can't stop the editor opening.</summary>
        public static double SampleSeconds(byte[] sample)
        {
            return Math.Max(0, sample.Length - WavFile.HeaderBytes) / 2.0 / SampleRate;
        }

        /// <summary>Same as <see cref="LoadSample"/>, for a WAV file the server made.</summary>
        public static byte[] SampleFromSpeech(byte[] wav)
        {
            bool trimmed;
            return MakeSample(Decode(wav), out trimmed);
        }

        private static byte[] MakeSample(short[] samples, out bool trimmed)
        {
            if (samples.Length < MinSampleSeconds * SampleRate)
                throw new VoiceSampleException("A voice sample has to be at least " + MinSampleSeconds + " second long.");

            trimmed = samples.Length > MaxSampleSeconds * SampleRate;
            if (trimmed)
                Array.Resize(ref samples, MaxSampleSeconds * SampleRate);
            return WavFile.FromPcm(ToPcm(samples), SampleRate);
        }

        public static byte[] ToPcm(short[] samples)
        {
            var pcm = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
            return pcm;
        }

        private static short[] Convert(ISampleProvider source, int maxSamples)
        {
            if (source.WaveFormat.Channels == 2)
                source = new StereoToMonoSampleProvider(source);
            else if (source.WaveFormat.Channels != 1)
                throw new VoiceSampleException("Only mono and stereo audio can be used.");
            if (source.WaveFormat.SampleRate != SampleRate)
                source = new WdlResamplingSampleProvider(source, SampleRate);

            var samples = new List<short>();
            var buffer = new float[SampleRate / 4];
            int count;
            while (samples.Count < maxSamples && (count = source.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < count; i++)
                    samples.Add((short)(Math.Max(-1f, Math.Min(1f, buffer[i])) * short.MaxValue));
            return samples.ToArray();
        }

        /// <summary>Plays samples on the chosen speakers, returning when they end or <paramref name="cancel"/> is set.</summary>
        public static void Play(short[] samples, CancellationToken cancel)
        {
            using (WaveOutEvent output = AudioDevices.CreateOutput())
            using (var stream = new RawSourceWaveStream(new MemoryStream(ToPcm(samples)), new WaveFormat(SampleRate, 16, 1)))
            {
                output.Init(stream);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing && !cancel.IsCancellationRequested)
                    Thread.Sleep(20);
                output.Stop();
            }
        }
    }
}
