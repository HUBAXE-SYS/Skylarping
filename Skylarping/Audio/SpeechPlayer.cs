/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A call's speaker. It runs for the whole call, playing silence between clips: separate
// clips would each reopen the device, leaving gaps, and the echo canceller needs a steady
// stream to line up with the microphone (see EchoReference).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using Skylarping.Preferences;

namespace Skylarping.Audio
{
    public sealed class SpeechPlayer : IWaveProvider, IDisposable
    {
        // How far ahead the device is fed (Options > Audio settings). Every reply and every cut
        // starts that much later, but too little and the sound breaks up: XP's waveOut needs the
        // room, as the microphone does; from Vista on it runs on the audio engine's steady clock.
        public const int MinBufferMilliseconds = 60;
        public const int MaxBufferMilliseconds = 1000;
        public static readonly int AutomaticBufferMilliseconds = Environment.OSVersion.Version.Major < 6 ? 300 : 150;
        private const int BufferCount = 3;
        private const int RoomMilliseconds = 300; // sound handed over is still in the device's buffers, then in the room
        private const int BytesPerSample = 2;
        private const int SamplesPerEchoFrame = SpeechAudio.SampleRate * MicrophoneCapture.FrameMilliseconds / 1000;

        private sealed class Clip
        {
            public short[] Samples;
            public object Tag;
        }

        private readonly object _lock = new object();
        private readonly Queue<Clip> _clips = new Queue<Clip>();
        private readonly EchoFramer _echo;
        private readonly int _latency; // ms the device holds
        private readonly int _fade;    // ms after the last sound until it's out of the room
        private Clip _current;
        private int _position;
        private int _lastSound;
        private short[] _samples = new short[0];
        private WaveOutEvent _output;
        private long _handedOver; // bytes the device has been given since its position last started from zero

        /// <param name="onEchoFrame">Called with what's being played, as microphone-format frames.</param>
        public SpeechPlayer(Action<short[]> onEchoFrame)
        {
            WaveFormat = new WaveFormat(SpeechAudio.SampleRate, 16, 1);
            _echo = new EchoFramer(MicrophoneCapture.SampleRate * MicrophoneCapture.FrameMilliseconds / 1000, onEchoFrame);
            _latency = BufferMilliseconds(Settings.SpeakerBufferMilliseconds);
            _fade = _latency + RoomMilliseconds;
            _lastSound = Environment.TickCount - _fade;
        }

        /// <summary>The buffer a setting asks for: automatic for 0, otherwise kept within the limits.</summary>
        public static int BufferMilliseconds(int setting)
        {
            return setting <= 0 ? AutomaticBufferMilliseconds : Math.Max(MinBufferMilliseconds, Math.Min(MaxBufferMilliseconds, setting));
        }

        public WaveFormat WaveFormat { get; private set; }

        /// <summary>A clip began playing. Raised on the playback thread: don't block it.</summary>
        public event Action<object> ClipStarted;

        /// <summary>
        ///  A cut threw away sound the device was still holding: this many echo frames of it had
        ///  already gone to <c>onEchoFrame</c> and will never be heard. Raised under the player's
        ///  lock, so keep it quick.
        /// </summary>
        public event Action<int> Unplayed;

        /// <summary>Whether sound is playing, queued, or still dying away.</summary>
        public bool Busy
        {
            get
            {
                lock (_lock)
                    return _current != null || _clips.Count > 0 || Environment.TickCount - _lastSound < _fade;
            }
        }

        public void Start()
        {
            // As with the microphone: no context, so stopping can't wait on the UI thread.
            SynchronizationContext context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                _output = AudioDevices.CreateOutput();
                _output.DesiredLatency = _latency;
                _output.NumberOfBuffers = BufferCount;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }
            _output.Init(this);
            _output.Play();
        }

        public void Enqueue(short[] samples, object tag)
        {
            lock (_lock)
                _clips.Enqueue(new Clip { Samples = samples, Tag = tag });
        }

        /// <summary>Stops the sound at once: what's queued here and what the device was still holding.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                bool sounding = _current != null || _clips.Count > 0 || Environment.TickCount - _lastSound < _latency;
                _clips.Clear();
                _current = null;
                if (sounding && _output != null)
                    FlushDevice();
            }
        }

        // The device holds up to _latency ms of sound, which would play on after a cut.
        // Flushing it leaves a hole in the echo reference, which already has that sound: the
        // frames that won't be heard come out again, so the count pairing (EchoReference) holds.
        private void FlushDevice()
        {
            long played;
            try
            {
                played = _output.GetPosition();
                _output.Flush();
            }
            catch (Exception)
            {
                return; // a device that can't say where it is, or won't reset: the sound plays out
            }
            long unplayed = Math.Min(_handedOver - played, (long)_latency * SpeechAudio.SampleRate / 1000 * BytesPerSample);
            _handedOver = 0;
            int frames = (int)((unplayed / BytesPerSample + SamplesPerEchoFrame / 2) / SamplesPerEchoFrame);
            if (frames > 0)
                Unplayed?.Invoke(frames);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int wanted = count / BytesPerSample;
            if (_samples.Length < wanted)
                _samples = new short[wanted];
            else
                Array.Clear(_samples, 0, wanted);

            var started = new List<object>();
            lock (_lock)
            {
                _handedOver += wanted * BytesPerSample;
                int filled = 0;
                while (filled < wanted)
                {
                    if (_current == null)
                    {
                        if (_clips.Count == 0)
                            break;
                        _current = _clips.Dequeue();
                        _position = 0;
                        started.Add(_current.Tag);
                    }
                    int take = Math.Min(wanted - filled, _current.Samples.Length - _position);
                    Array.Copy(_current.Samples, _position, _samples, filled, take);
                    filled += take;
                    _position += take;
                    if (_position >= _current.Samples.Length)
                        _current = null;
                }
                if (filled > 0)
                    _lastSound = Environment.TickCount;

                Buffer.BlockCopy(_samples, 0, buffer, offset, wanted * BytesPerSample);
                _echo.Push(_samples, wanted); // under the lock: a cut mustn't fall between a buffer and its echo frames
            }

            Action<object> onStart = ClipStarted;
            if (onStart != null)
                foreach (object tag in started)
                    onStart(tag);
            return wanted * BytesPerSample;
        }

        public void Dispose()
        {
            Clear();
            if (_output != null)
            {
                _output.Dispose();
                _output = null;
            }
        }
    }
}
