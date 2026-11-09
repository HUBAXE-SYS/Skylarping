/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Sample rate conversion for a live stream, on NAudio's managed WDL resampler (sinc mode).
//
// Calls open both sound devices at the same rate (48 kHz where they can, see AudioDevices) and
// convert here, so Windows never resamples. That matters for echo cancellation: XP's KMixer
// converts by linear interpolation, which distorts the echo in a way the canceller can't
// follow. The microphone and the echo reference each run their own converter with the same
// settings, so the same input always gives the same output and the two stay sample for sample
// alike.
/*==========================================================*/

using System;
using NAudio.Dsp;

namespace Skylarping.Audio
{
    public sealed class RateConverter
    {
        private const int SincSize = 64; // taps; 64 keeps the band above 8 kHz well out of the 16 kHz stream

        private readonly WdlResampler _resampler; // null when nothing has to change
        private readonly int _inputRate, _outputRate;
        private float[] _out = new float[0];

        public RateConverter(int inputRate, int outputRate)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException("inputRate");
            if (outputRate <= 0) throw new ArgumentOutOfRangeException("outputRate");
            _inputRate = inputRate;
            _outputRate = outputRate;
            if (inputRate == outputRate)
                return;
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 0, true, SincSize, 32);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true); // we say how much input there is, it makes what it can
            _resampler.SetRates(inputRate, outputRate);
        }

        public int InputRate { get { return _inputRate; } }
        public int OutputRate { get { return _outputRate; } }

        /// <summary>
        /// Converts <paramref name="count"/> samples. The result goes into <paramref name="output"/>,
        /// which grows as needed; returns how many samples were written. A sinc filter needs to see
        /// a little ahead, so the output trails the input by a fixed few samples.
        /// </summary>
        public int Convert(short[] input, int count, ref short[] output)
        {
            if (_resampler == null)
            {
                if (output == null || output.Length < count)
                    output = new short[count];
                Array.Copy(input, output, count);
                return count;
            }

            float[] inBuffer;
            int inOffset;
            int wanted = _resampler.ResamplePrepare(count, 1, out inBuffer, out inOffset);
            for (int i = 0; i < wanted; i++)
                inBuffer[inOffset + i] = input[i] / 32768f;

            int most = (int)((long)count * _outputRate / _inputRate) + SincSize;
            if (_out.Length < most)
                _out = new float[most];
            int produced = _resampler.ResampleOut(_out, 0, wanted, most, 1);

            if (output == null || output.Length < produced)
                output = new short[Math.Max(produced, count)];
            for (int i = 0; i < produced; i++)
            {
                float sample = _out[i] * 32768f;
                output[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(sample)));
            }
            return produced;
        }
    }
}
