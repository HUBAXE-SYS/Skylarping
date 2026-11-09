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
//
// The distance does slip, though: waveIn drops a buffer (XP does under load), waveOut runs
// dry for a moment, or a USB microphone and onboard speakers tick at slightly different
// rates. The queue watches its own depth for that and puts the pairing back (see EchoQueue).
/*==========================================================*/

using System;
using System.Collections.Generic;

namespace Skylarping.Audio
{
    /// <summary>Turns speech samples into microphone-format frames. One thread at a time.</summary>
    public sealed class EchoFramer
    {
        private readonly RateConverter _converter;
        private readonly int _frameSamples;
        private readonly Action<short[]> _onFrame;
        private short[] _converted = new short[0];
        private short[] _frame;
        private int _filled;

        /// <param name="inputRate">The rate of the samples pushed in: what the speakers are opened at.</param>
        /// <param name="frameSamples">Samples per frame at the microphone rate.</param>
        public EchoFramer(int inputRate, int frameSamples, Action<short[]> onFrame)
        {
            _converter = new RateConverter(inputRate, MicrophoneCapture.SampleRate);
            _frameSamples = frameSamples;
            _onFrame = onFrame;
            _frame = new short[frameSamples];
        }

        /// <summary>Takes samples at the input rate.</summary>
        public void Push(short[] samples, int count)
        {
            int produced = _converter.Convert(samples, count, ref _converted);
            for (int i = 0; i < produced; i++)
            {
                _frame[_filled++] = _converted[i];
                if (_filled == _frameSamples)
                {
                    _onFrame(_frame);
                    _frame = new short[_frameSamples];
                    _filled = 0;
                }
            }
        }
    }

    /// <summary>
    /// The played frames waiting for their microphone frame, with a watchdog on the pairing.
    /// </summary>
    /// <remarks>
    /// The depth goes up and down by one device buffer as waveOut is refilled, so the watchdog
    /// looks at the lowest depth over each one-second window. The first steady window sets the
    /// target. A window whose low point is above the target by more than the tolerance means the
    /// microphone lost frames: that many played frames are dropped, so the next microphone frame
    /// meets the echo it actually holds. Below the target means the speakers fell behind (the
    /// echo is late): that many microphone frames get silence as their reference instead of
    /// using up real ones, which holds the played frames back until the echo catches up.
    /// </remarks>
    public sealed class EchoQueue
    {
        private const int MaxFrames = 150;      // three seconds; only reached by something badly wrong
        public const int WindowFrames = 50;     // one second of microphone frames per check
        public const int Tolerance = 3;         // frames the low point may wander before it's corrected
        private const int SettleWindows = 2;    // windows before the target is taken, so the start-up fill isn't it

        private readonly Queue<short[]> _frames = new Queue<short[]>();
        private readonly int _frameSamples;
        private int _windowMin = int.MaxValue, _windowCount, _windows;
        private int _target = -1;
        private int _pad; // microphone frames still to get silence

        public EchoQueue() : this(MicrophoneCapture.SampleRate * MicrophoneCapture.FrameMilliseconds / 1000)
        {
        }

        public EchoQueue(int frameSamples)
        {
            _frameSamples = frameSamples;
        }

        // For the call log. Written under the lock; read them on the microphone thread.
        public long Pushed { get; private set; }
        public long Consumed { get; private set; }
        /// <summary>Frames the watchdog dropped: the microphone had lost that many.</summary>
        public long Dropped { get; private set; }
        /// <summary>Microphone frames the watchdog paired with silence: the speakers had fallen that far behind.</summary>
        public long Padded { get; private set; }
        /// <summary>Frames thrown away because nothing consumed them for seconds.</summary>
        public long Overflowed { get; private set; }
        /// <summary>The low point of the last full window, or -1.</summary>
        public int LastWindowMin { get; private set; }
        /// <summary>The depth the watchdog holds the low point to, or -1 until it's known.</summary>
        public int Target { get { lock (_frames) return _target; } }
        public int Depth { get { lock (_frames) return _frames.Count; } }

        public void Push(short[] frame)
        {
            lock (_frames)
            {
                Pushed++;
                _frames.Enqueue(frame);
                if (_frames.Count > MaxFrames)
                {
                    _frames.Dequeue();
                    Overflowed++;
                }
            }
        }

        /// <summary>The next played frame, or null before the speakers have started.</summary>
        public short[] Next()
        {
            lock (_frames)
            {
                if (Pushed == 0)
                    return null;
                Consumed++;
                if (_pad == 0)
                    Watch(_frames.Count); // while padding, the depth is on its way back: not a reading
                if (_pad > 0)
                {
                    _pad--;
                    return new short[_frameSamples];
                }
                return _frames.Count > 0 ? _frames.Dequeue() : new short[_frameSamples]; // ran dry: the watchdog will see
            }
        }

        private void Watch(int depth)
        {
            if (depth < _windowMin)
                _windowMin = depth;
            if (++_windowCount < WindowFrames)
                return;

            LastWindowMin = _windowMin;
            _windows++;
            if (_target < 0)
            {
                if (_windows >= SettleWindows)
                    _target = _windowMin;
            }
            else
            {
                int error = _windowMin - _target;
                if (error > Tolerance)
                {
                    for (int i = 0; i < error && _frames.Count > 0; i++)
                        _frames.Dequeue();
                    Dropped += error;
                }
                else if (error < -Tolerance)
                {
                    _pad += -error;
                    Padded += -error;
                }
            }
            _windowMin = int.MaxValue;
            _windowCount = 0;
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
