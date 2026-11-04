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
    }
}
