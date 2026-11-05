/*==========================================================*/
// Acoustic echo cancellation for voice calls, built on
// SpeexDSP (BSD-3-Clause, see ThirdParty/speexdsp/COPYING).
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;

namespace Skylarping.Audio
{
    /// <summary>
    /// Removes the far end's audio (e.g. AI speech coming out of the speakers) from the microphone
    /// signal, then cleans the result up with SpeexDSP's preprocessor (residual echo suppression,
    /// noise suppression, optional gain control and a basic speech detector).
    /// </summary>
    /// <remarks>
    /// Audio is 16-bit mono PCM in fixed-size frames of <see cref="FrameSize"/> samples.
    /// Typical use with NAudio at 16 kHz and 20 ms frames:
    /// <list type="bullet">
    /// <item>call <see cref="Playback(short[])"/> with every frame as it is handed to the speaker output;</item>
    /// <item>call <see cref="Capture(short[], short[])"/> with every microphone frame and use the cleaned
    /// output for speech detection / speech-to-text.</item>
    /// </list>
    /// The two may be called from different threads.
    /// </remarks>
    public sealed class EchoCanceller : IDisposable
    {
        private readonly object _lock = new object();
        private IntPtr _echo;
        private IntPtr _preprocess;

        /// <summary>Sample rate in Hz (e.g. 16000).</summary>
        public int SampleRate { get; private set; }

        /// <summary>Samples per frame. Every buffer passed in must be exactly this long.</summary>
        public int FrameSize { get; private set; }

        /// <param name="sampleRate">Sample rate in Hz; 16000 is a good choice for speech.</param>
        /// <param name="frameMilliseconds">Frame length; 10-20 ms is recommended.</param>
        /// <param name="tailMilliseconds">
        /// How long an echo the canceller can remove (speaker-to-mic delay plus room reverb).
        /// 200-500 ms suits desktop speakers; longer adapts more slowly.
        /// </param>
        public EchoCanceller(int sampleRate = 16000, int frameMilliseconds = 20, int tailMilliseconds = 300)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException("sampleRate");
            if (frameMilliseconds <= 0) throw new ArgumentOutOfRangeException("frameMilliseconds");
            if (tailMilliseconds <= 0) throw new ArgumentOutOfRangeException("tailMilliseconds");

            SpeexNative.EnsureLoaded();

            SampleRate = sampleRate;
            FrameSize = sampleRate * frameMilliseconds / 1000;
            int filterLength = sampleRate * tailMilliseconds / 1000;

            _echo = SpeexNative.speex_echo_state_init(FrameSize, filterLength);
            if (_echo == IntPtr.Zero)
                throw new InvalidOperationException("speex_echo_state_init failed.");

            int rate = sampleRate;
            SpeexNative.speex_echo_ctl(_echo, SpeexNative.SPEEX_ECHO_SET_SAMPLING_RATE, ref rate);

            _preprocess = SpeexNative.speex_preprocess_state_init(FrameSize, sampleRate);
            if (_preprocess == IntPtr.Zero)
            {
                SpeexNative.speex_echo_state_destroy(_echo);
                _echo = IntPtr.Zero;
                throw new InvalidOperationException("speex_preprocess_state_init failed.");
            }

            // Link the preprocessor to the echo canceller so it also suppresses residual echo.
            SpeexNative.speex_preprocess_ctl(_preprocess, SpeexNative.SPEEX_PREPROCESS_SET_ECHO_STATE, _echo);

            NoiseSuppression = true;
            SpeechDetection = true;
        }

