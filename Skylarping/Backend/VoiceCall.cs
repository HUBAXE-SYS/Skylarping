/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A live call's audio: microphone -> echo cancellation -> voice activity detection ->
// speech-to-text. Each thing the user says becomes one transcript, handed to the owner in the
// order it was said. Transcription runs on its own thread because a request takes far longer
// than the 20 ms the capture thread has for each frame. The character's side is CallSpeech.
//
// Only words cut the character off. Noise and leaked echo can both sound like a voice, so
// sound heard while they talk is transcribed as it comes in, and they stop once that gives
// words that aren't their own.
/*==========================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Preferences;

namespace Skylarping.Backend
{
    public sealed class VoiceCall : IDisposable
    {
        // Whisper's labels for what it heard when nobody spoke: [BLANK_AUDIO], (wind blowing), *music*
        private static readonly Regex OnlyLabels = new Regex(@"^(\s*(\[[^\]]*\]|\([^)]*\)|\*[^*]*\*))+\s*$");

        private const int EchoTailMilliseconds = 500; // has to cover the delay as well as the room
        private const int InterruptCheckMilliseconds = 300; // of speech-like sound before it's worth transcribing
        private const int InterruptRecheckMilliseconds = 120; // more of it before trying again, if that gave no words

        private sealed class Utterance
        {
            public byte[] Pcm;
            public bool Check;         // only asks whether the user is cutting the character off
            public bool OverCharacter; // heard while they talked, so it may be their own voice
        }

        private readonly Action<string> _onTranscript;
        private readonly Action<string> _onProblem;
        private readonly LlmEndpoint _endpoint = LlmEndpoint.SpeechToTextFromSettings();
        private readonly string _language = Settings.SttLanguage;
        private readonly BlockingCollection<Utterance> _utterances = new BlockingCollection<Utterance>();
        private readonly CancellationTokenSource _hangUp = new CancellationTokenSource();

        private readonly MicrophoneCapture _microphone;
        private readonly VoiceDetector _detector;
        private readonly SpeechSegmenter _segmenter;
        private readonly CallSpeech _speech;
        private readonly EchoCanceller _canceller;
        private readonly EchoQueue _played = new EchoQueue();
        private readonly Action<IDictionary<string, string>> _onInterrupted;
        private readonly bool _canInterrupt = Settings.TtsInterruption;
        private volatile bool _muted;
        private volatile bool _speakerRunning;
        private volatile bool _checking;      // an interruption check is being transcribed
        private volatile bool _confirmedUser; // a check found the user's words in this utterance
        // Capture thread only:
        private bool _wasPaused;
        private bool _voiced;        // the detector's answer for the latest frame
        private int _overSpeech;     // frames of speech heard while the character talks
        private bool _overCharacter; // this utterance has speech from while they talked

        /// <param name="voice">How the character sounds; null for a call where they don't speak.</param>
        /// <param name="onTranscript">Called with each thing the user says. Not on the UI thread.</param>
        /// <param name="onProblem">Called when transcription or speech fails. Not on the UI thread.</param>
        /// <param name="onInterrupted">Called with what was heard of each reply the user cut off (see
        /// <see cref="ICallSpeaker.Interrupt"/>). Not on the UI thread.</param>
        public VoiceCall(bool startMuted, CallVoice voice, Action<string> onTranscript, Action<string> onProblem,
            Action<IDictionary<string, string>> onInterrupted)
        {
            _muted = startMuted;
            _onTranscript = onTranscript;
            _onProblem = onProblem;
            _onInterrupted = onInterrupted;

            _detector = new VoiceDetector(MicrophoneCapture.SampleRate, (VoiceDetectionMode)Settings.SttSpeechDetection);
            _segmenter = new SpeechSegmenter(IsSpeech, MicrophoneCapture.FrameMilliseconds, Settings.SttEndSilenceMilliseconds);
            _microphone = new MicrophoneCapture();
            _microphone.Frame += OnFrame;
            _microphone.Failed += ex => _onProblem("The microphone stopped working: " + ex.Message);

            if (voice != null)
            {
                _canceller = new EchoCanceller(MicrophoneCapture.SampleRate, MicrophoneCapture.FrameMilliseconds, EchoTailMilliseconds);
                _speech = new CallSpeech(voice, _played.Push, onProblem);
                _speech.Player.Unplayed += _played.DropLast; // a cut: that sound was never in the room
            }
        }

        /// <summary>Takes the character's replies; null on a call where they don't speak.</summary>
        public ICallSpeaker Speaker
        {
            get { return _speakerRunning ? _speech : null; }
        }

        public bool Muted
        {
            set { _muted = value; }
        }

        public void Start()
        {
            new Thread(TranscribeUtterances) { IsBackground = true, Name = "Call transcription" }.Start();
            _microphone.Start(); // first: see EchoReference
            if (_speech == null)
                return;
            try
            {
                _speech.Start();
                _speakerRunning = true;
            }
            catch (Exception ex)
            {
                // A call they can't be heard on still hears you, as it would with no voice set up.
                _onProblem("The character can't speak on this call: " + ex.Message +
                           " Check your speakers in Tools > Options > Audio settings.");
            }
        }

        private bool IsSpeech(short[] frame)
        {
            _voiced = _detector.IsSpeech(frame);
            return _voiced;
        }

        private void OnFrame(short[] frame)
        {
            // Always, even while muted: the canceller has to keep following the room.
            frame = CancelEcho(frame);

            bool speaking = _speakerRunning && _speech.Busy;
            if (_muted || (speaking && !_canInterrupt))
            {
                if (!_wasPaused)
                {
                    _wasPaused = true;
                    _segmenter.Reset();
                    _detector.Reset();
                }
                _overSpeech = 0;
                _overCharacter = false;
                return;
            }
            _wasPaused = false;

            byte[] utterance = _segmenter.Push(frame);
            if (speaking)
            {
                _overCharacter |= _voiced;
                _overSpeech = _voiced ? _overSpeech + 1 : Math.Max(0, _overSpeech - 1);
                if (!_checking && _overSpeech * MicrophoneCapture.FrameMilliseconds >= InterruptCheckMilliseconds)
                {
                    byte[] soFar = _segmenter.Current();
                    if (soFar != null)
                    {
                        _overSpeech = (InterruptCheckMilliseconds - InterruptRecheckMilliseconds) / MicrophoneCapture.FrameMilliseconds;
                        _checking = true;
                        _utterances.Add(new Utterance { Pcm = soFar, Check = true, OverCharacter = true });
                    }
                }
            }
            else
            {
                _overSpeech = 0;
            }

            if (utterance != null)
            {
                _utterances.Add(new Utterance { Pcm = utterance, OverCharacter = _overCharacter && !_confirmedUser });
                _overCharacter = false;
                _confirmedUser = false;
            }
        }

        private short[] CancelEcho(short[] frame)
        {
            short[] played = _canceller == null ? null : _played.Next();
            if (played == null)
                return frame;
            var cleaned = new short[frame.Length]; // the segmenter keeps it
            _canceller.Cancel(frame, played, cleaned);
            return cleaned;
        }

        private void TranscribeUtterances()
        {
            bool reported = false; // one popup per outage, not one for everything said during it
            try
            {
                foreach (Utterance utterance in _utterances.GetConsumingEnumerable(_hangUp.Token))
                {
                    string text;
                    try
                    {
                        text = OpenAICompatClient.Transcribe(_endpoint, WavFile.FromPcm(utterance.Pcm, MicrophoneCapture.SampleRate), _language);
                        reported = false;
                    }
                    catch (LlmException ex)
                    {
                        if (!reported && !_hangUp.IsCancellationRequested)
                            _onProblem("Couldn't transcribe what you said. " + ConnectionTest.Explain(ex) +
                                " You can check the settings in Tools > AI Services.");
                        reported = true;
                        if (utterance.Check)
                            _checking = false;
                        continue;
                    }

                    text = CleanTranscript(text);
                    bool words = text.Length > 0 && !_hangUp.IsCancellationRequested
                        && !(utterance.OverCharacter && _speech.IsEcho(text));
                    if (utterance.Check)
                    {
                        if (words)
                        {
                            _confirmedUser = true;
                            if (_speech.Busy)
                                _onInterrupted(_speech.Interrupt()); // the whole utterance is answered when it ends
                        }
                        _checking = false;
                    }
                    else if (words)
                    {
                        _onTranscript(text);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Hung up.
            }
        }

        /// <summary>The words in a transcript; empty when it's only a label for a non-speech sound.</summary>
        public static string CleanTranscript(string text)
        {
            text = (text ?? "").Trim();
            return OnlyLabels.IsMatch(text) ? "" : text;
        }

        public void Dispose()
        {
            _hangUp.Cancel();
            _speakerRunning = false;
            _speech?.Dispose();
            _microphone.Dispose();
            _detector.Dispose(); // only once the microphone can no longer call them
            _canceller?.Dispose();
        }
    }
}
