/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// What the echo canceller needs to know about the speakers: the speech as 20 ms frames in the
// microphone's format.
//
// Pairing is by count, not by clock. The output and the microphone both run for the whole
// call, so the Nth frame played stays a fixed distance from the Nth frame captured however
// unevenly waveIn and waveOut deliver their buffers. The microphone has to start first: the
// echo then always arrives after the frame it's paired with, which is the only way round
// the canceller can remove it.
/*==========================================================*/

using System;
using System.Collections.Generic;

namespace Skylarping.Audio
{
    /// <summary>Turns speech samples into microphone-format frames. One thread at a time.</summary>
    public sealed class EchoFramer
    {
        private readonly int _frameSamples;
        private readonly Action<short[]> _onFrame;
        private short[] _frame;
        private int _filled;
        private int _phase;
        private int _older, _old; // the two samples before the current one
        private int _kept;

        public EchoFramer(int frameSamples, Action<short[]> onFrame)
        {
            _frameSamples = frameSamples;
            _onFrame = onFrame;
            _frame = new short[frameSamples];
        }

        /// <summary>Takes samples at 24 kHz. Three become two, for 16 kHz.</summary>
        public void Push(short[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int filtered = (_older + 2 * _old + samples[i]) / 4; // keeps out what can't exist at 16 kHz
                _older = _old;
                _old = samples[i];

                if (_phase == 0)
                    Emit(filtered);
                else if (_phase == 1)
                    _kept = filtered;
                else
                    Emit((_kept + filtered) / 2);
                _phase = (_phase + 1) % 3;
            }
        }

        private void Emit(int sample)
        {
            _frame[_filled++] = (short)sample;
            if (_filled == _frameSamples)
            {
                _onFrame(_frame);
                _frame = new short[_frameSamples];
                _filled = 0;
            }
        }
    }

    /// <summary>The played frames waiting for their microphone frame.</summary>
    public sealed class EchoQueue
    {
        private const int MaxFrames = 100; // two seconds; only reached by drifting clocks

        private readonly Queue<short[]> _frames = new Queue<short[]>();

        public void Push(short[] frame)
        {
            lock (_frames)
            {
                _frames.Enqueue(frame);
                if (_frames.Count > MaxFrames)
                    _frames.Dequeue();
            }
        }

        /// <summary>The next played frame, or null before the speakers have started.</summary>
        public short[] Next()
        {
            lock (_frames)
                return _frames.Count > 0 ? _frames.Dequeue() : null;
        }

        /// <summary>Forgets the newest <paramref name="count"/> frames: they were cut before they played.</summary>
        public void DropLast(int count)
        {
            lock (_frames)
            {
                short[][] frames = _frames.ToArray();
                _frames.Clear();
                for (int i = 0; i < frames.Length - count; i++)
                    _frames.Enqueue(frames[i]);
            }
        }
    }
}
