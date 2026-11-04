/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;

namespace Skylarping.Audio
{
    /// <summary>
    /// Cuts the arbitrary buffers waveIn delivers into the exact-size frames libfvad and SpeexDSP
    /// need. A buffer may end mid-frame (or even mid-sample); the rest carries over to the next one.
    /// </summary>
    public sealed class FrameSlicer
    {
        private readonly short[] _partial;
        private int _filled;
        private int _lowByte = -1; // first half of a sample split across two buffers

        public FrameSlicer(int samplesPerFrame)
        {
            _partial = new short[samplesPerFrame];
        }

        public void Push(byte[] buffer, int count, Action<short[]> onFrame)
        {
            for (int i = 0; i < count; i++)
            {
                if (_lowByte < 0)
                {
                    _lowByte = buffer[i];
                    continue;
                }

                _partial[_filled++] = (short)(_lowByte | (buffer[i] << 8));
                _lowByte = -1;

                if (_filled == _partial.Length)
                {
                    onFrame((short[])_partial.Clone());
                    _filled = 0;
                }
            }
        }
    }
}
