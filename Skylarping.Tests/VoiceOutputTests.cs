/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for the character's side of a call: turning replies into pieces, the speech request,
// playback, what counts as heard, and echo cancellation.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Backend;
using Skylarping.Characters;
using Skylarping.Models;

namespace Skylarping.Tests
{
    [TestFixture]
    public class VoiceOutputTests
    {
        private const string Long1 = "This first sentence is comfortably long enough to go out alone, without being merged into another.";
        private const string Long2 = "And this second sentence is also long enough to go out alone, without being merged into another.";

        private static List<string> Pieces(string text, bool final = true)
        {
            return new SpeechChunker().Update(text, final).Select(c => c.Text).ToList();
        }

        [Test]
        public void Chunker_MergesShortSentences()
        {
            Assert.That(Pieces("Oh. Really? Tell me more about the thing you found yesterday, because I would love to hear it all. Okay."),
                Is.EqualTo(new[] { "Oh. Really? Tell me more about the thing you found yesterday, because I would love to hear it all.", "Okay." }));
        }

        [Test]
        public void Chunker_WaitsToSeeWhatFollowsASentenceEnd()
        {
            var chunker = new SpeechChunker();

            Assert.That(chunker.Update(Long1, false), Is.Empty, "could still be '...' or a closing quote");

            IList<SpeechChunk> chunks = chunker.Update(Long1 + " And", false);
            Assert.That(chunks.Single().Text, Is.EqualTo(Long1));
            Assert.That(chunks.Single().End, Is.EqualTo(Long1.Length));
        }

        [Test]
        public void Chunker_Eager_SendsTheFirstSentenceWithoutWaiting()
        {
            IList<SpeechChunk> chunks = new SpeechChunker().Update("Hey, how's it going? I was", false, true);

            Assert.That(chunks.Single().Text, Is.EqualTo("Hey, how's it going?"));
        }

        [Test]
        public void Chunker_Eager_OnlyTheFirstPieceIsShort()
        {
            IList<SpeechChunk> chunks = new SpeechChunker().Update("Hey, how's it going? Good to hear you. Tell me more. ", false, true);

            Assert.That(chunks.Single().Text, Is.EqualTo("Hey, how's it going?"));
        }

        [Test]
        public void Chunker_Eager_MergesAFewWords()
        {
            IList<SpeechChunk> chunks = new SpeechChunker().Update("Oh. Really? That sounds great. And", false, true);

            Assert.That(chunks.Single().Text, Is.EqualTo("Oh. Really? That sounds great."));
        }

        [Test]
        public void Chunker_NeverRepeatsAPiece()
        {
            var chunker = new SpeechChunker();
            string text = Long1 + " " + Long2 + " ";

            int first = chunker.Update(text, false).Count;

            Assert.That(first, Is.EqualTo(2));
            Assert.That(chunker.Update(text, false), Is.Empty);
            Assert.That(chunker.Update(text, true), Is.Empty, "only whitespace is left");
        }

        [TestCase("Long enough already to reach the point where a sentence could end, said Dr. Watson to the nurse today. Yes.")]
        [TestCase("Long enough already to reach the point where a sentence could end, and it was 3.5 percent up today. Yes.")]
        [TestCase("Long enough already to reach the point where a sentence could end, wrote J. Smith on the note today. Yes.")]
        public void Chunker_DoesNotEndOnADotInsideASentence(string text)
        {
            Assert.That(Pieces(text).First(), Does.EndWith("today."));
        }

        [Test]
        public void Chunker_KeepsAClosingQuoteWithItsSentence()
        {
            Assert.That(Pieces("She looked at me for a very long while across the table in that crowded room and said \"no way.\" Then she left.").First(),
                Does.EndWith("no way.\""));
        }

        [Test]
        public void Chunker_CutsASentenceThatWontEnd()
        {
            string text = string.Join(", ", Enumerable.Repeat("and then another thing happened", 20));

            List<string> pieces = Pieces(text);

            Assert.That(pieces.Count, Is.GreaterThan(1));
            Assert.That(pieces.All(p => p.Length <= 240));
        }

