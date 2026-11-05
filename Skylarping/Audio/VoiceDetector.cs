/*==========================================================*/
// Voice activity detection for voice calls, built on
// libfvad (BSD-3-Clause, see ThirdParty/libfvad/LICENSE).
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Runtime.InteropServices;

namespace Skylarping.Audio
{
    /// <summary>How eagerly frames are classified as non-speech.</summary>
    public enum VoiceDetectionMode
    {
        /// <summary>Least aggressive: reports speech most readily.</summary>
        Quality = 0,
        LowBitrate = 1,
        Aggressive = 2,
        /// <summary>Most aggressive: fewest false positives, may clip quiet speech.</summary>
        VeryAggressive = 3,
    }

    /// <summary>
    /// WebRTC's voice activity detector (via libfvad): tells whether a frame of audio contains speech.
    /// Feed it the cleaned output of <see cref="EchoCanceller.Capture"/> to detect the user talking
    /// (and interrupting) without reacting to the AI's own voice from the speakers.
    /// </summary>
    /// <remarks>
    /// Input is 16-bit mono PCM at 8, 16, 32 or 48 kHz, in frames of exactly 10, 20 or 30 ms.
    /// Each frame is judged on its own, so for interruption decisions require a few speech frames
    /// in a row (e.g. 150-250 ms) before acting.
    /// </remarks>
    public sealed class VoiceDetector : IDisposable
    {
        private const string Dll = "libfvad.dll";

        private readonly object _lock = new object();
        private IntPtr _inst;
        private VoiceDetectionMode _mode;

        /// <summary>Sample rate in Hz.</summary>
        public int SampleRate { get; private set; }

        public VoiceDetector(int sampleRate = 16000, VoiceDetectionMode mode = VoiceDetectionMode.Aggressive)
        {
            NativeLoader.Ensure(Dll);

            _inst = fvad_new();
            if (_inst == IntPtr.Zero)
                throw new InvalidOperationException("fvad_new failed.");

            if (fvad_set_sample_rate(_inst, sampleRate) != 0)
            {
                Free();
                throw new ArgumentOutOfRangeException("sampleRate", "Supported sample rates are 8000, 16000, 32000 and 48000 Hz.");
            }
            SampleRate = sampleRate;
            Mode = mode;
        }

        /// <summary>Detection aggressiveness (default <see cref="VoiceDetectionMode.Aggressive"/>).</summary>
        public VoiceDetectionMode Mode
        {
            set
            {
                lock (_lock)
                {
                    ThrowIfDisposed();
                    if (fvad_set_mode(_inst, (int)value) != 0)
                        throw new ArgumentOutOfRangeException("value");
                    _mode = value;
                }
            }
        }

        /// <summary>Returns true if the frame (10, 20 or 30 ms of samples) contains speech.</summary>
        public bool IsSpeech(short[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");

            lock (_lock)
            {
                ThrowIfDisposed();
                int result = fvad_process(_inst, frame, (UIntPtr)frame.Length);
                if (result < 0)
                    throw new ArgumentException(
                        "Frame must be 10, 20 or 30 ms long (" + (SampleRate / 100) + ", " + (SampleRate / 50) + " or " +
                        (SampleRate * 3 / 100) + " samples at " + SampleRate + " Hz); got " + frame.Length + ".", "frame");
                return result == 1;
            }
        }

        /// <summary>Clears the detector's adaptive state (keeps mode and sample rate).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                fvad_reset(_inst); // also puts it back to 8 kHz and the default mode
                fvad_set_sample_rate(_inst, SampleRate);
                fvad_set_mode(_inst, (int)_mode);
            }
        }

        public void Dispose()
        {
            Free();
            GC.SuppressFinalize(this);
        }

        ~VoiceDetector()
        {
            Free();
        }

        private void Free()
        {
            lock (_lock)
            {
                if (_inst != IntPtr.Zero)
                {
                    fvad_free(_inst);
                    _inst = IntPtr.Zero;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_inst == IntPtr.Zero)
                throw new ObjectDisposedException("VoiceDetector");
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr fvad_new();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void fvad_free(IntPtr inst);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void fvad_reset(IntPtr inst);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int fvad_set_mode(IntPtr inst, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int fvad_set_sample_rate(IntPtr inst, int sampleRate);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int fvad_process(IntPtr inst, [In] short[] frame, UIntPtr length);
    }
}
