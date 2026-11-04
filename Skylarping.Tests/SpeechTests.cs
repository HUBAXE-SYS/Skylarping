/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for the call audio path that doesn't need a microphone: framing, turn detection,
// and the transcription request (against the fake server from AIClientTests).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Backend;

namespace Skylarping.Tests
{
    [TestFixture]
    public class SpeechTests
    {
        private const int FrameMs = 20;
        private const int EndSilenceMs = 600;

        private static readonly short[] Loud = { 1000 };
        private static readonly short[] Quiet = { 0 };

        private static SpeechSegmenter Segmenter(int endSilenceMs = EndSilenceMs)
        {
            return new SpeechSegmenter(frame => frame[0] != 0, FrameMs, endSilenceMs);
        }

        /// <summary>Feeds frames and returns the sample count of each finished utterance.</summary>
        private static List<int> Feed(SpeechSegmenter segmenter, params KeyValuePair<short[], int>[] runs)
        {
            var utterances = new List<int>();
            foreach (KeyValuePair<short[], int> run in runs)
                for (int i = 0; i < run.Value / FrameMs; i++)
                {
                    byte[] pcm = segmenter.Push(run.Key);
                    if (pcm != null)
                        utterances.Add(pcm.Length / 2);
                }
            return utterances;
        }

        private static KeyValuePair<short[], int> Speech(int ms) { return new KeyValuePair<short[], int>(Loud, ms); }
        private static KeyValuePair<short[], int> Silence(int ms) { return new KeyValuePair<short[], int>(Quiet, ms); }

        [Test]
        public void Segmenter_EndsUtteranceAfterThePause()
        {
            List<int> utterances = Feed(Segmenter(), Silence(1000), Speech(1000), Silence(EndSilenceMs));

            Assert.That(utterances.Count, Is.EqualTo(1));
        }

        [Test]
        public void Segmenter_KeepsAudioFromBeforeTheStart()
        {
            // Everything spoken, the pre-roll silence before it, and the closing pause.
            List<int> utterances = Feed(Segmenter(), Silence(1000), Speech(1000), Silence(EndSilenceMs));

            int frames = utterances[0];
            Assert.That(frames, Is.GreaterThan(1000 / FrameMs + EndSilenceMs / FrameMs));
        }

        [Test]
        public void Segmenter_ShortGapStaysInOneUtterance()
        {
            List<int> utterances = Feed(Segmenter(),
                Speech(600), Silence(EndSilenceMs - FrameMs), Speech(600), Silence(EndSilenceMs));

            Assert.That(utterances.Count, Is.EqualTo(1));
        }

        [Test]
        public void Segmenter_DropsABlip()
        {
            List<int> utterances = Feed(Segmenter(), Speech(100), Silence(2000));
            Assert.That(utterances, Is.Empty);

            utterances = Feed(Segmenter(), Speech(220), Silence(2000));
            Assert.That(utterances, Is.Empty, "reaches the start of speech, but isn't enough speech to keep");
        }

        [Test]
        public void Segmenter_CutsVeryLongSpeech()
        {
            List<int> utterances = Feed(Segmenter(), Speech(61000));

            Assert.That(utterances.Count, Is.EqualTo(2));
        }

        [Test]
        public void Segmenter_ResetForgetsSpeechInProgress()
        {
            SpeechSegmenter segmenter = Segmenter();
            Feed(segmenter, Speech(1000));

            segmenter.Reset();

            Assert.That(Feed(segmenter, Silence(EndSilenceMs)), Is.Empty);
        }

        [Test]
        public void Slicer_ReassemblesFramesAcrossBuffersAndSplitSamples()
        {
            var frames = new List<short[]>();
            var slicer = new FrameSlicer(2);
            byte[] bytes = { 1, 0, 2, 0, 3, 0, 0xFF, 0xFF };

            slicer.Push(bytes.Take(3).ToArray(), 3, frames.Add); // ends mid-sample
            slicer.Push(bytes.Skip(3).ToArray(), 5, frames.Add);

            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames[0], Is.EqualTo(new short[] { 1, 2 }));
            Assert.That(frames[1], Is.EqualTo(new short[] { 3, -1 }));
        }