        [TestCase("**Hello** there", "Hello there")]
        [TestCase("Go to https://example.com/a?b=c now", "Go to now")]
        [TestCase("Nice \U0001F600 one", "Nice one")]
        [TestCase("# Title", "Title")]
        [TestCase("snake_case stays", "snake_case stays")]
        [TestCase("_emphasis_", "emphasis")]
        public void Clean_LeavesOnlyWhatCanBeSpoken(string text, string expected)
        {
            Assert.That(SpeechChunker.Clean(text), Is.EqualTo(expected));
        }

        [Test]
        public void Chunker_SkipsPiecesWithNothingToSay()
        {
            Assert.That(Pieces("\U0001F600 \U0001F600"), Is.Empty);
        }

        private static byte[] Wav(int samples, int rate = SpeechAudio.SampleRate)
        {
            return WavFile.FromPcm(new byte[samples * 2], rate);
        }

        [Test]
        public void Synthesize_SendsOnlyWhatsSet()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (method, path, body, auth) => FakeOpenAiServer.Reply(200, "RIFF....WAVEfmt ");

                OpenAICompatClient.Synthesize(new LlmEndpoint(server.BaseUrl, "", "OpenMOSS-TTS"), "Hi");

                string request = server.Requests.Single();
                Assert.That(request, Does.StartWith("POST /v1/audio/speech "));
                Assert.That(request, Does.Contain("\"input\":\"Hi\""));
                Assert.That(request, Does.Contain("\"model\":\"OpenMOSS-TTS\""));
                Assert.That(request, Does.Contain("\"response_format\":\"wav\""));
                Assert.That(request, Does.Not.Contain("\"voice\""));
                Assert.That(request, Does.Not.Contain("\"speed\""));
                Assert.That(request, Does.Not.Contain("reference_wav_b64"));
                Assert.That(request, Does.Not.Contain("voice_design_description"));
            }
        }

        [Test]
        public void Synthesize_SendsSpeedAndClip()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (method, path, body, auth) => FakeOpenAiServer.Reply(200, "RIFF....WAVEfmt ");

                OpenAICompatClient.Synthesize(new LlmEndpoint(server.BaseUrl, "", "m"), "Hi", 150, "QUJD", "a low voice");

                string request = server.Requests.Single();
                Assert.That(request, Does.Contain("\"speed\":1.5"));
                Assert.That(request, Does.Contain("\"reference_wav_b64\":\"QUJD\""));
                Assert.That(request, Does.Contain("\"voice_design_description\":\"a low voice\""));
            }
        }

        [Test]
        public void Synthesize_RefusesWhatIsntAudio()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (method, path, body, auth) => FakeOpenAiServer.Reply(200, "{\"error\":\"nope\"}");

                var error = Assert.Throws<LlmException>(() =>
                    OpenAICompatClient.Synthesize(new LlmEndpoint(server.BaseUrl, "", "m"), "Hi"));

                Assert.That(error.Kind, Is.EqualTo(LlmErrorKind.BadResponse));
            }
        }

        [Test]
        public void Decode_ConvertsOtherFormatsToSpeechFormat()
        {
            using (var memory = new MemoryStream())
            {
                using (var writer = new NAudio.Wave.WaveFileWriter(new NonClosing(memory), new NAudio.Wave.WaveFormat(48000, 16, 2)))
                    writer.Write(new byte[48000 * 4], 0, 48000 * 4); // one second

                short[] samples = SpeechAudio.Decode(memory.ToArray());

                Assert.That(samples.Length, Is.EqualTo(SpeechAudio.SampleRate).Within(100));
            }
        }

        private sealed class NonClosing : MemoryStream
        {
            private readonly Stream _inner;
            public NonClosing(Stream inner) { _inner = inner; }
            public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); }
            public override long Position { get { return _inner.Position; } set { _inner.Position = value; } }
            public override long Seek(long offset, SeekOrigin loc) { return _inner.Seek(offset, loc); }
            public override long Length { get { return _inner.Length; } }
        }

        [Test]
        public void Sample_LongerThanTheLimitIsCut()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(path, Wav(SpeechAudio.SampleRate * 40));
            try
            {
                bool trimmed;
                byte[] sample = SpeechAudio.LoadSample(path, out trimmed);

                Assert.That(trimmed, Is.True);
                Assert.That(SpeechAudio.Decode(sample).Length, Is.EqualTo(SpeechAudio.SampleRate * 30));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Sample_TooShortOrNotAudioIsRefused()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                bool trimmed;
                File.WriteAllBytes(path, Wav(100));
                Assert.Throws<VoiceSampleException>(() => SpeechAudio.LoadSample(path, out trimmed));

                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
                Assert.Throws<VoiceSampleException>(() => SpeechAudio.LoadSample(path, out trimmed));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void SampleLength_ComesFromTheSizeAlone()
        {
            Assert.That(SpeechAudio.SampleSeconds(Wav(SpeechAudio.SampleRate * 2)), Is.EqualTo(2));
            Assert.That(SpeechAudio.SampleSeconds(new byte[] { 1, 2, 3 }), Is.EqualTo(0));
        }

        [Test]
        public void Store_ReplacesAndRemovesTheSample()
        {
            string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Assert.That(CharacterStore.ReadVoiceSample(folder), Is.Null);

                CharacterStore.WriteVoiceSample(folder, new byte[] { 1 });
                CharacterStore.WriteVoiceSample(folder, new byte[] { 2, 3 });
                Assert.That(CharacterStore.ReadVoiceSample(folder), Is.EqualTo(new byte[] { 2, 3 }));

                CharacterStore.WriteVoiceSample(folder, null);
                Assert.That(CharacterStore.ReadVoiceSample(folder), Is.Null);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void Framer_MakesFramesInTheMicrophoneFormat()
        {
            var frames = new List<short[]>();
            var framer = new EchoFramer(320, frames.Add);
            short[] speech = Enumerable.Repeat((short)1000, 960).ToArray(); // 40 ms at 24 kHz

            framer.Push(speech, speech.Length);

            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames.All(f => f.Length == 320));
            Assert.That(frames[1][100], Is.EqualTo(1000));
        }

        [Test]
        public void EchoQueue_DropsTheOldestWhenClocksDrift()
        {
            var queue = new EchoQueue();
            for (short i = 0; i < 105; i++)
                queue.Push(new[] { i });

            Assert.That(queue.Next()[0], Is.EqualTo(5));
        }

        [Test]
        public void Chunker_LeavesOutWhatWouldBeReadByName()
        {
            Assert.That(SpeechChunker.Clean("I love it ❤️ so much 😊!"), Is.EqualTo("I love it so much!"));
            Assert.That(SpeechChunker.Clean("Okay 👍🏻 let's go 👨‍👩."), Is.EqualTo("Okay let's go."));
            Assert.That(SpeechChunker.Clean("Sparkles ✨ everywhere, 20°C."), Is.EqualTo("Sparkles everywhere, 20°C."));
            Assert.That(SpeechChunker.Clean("😊"), Is.Empty);
            Assert.That(SpeechChunker.Clean("..."), Is.Empty, "nothing to say");
            Assert.That(new SpeechChunker().Update("😊 ✨", true), Is.Empty);
        }

        [Test]
        public void Chunker_NeverCutsThroughAnEmoji()
        {
            string text = new string('a', 239) + "😊" + new string('b', 20);
            IList<SpeechChunk> chunks = new SpeechChunker().Update(text, true);

            Assert.That(chunks.Count, Is.EqualTo(2));
            Assert.That(chunks[0].End, Is.EqualTo(241), "the pair stays whole");
            Assert.That(chunks[0].Text, Is.EqualTo(new string('a', 239)));
        }

        private static byte[] ReadPlayer(SpeechPlayer player, int samples)
        {
            var buffer = new byte[samples * 2];
            Assert.That(player.Read(buffer, 0, buffer.Length), Is.EqualTo(buffer.Length), "must never run dry");
            return buffer;
        }

        [Test]
        public void Player_PlaysClipsInOrderThenSilence()
        {
            var started = new List<object>();
            var player = new SpeechPlayer(frame => { });
            player.ClipStarted += started.Add;
            player.Enqueue(new short[] { 1, 1 }, "a");
            player.Enqueue(new short[] { 2 }, "b");

            byte[] output = ReadPlayer(player, 5);

            Assert.That(output, Is.EqualTo(new byte[] { 1, 0, 1, 0, 2, 0, 0, 0, 0, 0 }));
            Assert.That(started, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Player_ClearStopsWhatsPlaying()
        {
            var player = new SpeechPlayer(frame => { });
            player.Enqueue(new short[] { 1, 1, 1, 1 }, "a");
            ReadPlayer(player, 2);

            player.Clear();

            Assert.That(ReadPlayer(player, 2).All(b => b == 0));
        }

        [Test]
        public void EchoQueue_DropLastForgetsTheNewestFrames()
        {
            var queue = new EchoQueue();
            for (short i = 0; i < 5; i++)
                queue.Push(new[] { i });

            queue.DropLast(2);

            Assert.That(queue.Next()[0], Is.EqualTo(0));
            Assert.That(queue.Next()[0], Is.EqualTo(1));
            Assert.That(queue.Next()[0], Is.EqualTo(2));
            Assert.That(queue.Next(), Is.Null);

            queue.Push(new short[] { 9 });
            queue.DropLast(5); // more than it has
            Assert.That(queue.Next(), Is.Null);
        }

        [Test]
        public void Player_ClearWhenSilent_DoesNotDelayTheNextClip()
        {
            var player = new SpeechPlayer(frame => { });

            player.Clear();
            player.Enqueue(new short[] { 2 }, "a");

            Assert.That(ReadPlayer(player, 1), Is.EqualTo(new byte[] { 2, 0 }));
        }

        private static CallSpeech Speech(Func<string, byte[]> synthesize = null)
        {
            var speech = new CallSpeech(null, synthesize ?? (text => Wav(240)), frame => { }, problem => { });
            speech.StartGenerating();
            return speech;
        }

        private static void WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 500 && !condition(); i++)
                Thread.Sleep(10);
            Assert.That(condition(), "timed out");
        }

        [Test]
        public void Speech_StartsOnTheFirstSentence_ThenMergesWhileGenerating()
        {
            var spoken = new List<string>();
            var release = new ManualResetEvent(false);
            using (CallSpeech speech = Speech(text =>
            {
                lock (spoken)
                    spoken.Add(text);
                release.WaitOne(5000);
                return Wav(240);
            }))
            {
                speech.Speak("m1", "Hey, how's it going? It", false);
                WaitFor(() => { lock (spoken) return spoken.Count == 1; });
                speech.Speak("m1", "Hey, how's it going? It is good to hear you. I", false);
                release.Set();
                speech.Speak("m1", "Hey, how's it going? It is good to hear you. I missed you.", true);
                WaitFor(() => { lock (spoken) return spoken.Count == 2; });

                lock (spoken)
                    Assert.That(spoken, Is.EqualTo(new[] { "Hey, how's it going?", "It is good to hear you. I missed you." }));
            }
        }

        [TestCase("It is good to hear you", true)]
        [TestCase("it's GOOD to hear you, today", true)]
        [TestCase("wait hold on a second", false)]
        [TestCase("", false)]
        public void Echo_IsWhatTheCharacterJustSaid(string transcript, bool echo)
        {
            Assert.That(CallSpeech.Repeats("It's good to hear you today. It is lovely.", transcript), Is.EqualTo(echo));
        }

        [Test]
        public void Speech_InterruptedBeforeAnythingPlayed_NothingWasHeard()
        {
            using (CallSpeech speech = Speech())
            {
                speech.Speak("m1", Long1 + " " + Long2, true);

                IDictionary<string, string> heard = speech.Interrupt();

                Assert.That(heard["m1"], Is.Empty);
            }
        }

        [Test]
        public void Speech_InterruptedPartWay_KeepsWhatStarted()
        {
            var second = new ManualResetEvent(false);
            int request = 0;
            using (CallSpeech speech = Speech(text =>
            {
                if (Interlocked.Increment(ref request) > 1)
                    second.WaitOne(5000); // the second piece never finishes generating
                return Wav(240);
            }))
            {
                speech.Speak("m1", Long1 + " " + Long2, true);
                WaitFor(() => request == 2);
                ReadPlayer(speech.Player, 480); // the first piece starts playing

                IDictionary<string, string> heard = speech.Interrupt();

                Assert.That(heard["m1"], Is.EqualTo(Long1));
                second.Set();
            }
        }

        [Test]
        public void Speech_FinishedReply_IsLeftAlone()
        {
            using (CallSpeech speech = Speech())
            {
                var played = new ManualResetEvent(false);
                speech.Player.ClipStarted += tag => played.Set();
                speech.Speak("m1", Long1, true);

                WaitFor(() => { ReadPlayer(speech.Player, 480); return played.WaitOne(0); });

                Assert.That(speech.Interrupt(), Is.Empty);
            }
        }

        [Test]
        public void Speech_KeepsTheWholeReplyWhileItsStillBeingWritten()
        {
            using (CallSpeech speech = Speech())
            {
                var played = new ManualResetEvent(false);
                speech.Player.ClipStarted += tag => played.Set();
                speech.Speak("m1", Long1 + " And", false);
                WaitFor(() => { ReadPlayer(speech.Player, 480); return played.WaitOne(0); });

                speech.Speak("m1", Long1 + " And then some.", false);
                IDictionary<string, string> heard = speech.Interrupt();

                Assert.That(heard["m1"], Is.EqualTo(Long1), "only what was spoken, not what was written since");
            }
        }

        [Test]
        public void Speech_InterruptedReply_StaysSilentWhenMoreOfItArrives()
        {
            using (CallSpeech speech = Speech())
            {
                speech.Speak("m1", Long1, false);
                speech.Interrupt();

                speech.Speak("m1", Long1 + " " + Long2, true);

                Assert.That(speech.Interrupt(), Is.Empty);
            }
        }

        [Test]
        public void Speech_Failure_IsReportedOncePerOutage()
        {
            var problems = new List<string>();
            var speech = new CallSpeech(null, text => { throw new LlmException(LlmErrorKind.Unreachable, "down"); },
                frame => { }, problem => { lock (problems) problems.Add(problem); });
            using (speech)
            {
                speech.StartGenerating();
                speech.Speak("m1", Long1 + " " + Long2, true);

                WaitFor(() => !speech.Busy);

                Assert.That(problems.Count, Is.EqualTo(1));
            }
        }

        private static CharacterChatService Chat(List<MessageEventArgs> events, out string conversation)
        {
            var me = new User("me", "me", "me");
            var chat = new CharacterChatService(() => me, events.Add, dialog => { }, new ObservableCollection<User>());
            string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            StoredCharacter character = new CharacterStore(folder).Add(new CharacterCard { Name = "Test" }, null);
            conversation = chat.Register(character).Identifier;
            return chat;
        }

        private static string Sent(CharacterChatService chat, List<MessageEventArgs> events, string conversation, string text)
        {
            chat.SendMessage(conversation, text, false);
            return ((MessageReceivedEventArgs)events.Last(e => e is MessageReceivedEventArgs)).Item.Identifier;
        }

        [Test]
        public void Chat_InterruptedReply_BecomesWhatWasHeard()
        {
            var events = new List<MessageEventArgs>();
            string conversation;
            using (CharacterChatService chat = Chat(events, out conversation))
            {
                string id = Sent(chat, events, conversation, "One. Two. Three.");

                chat.Interrupted(conversation, new Dictionary<string, string> { { id, "One." } });

                Assert.That(((Message)events.OfType<MessageEditedEventArgs>().Last().NewItem).Text, Is.EqualTo("One."));
            }
        }

        [Test]
        public void Chat_ReplyNobodyHeard_IsRemoved()
        {
            var events = new List<MessageEventArgs>();
            string conversation;
            using (CharacterChatService chat = Chat(events, out conversation))
            {
                string id = Sent(chat, events, conversation, "One.");

                chat.Interrupted(conversation, new Dictionary<string, string> { { id, "" } });

                Assert.That(events.OfType<MessageDeletedEventArgs>().Single().DeletedItemId, Is.EqualTo(id));
            }
        }

        // Set SKYLARPING_TTS_URL (and SKYLARPING_TTS_MODEL) to try the real thing.
        [Test]
        public void Live_ServerSpeaksClonesAndDesigns()
        {
            string url = Environment.GetEnvironmentVariable("SKYLARPING_TTS_URL");
            Assume.That(url, Is.Not.Null.And.Not.Empty, "needs a text-to-speech server");
            var endpoint = new LlmEndpoint(url, "", Environment.GetEnvironmentVariable("SKYLARPING_TTS_MODEL") ?? "OpenMOSS-TTS");

            byte[] designed = OpenAICompatClient.Synthesize(endpoint, "Hello there. It is good to finally hear your voice, after all this time.",
                voiceDescription: "A warm, low voice, calm and slightly amused.");
            byte[] sample = SpeechAudio.SampleFromSpeech(designed);
            Assert.That(SpeechAudio.Decode(sample).Length, Is.GreaterThan(SpeechAudio.SampleRate));

            var problems = new List<string>();
            var clips = new List<object>();
            byte[] heard = sample;
            string reference = Convert.ToBase64String(heard);
            using (var speech = new CallSpeech(null, text => OpenAICompatClient.Synthesize(endpoint, text, referenceWavBase64: reference),
                frame => { }, problems.Add))
            {
                speech.Player.ClipStarted += clips.Add;
                speech.StartGenerating();
                speech.Speak("m1", Long1 + " " + Long2, true);

                for (int i = 0; i < 1200 && clips.Count < 2 && problems.Count == 0; i++)
                {
                    ReadPlayer(speech.Player, 24000);
                    Thread.Sleep(100);
                }

                Assert.That(problems, Is.Empty);
                Assert.That(clips.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void Canceller_RemovesAnEchoAtACallLikeDelay()
        {
            const int frameSize = 320;
            const int delayFrames = 10; // 200 ms from the frame it's paired with
            var noise = new Random(1);
            var played = new List<short[]>();
            double before = 0, after = 0;
            int measured = 0;

            using (var canceller = new EchoCanceller(16000, 20, 500))
            {
                for (int n = 0; n < 750; n++)
                {
                    var frame = new short[frameSize];
                    for (int i = 0; i < frameSize; i++)
                        frame[i] = (short)noise.Next(-8000, 8000);
                    played.Add(frame);

                    var mic = new short[frameSize];
                    if (n >= delayFrames)
                        for (int i = 0; i < frameSize; i++)
                            mic[i] = (short)(played[n - delayFrames][i] / 2);

                    var output = new short[frameSize];
                    canceller.Cancel(mic, frame, output);

                    if (n >= 500)
                    {
                        before += mic.Sum(s => (double)s * s);
                        after += output.Sum(s => (double)s * s);
                        measured++;
                    }
                }
            }

            Assert.That(measured, Is.GreaterThan(0));
            Assert.That(after, Is.LessThan(before / 100), "at least 20 dB quieter once it has adapted");
        }
    }
}
