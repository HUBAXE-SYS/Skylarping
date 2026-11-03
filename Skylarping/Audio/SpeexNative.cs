/*==========================================================*/
// SpeexDSP P/Invoke bindings (libspeexdsp.dll, BSD-3-Clause,
// see ThirdParty/speexdsp/COPYING).
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Runtime.InteropServices;

namespace Skylarping.Audio
{
    /// <summary>
    /// Raw bindings to libspeexdsp. Call <see cref="EnsureLoaded"/> before anything else so the
    /// DLL matching the process architecture (x86\ or x64\ next to the app) gets loaded.
    /// </summary>
    internal static class SpeexNative
    {
        private const string Dll = "libspeexdsp.dll";

        // speex_echo.h
        internal const int SPEEX_ECHO_SET_SAMPLING_RATE = 24;

        // speex_preprocess.h
        internal const int SPEEX_PREPROCESS_SET_DENOISE = 0;
        internal const int SPEEX_PREPROCESS_SET_AGC = 2;
        internal const int SPEEX_PREPROCESS_SET_VAD = 4;
        internal const int SPEEX_PREPROCESS_SET_NOISE_SUPPRESS = 18;
        internal const int SPEEX_PREPROCESS_SET_ECHO_SUPPRESS = 20;
        internal const int SPEEX_PREPROCESS_SET_ECHO_SUPPRESS_ACTIVE = 22;
        internal const int SPEEX_PREPROCESS_SET_ECHO_STATE = 24;

        /// <summary>Loads x86\ or x64\libspeexdsp.dll from the application folder.</summary>
        internal static void EnsureLoaded()
        {
            NativeLoader.Ensure(Dll);
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr speex_echo_state_init(int frame_size, int filter_length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void speex_echo_state_destroy(IntPtr st);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void speex_echo_capture(IntPtr st, [In] short[] rec, [Out] short[] output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void speex_echo_playback(IntPtr st, [In] short[] play);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void speex_echo_state_reset(IntPtr st);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int speex_echo_ctl(IntPtr st, int request, ref int value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr speex_preprocess_state_init(int frame_size, int sampling_rate);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void speex_preprocess_state_destroy(IntPtr st);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int speex_preprocess_run(IntPtr st, [In, Out] short[] x);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int speex_preprocess_ctl(IntPtr st, int request, ref int value);

        /// <summary>Overload for SPEEX_PREPROCESS_SET_ECHO_STATE, which takes the echo state pointer itself.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int speex_preprocess_ctl(IntPtr st, int request, IntPtr value);
    }
}
