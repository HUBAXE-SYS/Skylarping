/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;

namespace Skylarping.Audio
{
    /// <summary>
    /// The chosen microphone as a stream of fixed-size frames, in the format calls use throughout:
    /// 16 kHz, 16-bit mono, 20 ms per frame.
    /// </summary>
    public sealed class MicrophoneCapture : IDisposable
    {
        public const int SampleRate = 16000;
        public const int FrameMilliseconds = 20;

        private const int SamplesPerFrame = SampleRate * FrameMilliseconds / 1000;
        private const int BufferMilliseconds = 100; // waveIn delivery; XP is unreliable much below this
        private const int StopWaitMilliseconds = 200;
        private const int StopAttempts = 10;

        private readonly WaveInEvent _input;
        private readonly FrameSlicer _slicer = new FrameSlicer(SamplesPerFrame);
        private readonly ManualResetEvent _stopped = new ManualResetEvent(false);
        private bool _started;

        public MicrophoneCapture()
        {
            // WaveInEvent posts RecordingStopped to the context it's created on. Dispose waits for
            // that event, so on the UI thread it would be waiting for itself; with no context the
            // event comes straight from the capture thread.
            SynchronizationContext context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                _input = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = BufferMilliseconds,
                    DeviceNumber = AudioDevices.Microphone,
                };
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }

            _input.DataAvailable += (s, e) =>
            {
                Action<short[]> frame = Frame;
                if (frame != null)
                    _slicer.Push(e.Buffer, e.BytesRecorded, frame);
            };
            _input.RecordingStopped += (s, e) =>
            {
                _stopped.Set();
                if (e.Exception != null)
                    Failed?.Invoke(e.Exception);
            };
        }

        /// <summary>A full frame arrived. Raised on the capture thread: don't block it.</summary>
        public event Action<short[]> Frame;

        /// <summary>Capture stopped on its own, e.g. the microphone was unplugged. Not on the UI thread.</summary>
        public event Action<Exception> Failed;

        public static bool Available
        {
            get { return WaveInEvent.DeviceCount > 0; }
        }

        public void Start()
        {
            _input.StartRecording();
            _started = true;
        }

        /// <summary>How loud a frame is, from 0 to 1, scaled so that speech shows up well on a meter.</summary>
        public static double Level(short[] frame)
        {
            int peak = 0;
            foreach (short sample in frame)
                peak = Math.Max(peak, Math.Abs((int)sample));
            return Math.Sqrt(Math.Min(1.0, peak / 32767.0));
        }

        /// <summary>Records for a fixed time (blocking) and returns the audio as PCM bytes.</summary>
        public static byte[] Record(int milliseconds)
        {
            var frames = new List<short[]>();
            using (var microphone = new MicrophoneCapture())
            {
                microphone.Frame += frames.Add;
                microphone.Start();
                Thread.Sleep(milliseconds);
            }
            return WavFile.ToPcm(frames);
        }

        public void Dispose()
        {
            if (_started)
            {
                // A stop right after Start can be overwritten as the capture thread starts up, so
                // keep asking. If it never stops, disposing would free the buffers under it.
                bool stopped = false;
                for (int attempt = 0; attempt < StopAttempts && !stopped; attempt++)
                {
                    _input.StopRecording();
                    stopped = _stopped.WaitOne(StopWaitMilliseconds);
                }
                if (!stopped)
                    return;
            }
            _input.Dispose();
            _stopped.Close();
        }
    }
}