        /// <summary>Speex noise suppression (on by default).</summary>
        public bool NoiseSuppression
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_DENOISE, value ? 1 : 0); }
        }

        /// <summary>Automatic gain control (off by default).</summary>
        public bool AutomaticGain
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_AGC, value ? 1 : 0); }
        }

        /// <summary>
        /// Speex's built-in speech detector, reported by <see cref="Capture"/> (on by default).
        /// It's fairly basic; run <see cref="VoiceDetector"/> on the cleaned output for real decisions.
        /// </summary>
        public bool SpeechDetection
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_VAD, value ? 1 : 0); }
        }

        /// <summary>Maximum attenuation of noise in dB (negative; Speex default is -15).</summary>
        public int NoiseSuppressDb
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, value); }
        }

        /// <summary>Maximum attenuation of residual echo in dB (negative; Speex default is -40).</summary>
        public int EchoSuppressDb
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_ECHO_SUPPRESS, value); }
        }

        /// <summary>Maximum attenuation of residual echo in dB while the far end is talking (Speex default is -15).</summary>
        public int EchoSuppressActiveDb
        {
            set { SetPreprocess(SpeexNative.SPEEX_PREPROCESS_SET_ECHO_SUPPRESS_ACTIVE, value); }
        }

        /// <summary>Feed one frame that is being sent to the speakers (the echo reference).</summary>
        public void Playback(short[] frame)
        {
            CheckFrame(frame, "frame");
            lock (_lock)
            {
                ThrowIfDisposed();
                SpeexNative.speex_echo_playback(_echo, frame);
            }
        }

        /// <summary>
        /// Process one microphone frame. Writes the echo-cancelled, cleaned frame to
        /// <paramref name="output"/> and returns true if the preprocessor thinks it contains speech.
        /// </summary>
        public bool Capture(short[] micFrame, short[] output)
        {
            CheckFrame(micFrame, "micFrame");
            CheckFrame(output, "output");
            lock (_lock)
            {
                ThrowIfDisposed();
                SpeexNative.speex_echo_capture(_echo, micFrame, output);
                return SpeexNative.speex_preprocess_run(_preprocess, output) != 0;
            }
        }

        /// <summary>
        /// Process one microphone frame against the played frame paired with it, for callers doing
        /// their own pairing. <see cref="Playback"/> and <see cref="Capture"/> pair frames by arrival
        /// and only allow a few frames of slack, but waveIn and waveOut each deliver 100 ms or more
        /// at a time. See EchoReference.
        /// </summary>
        public bool Cancel(short[] micFrame, short[] playedFrame, short[] output)
        {
            CheckFrame(micFrame, "micFrame");
            CheckFrame(playedFrame, "playedFrame");
            CheckFrame(output, "output");
            lock (_lock)
            {
                ThrowIfDisposed();
                SpeexNative.speex_echo_cancellation(_echo, micFrame, playedFrame, output);
                return SpeexNative.speex_preprocess_run(_preprocess, output) != 0;
            }
        }

        /// <summary>Forget everything learned about the echo path (e.g. after the audio device changes).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                SpeexNative.speex_echo_state_reset(_echo);
            }
        }

        /// <summary>Converts little-endian 16-bit PCM bytes (as NAudio delivers them) into samples.</summary>
        public static void BytesToSamples(byte[] source, int offset, short[] destination)
        {
            Buffer.BlockCopy(source, offset, destination, 0, destination.Length * 2);
        }

        /// <summary>Converts samples back into little-endian 16-bit PCM bytes.</summary>
        public static void SamplesToBytes(short[] source, byte[] destination, int offset)
        {
            Buffer.BlockCopy(source, 0, destination, offset, source.Length * 2);
        }

        public void Dispose()
        {
            Free();
            GC.SuppressFinalize(this);
        }

        ~EchoCanceller()
        {
            Free();
        }

        private void Free()
        {
            lock (_lock)
            {
                // The preprocessor references the echo state, so destroy it first.
                if (_preprocess != IntPtr.Zero)
                {
                    SpeexNative.speex_preprocess_state_destroy(_preprocess);
                    _preprocess = IntPtr.Zero;
                }
                if (_echo != IntPtr.Zero)
                {
                    SpeexNative.speex_echo_state_destroy(_echo);
                    _echo = IntPtr.Zero;
                }
            }
        }

        private void SetPreprocess(int request, int value)
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                SpeexNative.speex_preprocess_ctl(_preprocess, request, ref value);
            }
        }

        private void CheckFrame(short[] buffer, string name)
        {
            if (buffer == null)
                throw new ArgumentNullException(name);
            if (buffer.Length != FrameSize)
                throw new ArgumentException("Expected " + FrameSize + " samples, got " + buffer.Length + ".", name);
        }

        private void ThrowIfDisposed()
        {
            if (_echo == IntPtr.Zero)
                throw new ObjectDisposedException("EchoCanceller");
        }
    }
}