        [Test]
        public void Wav_HasACorrectHeader()
        {
            byte[] wav = WavFile.FromPcm(WavFile.ToPcm(new[] { new short[] { 1, 2 }, new short[] { 3 } }), 16000);

            Assert.That(wav.Length, Is.EqualTo(44 + 6));
            Assert.That(Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(wav.Length - 8));
            Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(1), "PCM");
            Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1), "mono");
            Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
            Assert.That(BitConverter.ToInt32(wav, 28), Is.EqualTo(32000), "bytes per second");
            Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(6), "data size");
            Assert.That(BitConverter.ToInt16(wav, 48), Is.EqualTo(3), "last sample");
        }

        /// <summary>A UI thread that's busy: nothing posted to it runs.</summary>
        private sealed class BusyUiContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state) { }
        }

        [TestCase(0, Description = "stopped while the capture thread is still starting")]
        [TestCase(300, Description = "stopped while capturing")]
        public void Microphone_StopsWithoutNeedingTheUiThread(int recordMilliseconds)
        {
            Assume.That(MicrophoneCapture.Available, "needs a microphone");

            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new BusyUiContext());
            try
            {
                var microphone = new MicrophoneCapture();
                microphone.Start();
                Thread.Sleep(recordMilliseconds);

                Stopwatch timer = Stopwatch.StartNew();
                microphone.Dispose();

                Assert.That(timer.ElapsedMilliseconds, Is.LessThan(1000));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void Level_HandlesTheLoudestSamples()
        {
            Assert.That(MicrophoneCapture.Level(new short[] { 0, 0 }), Is.EqualTo(0));
            Assert.That(MicrophoneCapture.Level(new short[] { short.MinValue }), Is.EqualTo(1));
        }

        [Test]
        public void Device_MissingOrUnchosen_MeansTheWindowsDefault()
        {
            string[] devices = { "Headset", "Webcam" };

            Assert.That(AudioDevices.Find(devices, "Webcam"), Is.EqualTo(1));
            Assert.That(AudioDevices.Find(devices, "Unplugged"), Is.EqualTo(-1));
            Assert.That(AudioDevices.Find(devices, ""), Is.EqualTo(-1));
        }

        [TestCase("[BLANK_AUDIO]", "")]
        [TestCase(" [ Silence ] ", "")]
        [TestCase("(wind blowing)", "")]
        [TestCase("*music*", "")]
        [TestCase("[Music] (applause)", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase("  Hello there. ", "Hello there.")]
        [TestCase("Hello [laughs] there", "Hello [laughs] there")]
        public void CleanTranscript_DropsOnlyNonSpeechLabels(string text, string expected)
        {
            Assert.That(VoiceCall.CleanTranscript(text), Is.EqualTo(expected));
        }

        [Test]
        public void Transcribe_PostsMultipartAudioAndReturnsText()
        {
            using (var server = new FakeOpenAiServer())
            {
                string seenAuth = null;
                server.Handler = (method, path, body, auth) =>
                {
                    seenAuth = auth;
                    return FakeOpenAiServer.Reply(200, "{\"text\":\"  hello world \"}");
                };

                string text = OpenAICompatClient.Transcribe(new LlmEndpoint(server.BaseUrl, "key", "whisper-1"),
                    WavFile.FromPcm(new byte[32], 16000), " en ");

                Assert.That(text, Is.EqualTo("hello world"));
                Assert.That(seenAuth, Is.EqualTo("Bearer key"));
                string request = server.Requests.Single();
                Assert.That(request, Does.StartWith("POST /v1/audio/transcriptions "));
                Assert.That(request, Does.Contain("name=\"file\"; filename=\"speech.wav\""));
                Assert.That(request, Does.Contain("Content-Type: audio/wav"));
                Assert.That(request, Does.Contain("RIFF"));
                Assert.That(request, Does.Contain("name=\"model\"\r\n\r\nwhisper-1\r\n"));
                Assert.That(request, Does.Contain("name=\"language\"\r\n\r\nen\r\n"));
                Assert.That(request, Does.Contain("name=\"response_format\"\r\n\r\njson\r\n"));
            }
        }

        [Test]
        public void Transcribe_LeavesOutBlankModelAndLanguage()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (method, path, body, auth) => FakeOpenAiServer.Reply(200, "{\"text\":\"hi\"}");

                OpenAICompatClient.Transcribe(new LlmEndpoint(server.BaseUrl, "", ""), new byte[8], "");

                string request = server.Requests.Single();
                Assert.That(request, Does.Not.Contain("name=\"model\""));
                Assert.That(request, Does.Not.Contain("name=\"language\""));
            }
        }

        [Test]
        public void Transcribe_ReportsServerErrors()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (method, path, body, auth) =>
                    FakeOpenAiServer.Reply(404, "{\"error\":{\"message\":\"no such route\"}}");

                var error = Assert.Throws<LlmException>(() =>
                    OpenAICompatClient.Transcribe(new LlmEndpoint(server.BaseUrl, "", "m"), new byte[8]));

                Assert.That(error.Kind, Is.EqualTo(LlmErrorKind.NotFound));
                Assert.That(error.Message, Does.Contain("no such route"));
            }
        }

        [Test]
        public void SpeechEndpoint_OwnServerNeverGetsTheLanguageModelKey()
        {
            LlmEndpoint endpoint = LlmEndpoint.ForSpeechToText(" http://stt:9000/v1/ ", "", "whisper-1",
                new LlmEndpoint("http://llm:1234/v1", "llm-key", "chat-model"));

            Assert.That(endpoint.BaseUrl, Is.EqualTo("http://stt:9000/v1"));
            Assert.That(endpoint.ApiKey, Is.Empty);
            Assert.That(endpoint.Model, Is.EqualTo("whisper-1"));
        }

        [Test]
        public void SpeechEndpoint_BlankUrlUsesTheLanguageModelServerAndKey()
        {
            LlmEndpoint endpoint = LlmEndpoint.ForSpeechToText(" ", "ignored", "whisper-1",
                new LlmEndpoint("http://llm:1234/v1", "llm-key", "chat-model"));

            Assert.That(endpoint.BaseUrl, Is.EqualTo("http://llm:1234/v1"));
            Assert.That(endpoint.ApiKey, Is.EqualTo("llm-key"));
            Assert.That(endpoint.Model, Is.EqualTo("whisper-1"));
        }
    }
}
