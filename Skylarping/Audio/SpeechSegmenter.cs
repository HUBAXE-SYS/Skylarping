/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;

namespace Skylarping.Audio
{
    /// <summary>
    /// Turns a stream of audio frames into finished utterances. Speech starts after a short run of
    /// speech frames and ends after a pause; audio from just before the start is kept so the first
    /// sound of a word isn't cut off. Bursts with too little speech in them (coughs, door slams)
    /// are dropped. Very long speech is cut so one utterance can't grow without limit.
    /// </summary>
    public sealed class SpeechSegmenter
    {
        private const int StartMilliseconds = 200;
        private const int PreRollMilliseconds = 400;    // must cover StartMilliseconds
        private const int MinSpeechMilliseconds = 250;
        private const int MaxMilliseconds = 30000;

        private readonly Func<short[], bool> _isSpeech;
        private readonly int _startFrames, _preRollFrames, _minSpeechFrames, _endFrames, _maxFrames;

        private readonly Queue<short[]> _preRoll = new Queue<short[]>();
        private readonly List<short[]> _utterance = new List<short[]>();
        private bool _inSpeech;
        private int _startRun;      // speech frames towards a start; a lone silent frame only takes one back
        private int _speechFrames;  // speech frames in this utterance
        private int _silentFrames;  // silent frames since the last speech frame

        public SpeechSegmenter(Func<short[], bool> isSpeech, int frameMilliseconds, int endSilenceMilliseconds)
        {
            _isSpeech = isSpeech;
            _startFrames = StartMilliseconds / frameMilliseconds;
            _preRollFrames = PreRollMilliseconds / frameMilliseconds;
            _minSpeechFrames = MinSpeechMilliseconds / frameMilliseconds;
            _endFrames = endSilenceMilliseconds / frameMilliseconds;
            _maxFrames = MaxMilliseconds / frameMilliseconds;
        }

        /// <summary>Takes one frame. Returns an utterance as PCM bytes when one ends, otherwise null.</summary>
        public byte[] Push(short[] frame)
        {
            bool speech = _isSpeech(frame);

            if (!_inSpeech)
            {
                _preRoll.Enqueue(frame);
                if (_preRoll.Count > _preRollFrames)
                    _preRoll.Dequeue();

                _startRun = speech ? _startRun + 1 : Math.Max(0, _startRun - 1);
                if (_startRun >= _startFrames)
                {
                    _inSpeech = true;
                    _speechFrames = _startRun;
                    _silentFrames = 0;
                    _utterance.AddRange(_preRoll);
                    _preRoll.Clear();
                }
                return null;
            }

            _utterance.Add(frame);
            if (speech)
            {
                _speechFrames++;
                _silentFrames = 0;
            }
            else
            {
                _silentFrames++;
            }

            if (_silentFrames < _endFrames && _utterance.Count < _maxFrames)
                return null;

            byte[] pcm = _speechFrames >= _minSpeechFrames ? WavFile.ToPcm(_utterance) : null;
            Reset();
            return pcm;
        }

        /// <summary>The utterance so far as PCM bytes, or null if nobody is speaking.</summary>
        public byte[] Current()
        {
            return _inSpeech ? WavFile.ToPcm(_utterance) : null;
        }

        /// <summary>Forgets anything in progress.</summary>
        public void Reset()
        {
            _preRoll.Clear();
            _utterance.Clear();
            _inSpeech = false;
            _startRun = 0;
            _speechFrames = 0;
            _silentFrames = 0;
        }
    }
}
