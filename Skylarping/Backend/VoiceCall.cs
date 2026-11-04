/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A live call's audio: microphone -> voice activity detection -> speech-to-text. Each thing
// the user says becomes one transcript, handed to the owner in the order it was said.
// Transcription runs on its own thread because a request takes far longer than the 20 ms
// the capture thread has for each frame.
/*==========================================================*/

using System;
using System.Collections.Concurrent;
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

        private readonly Action<string> _onTranscript;
        private readonly Action<string> _onProblem;
        private readonly LlmEndpoint _endpoint = LlmEndpoint.SpeechToTextFromSettings();
        private readonly string _language = Settings.SttLanguage;
        private readonly BlockingCollection<byte[]> _utterances = new BlockingCollection<byte[]>();
        private readonly CancellationTokenSource _hangUp = new CancellationTokenSource();

        private readonly MicrophoneCapture _microphone;
        private readonly VoiceDetector _detector;
        private readonly SpeechSegmenter _segmenter;
        private volatile bool _muted;
        private bool _wasMuted; // capture thread only

        /// <param name="onTranscript">Called with each thing the user says. Not on the UI thread.</param>
        /// <param name="onProblem">Called when transcription fails. Not on the UI thread.</param>
        public VoiceCall(bool startMuted, Action<string> onTranscript, Action<string> onProblem)
        {
            _muted = startMuted;
            _onTranscript = onTranscript;
            _onProblem = onProblem;

            _detector = new VoiceDetector(MicrophoneCapture.SampleRate, (VoiceDetectionMode)Settings.SttSpeechDetection);
            _segmenter = new SpeechSegmenter(_detector.IsSpeech, MicrophoneCapture.FrameMilliseconds, Settings.SttEndSilenceMilliseconds);
            _microphone = new MicrophoneCapture();
            _microphone.Frame += OnFrame;
            _microphone.Failed += ex => _onProblem("The microphone stopped working: " + ex.Message);
        }

        public bool Muted
        {
            set { _muted = value; }
        }

        public void Start()
        {
            new Thread(TranscribeUtterances) { IsBackground = true, Name = "Call transcription" }.Start();
            _microphone.Start();
        }

        private void OnFrame(short[] frame)
        {
            if (_muted)
            {
                if (!_wasMuted)
                {
                    _wasMuted = true;
                    _segmenter.Reset();
                    _detector.Reset();
                }
                return;
            }
            _wasMuted = false;

            byte[] utterance = _segmenter.Push(frame);
            if (utterance != null)
                _utterances.Add(utterance);
        }

        private void TranscribeUtterances()
        {
            bool reported = false; // one popup per outage, not one for everything said during it
            try
            {
                foreach (byte[] pcm in _utterances.GetConsumingEnumerable(_hangUp.Token))
                {
                    string text;
                    try
                    {
                        text = OpenAICompatClient.Transcribe(_endpoint, WavFile.FromPcm(pcm, MicrophoneCapture.SampleRate), _language);
                        reported = false;
                    }
                    catch (LlmException ex)
                    {
                        if (!reported && !_hangUp.IsCancellationRequested)
                            _onProblem("Couldn't transcribe what you said. " + ConnectionTest.Explain(ex) +
                                " You can check the settings in Tools > AI Services.");
                        reported = true;
                        continue;
                    }

                    text = CleanTranscript(text);
                    if (text.Length > 0 && !_hangUp.IsCancellationRequested)
                        _onTranscript(text);
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
            _microphone.Dispose();
            _detector.Dispose(); // only once the microphone can no longer call it
        }
    }
}
