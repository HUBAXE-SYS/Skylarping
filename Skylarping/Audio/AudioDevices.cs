/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// The sound devices chosen in Options > Audio settings. Choices are stored by name because
// waveIn/waveOut numbers shift when hardware is plugged in or removed. An empty name, or a
// device that's gone, means the Windows default, which also lets Windows convert formats
// for devices that can't record 16 kHz themselves.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.Wave;
using Skylarping.Preferences;

namespace Skylarping.Audio
{
    public static class AudioDevices
    {
        /// <summary>WAVE_MAPPER: whatever Windows' default device is at the time.</summary>
        private const int WindowsDefault = -1;

        public static IList<string> Microphones()
        {
            var names = new List<string>();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                names.Add(WaveInEvent.GetCapabilities(i).ProductName);
            return names;
        }

        public static IList<string> Speakers()
        {
            var names = new List<string>();
            int count = WaveInterop.waveOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                WaveOutCapabilities caps;
                MmException.Try(WaveInterop.waveOutGetDevCaps((IntPtr)i, out caps, Marshal.SizeOf(typeof(WaveOutCapabilities))), "waveOutGetDevCaps");
                names.Add(caps.ProductName);
            }
            return names;
        }

        /// <summary>The number of the device called <paramref name="name"/>, or the Windows default.</summary>
        public static int Find(IList<string> devices, string name)
        {
            int index = string.IsNullOrEmpty(name) ? -1 : devices.IndexOf(name);
            return index >= 0 ? index : WindowsDefault;
        }

        public static int Microphone
        {
            get { return Find(Microphones(), Settings.MicrophoneDevice); }
        }

        /// <summary>A player on the chosen speakers.</summary>
        public static WaveOutEvent CreateOutput()
        {
            return new WaveOutEvent { DeviceNumber = Find(Speakers(), Settings.SpeakerDevice) };
        }

        /// <summary>A player on the device chosen for ringing.</summary>
        public static WaveOutEvent CreateRingOutput()
        {
            return new WaveOutEvent { DeviceNumber = Find(Speakers(), Settings.RingDevice) };
        }

        // ------------------------------------------------------------------ rates on calls

        /// <summary>The rate calls open the devices at unless a setting says otherwise.</summary>
        public const int PreferredCallRate = 48000;
        /// <summary>The other rate old hardware runs at.</summary>
        public const int AlternativeCallRate = 44100;

        /// <summary>The rates the microphone and the speakers are opened at on a call.</summary>
        public struct CallRates
        {
            public int Microphone;
            public int Speakers;
            /// <summary>Both devices run at one rate, so nothing in Windows converts either way.</summary>
            public bool Matched { get { return Microphone == Speakers; } }
        }

        /// <summary>
        /// Picks the rate for a call: the one in the settings, else 48 kHz, else 44.1 kHz, as long as
        /// both devices take it; otherwise each device at the rate the call's audio is in, and
        /// Windows converts as it did before.
        /// </summary>
        public static CallRates ChooseCallRates()
        {
            return ChooseCallRates(Settings.CallSampleRate, rate => Supports(true, rate), rate => Supports(false, rate));
        }

        public static CallRates ChooseCallRates(int preferred, Func<int, bool> microphoneTakes, Func<int, bool> speakersTake)
        {
            var candidates = new List<int>();
            if (preferred > 0)
                candidates.Add(preferred);
            if (!candidates.Contains(PreferredCallRate))
                candidates.Add(PreferredCallRate);
            if (!candidates.Contains(AlternativeCallRate))
                candidates.Add(AlternativeCallRate);
            foreach (int rate in candidates)
                if (microphoneTakes(rate) && speakersTake(rate))
                    return new CallRates { Microphone = rate, Speakers = rate };
            return new CallRates { Microphone = MicrophoneCapture.SampleRate, Speakers = SpeechAudio.SampleRate };
        }

        // WAVE_FORMAT_QUERY: waveInOpen/waveOutOpen only say whether they could, and open nothing.
        private const WaveInterop.WaveInOutOpenFlags FormatQuery = (WaveInterop.WaveInOutOpenFlags)1;

        /// <summary>Asks the chosen device (without opening it) whether it takes 16-bit mono at this rate.</summary>
        private static bool Supports(bool microphone, int rate)
        {
            var format = new WaveFormat(rate, 16, 1);
            IntPtr handle;
            MmResult result;
            try
            {
                result = microphone
                    ? WaveInterop.waveInOpen(out handle, (IntPtr)Microphone, format, null, IntPtr.Zero, FormatQuery)
                    : WaveInterop.waveOutOpen(out handle, (IntPtr)Find(Speakers(), Settings.SpeakerDevice), format, null, IntPtr.Zero, FormatQuery);
            }
            catch (Exception)
            {
                return false;
            }
            return result == MmResult.NoError;
        }
    }
}
