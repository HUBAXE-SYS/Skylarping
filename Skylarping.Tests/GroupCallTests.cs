// Group calls and directed questions. The audio side uses the real player and speech queue with
// no device; the call side uses a fake speaker. Real sound still has to be tried on a real call.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Backend;
using Skylarping.Characters;
using Skylarping.Models;

namespace Skylarping.Tests
{
    [TestFixture]
    public class QuestionDetectionTests
    {
        [TestCase("Are you coming?", true)]
        [TestCase("Are you coming?  ", true)]
        [TestCase("\"Are you coming?\"", true)]
        [TestCase("Are you coming? *smiles*", true)]
        [TestCase("Are you coming? \U0001F600", true)]
        [TestCase("Are you coming?”", true)]
        [TestCase("你好吗？", true)]
        [TestCase("Right? Anyway, I'm off.", false)]
        [TestCase("I'm off.", false)]
        [TestCase("*wonders why?* Fine.", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AMessageMustEndOnTheQuestion(string text, bool expected)
        {
            Assert.AreEqual(expected, GroupTurns.EndsInQuestion(text));
        }
    }

    [TestFixture]
    public class CallAudioTests
    {
        private static short[] Tone(int count)
        {
            return Enumerable.Repeat((short)1000, count).ToArray();
        }

        private static short[] ReadSamples(SpeechPlayer player, int count)
        {
            var bytes = new byte[count * 2];
            player.Read(bytes, 0, bytes.Length);
            var samples = new short[count];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }

        [Test]
        public void APauseIsSilenceThatKeepsTheOrder()
        {
            using (var player = new SpeechPlayer(frame => { }))
            {
                var started = new List<object>();
                player.ClipStarted += tag => started.Add(tag);
                player.Enqueue(Tone(100), "first");
                player.EnqueueSilence(10); // 240 samples at 24 kHz
                player.Enqueue(Tone(100), "second");

                short[] samples = ReadSamples(player, 440);

                Assert.IsTrue(samples.Take(100).All(s => s == 1000));
                Assert.IsTrue(samples.Skip(100).Take(240).All(s => s == 0));
                Assert.IsTrue(samples.Skip(340).All(s => s == 1000));
                CollectionAssert.AreEqual(new object[] { "first", "second" }, started, "a pause isn't a clip anyone heard");
            }
        }

        [Test]
        public void HoldKeepsTheNextSpeakerBackUntilReleased()
        {
            using (var player = new SpeechPlayer(frame => { }))
            {
                player.Hold = true;
                player.Enqueue(Tone(100), "held");

                Assert.IsTrue(ReadSamples(player, 100).All(s => s == 0), "nothing plays while the user talks");

                player.Hold = false;

                Assert.IsTrue(ReadSamples(player, 100).All(s => s == 1000));
            }
        }

        [Test]
        public void HoldLetsAClipAlreadyPlayingFinish()
        {
            using (var player = new SpeechPlayer(frame => { }))
            {
                player.Enqueue(Tone(200), "playing");
                ReadSamples(player, 100);

                player.Hold = true;

                Assert.IsTrue(ReadSamples(player, 100).All(s => s == 1000));
            }
        }

        [Test]
        public void APauseCountsAsBusyButNotAsSomeoneTalking()
        {
            using (var player = new SpeechPlayer(frame => { }))
            {
                Assert.IsFalse(player.Audible);
                player.EnqueueSilence(1000);
                ReadSamples(player, 100);

                Assert.IsTrue(player.Busy, "the queue is still going");
                Assert.IsFalse(player.Audible, "nobody is speaking");

                player.Clear();
                player.Enqueue(Tone(1000), "voice");
                ReadSamples(player, 100);

                Assert.IsTrue(player.Audible);
            }
        }

        [Test]
        public void EachReplyIsSpokenInItsOwnVoice()
        {
            var voices = new List<string>();
            var wav = SpeechAudioFixture.Wav();
            using (var speech = new CallSpeech(new CallVoice(), (text, sample) =>
            {
                lock (voices)
                    voices.Add(text + "=" + (sample ?? "default"));
                return wav;
            }, frame => { }, problem => Assert.Fail(problem)))
            {
                speech.StartGenerating();
                speech.Speak("m1", "This is the first speaker, talking for long enough to count as a sentence.", true,
                    new CallTurn { SpeakerId = "a", Sample = "voice-a" });
                speech.Speak("m2", "This is the second speaker, who has no sample of their own at all, sadly.", true,
                    new CallTurn { SpeakerId = "b" });

                for (int i = 0; i < 500 && voices.Count < 2; i++)
                    Thread.Sleep(10);

                Assert.AreEqual(2, voices.Count);
                StringAssert.EndsWith("=voice-a", voices[0]);
                StringAssert.EndsWith("=default", voices[1]);
            }
        }

        [Test]
        public void RepliesWaitingCountsThoseNotYetPlaying()
        {
            using (var speech = new CallSpeech(new CallVoice(), (text, sample) => SpeechAudioFixture.Wav(), frame => { }, problem => { }))
            {
                speech.StartGenerating();
                speech.Speak("m1", "A reply that is long enough to be treated as a sentence on its own, certainly.", true,
                    new CallTurn { SpeakerId = "a" });

                Assert.AreEqual(1, speech.RepliesWaiting);

                for (int i = 0; i < 500 && speech.Player.Busy == false; i++)
                    Thread.Sleep(10);
                var bytes = new byte[4800];
                speech.Player.Read(bytes, 0, bytes.Length);

                for (int i = 0; i < 500 && speech.RepliesWaiting > 0; i++)
                    Thread.Sleep(10);
                Assert.AreEqual(0, speech.RepliesWaiting);
                Assert.AreEqual("a", speech.LastSpeakerId);
            }
        }
    }

    internal static class SpeechAudioFixture
    {
        /// <summary>A short mono 24 kHz WAV.</summary>
        public static byte[] Wav()
        {
            const int samples = 2400;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(24000);
                writer.Write(48000);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                    writer.Write((short)1000);
                return stream.ToArray();
            }
        }
    }

    [TestFixture]
    public class GroupConversationTests
    {
        private sealed class FakeSpeaker : ICallSpeaker
        {
            public readonly List<CallTurn> Turns = new List<CallTurn>();
            public readonly List<string> Finished = new List<string>();
            public int Interrupts;
            public volatile int Waiting;
            public volatile bool IsBusy;

            public int RepliesWaiting
            {
                get { return Waiting; }
            }

            public bool Busy
            {
                get { return IsBusy; }
            }

            public void Speak(string messageId, string text, bool final, CallTurn turn = null)
            {
                lock (Turns)
                {
                    if (!final)
                        return;
                    Turns.Add(turn);
                    Finished.Add(text);
                }
            }

            public IDictionary<string, string> Interrupt()
            {
                Interlocked.Increment(ref Interrupts);
                return new Dictionary<string, string>();
            }
        }

        private sealed class Harness : IDisposable
        {
            private readonly List<MessageEventArgs> _events = new List<MessageEventArgs>();
            private readonly List<string> _ids = new List<string>();
            private readonly Dictionary<string, string> _idOf = new Dictionary<string, string>();
            private int _number;

            public readonly User Me = new User("Al", "Al", "Al");
            public readonly FakeOpenAiServer Server = new FakeOpenAiServer();
            public readonly CharacterChatService Chat;
            public readonly List<string> Bodies = new List<string>();
            public readonly StoredGroup Stored;
            public readonly FakeSpeaker Speaker = new FakeSpeaker();
            public readonly string Group;
            public Func<string, int, KeyValuePair<int, string>> Script;

            public Harness(Func<string, int, KeyValuePair<int, string>> script, bool questions = true, bool voiceForBob = false)
            {
                Script = script;
                Server.Handler = (method, path, body, auth) =>
                {
                    int number;
                    lock (Bodies)
                    {
                        Bodies.Add(body);
                        number = _number++;
                    }
                    return Script(body, number);
                };
                Chat = new CharacterChatService(() => Me, Record, d => { }, new TypingTracker(new ObservableCollection<User>()));
                Chat.Hooks.Configured = () => true;
                Chat.Hooks.Endpoint = () => new LlmEndpoint(Server.BaseUrl, "", "");
                Chat.Hooks.StreamReplies = () => false;
                Chat.Hooks.MaxToolRounds = () => 8;
                Chat.Hooks.Tools = c => null;
                Chat.Hooks.DirectQuestions = () => questions;

                string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
                var store = new CharacterStore(Path.Combine(folder, "Characters"));
                foreach (string name in new[] { "Bob", "Mina" })
                {
                    StoredCharacter character = store.Add(new CharacterCard { Name = name }, null);
                    if (voiceForBob && name == "Bob")
                        CharacterStore.WriteVoiceSample(character.Folder, new byte[] { 1, 2, 3 });
                    _idOf[name] = Chat.Register(character).Identifier;
                    _ids.Add(_idOf[name]);
                }
                Stored = new GroupStore(Path.Combine(folder, "Groups")).Add(new StoredGroup
                {
                    Name = "Book Club",
                    MemberIds = _ids.ToList(),
                    DelayMinSeconds = 0,
                    DelayMaxSeconds = 0,
                    CallPauseMinSeconds = 2,
                    CallPauseMaxSeconds = 2,
                });
                Group = Chat.RegisterGroup(Stored).Identifier;
            }

            public string Id(string name)
            {
                return _idOf[name];
            }

            public void StartCall()
            {
                Chat.SetInCall(Group, true);
                Chat.SetSpeaker(Group, Speaker);
            }

            private void Record(MessageEventArgs e)
            {
                lock (_events)
                    _events.Add(e);
            }

            public List<string> Said()
            {
                lock (_events)
                    return _events.OfType<MessageReceivedEventArgs>().Select(e => e.Item as Message)
                        .Where(m => m != null && m.Author != Me)
                        .Select(m => m.Author.DisplayName + ": " + m.Text).ToList();
            }

            public int Requests
            {
                get { lock (Bodies) return Bodies.Count; }
            }

            public string Body(int number)
            {
                lock (Bodies)
                    return Bodies[number];
            }

            public void Settle(Func<bool> done)
            {
                DateTime give_up = DateTime.UtcNow.AddSeconds(10);
                while (!done())
                {
                    if (DateTime.UtcNow > give_up)
                        Assert.Fail("Timed out. Said: [" + string.Join(" | ", Said().ToArray()) + "], requests: " + Requests);
                    Thread.Sleep(10);
                }
                Thread.Sleep(150);
            }

            public void Dispose()
            {
                Chat.Dispose();
                Server.Dispose();
            }
        }

        private static string Speaker(string body)
        {
            return body.Contains("You are Bob.") ? "Bob" : "Mina";
        }

        private static KeyValuePair<int, string> Stream(string text)
        {
            string content = new JValue(text).ToString(Newtonsoft.Json.Formatting.None);
            return FakeOpenAiServer.Reply(200, "data: {\"choices\":[{\"delta\":{\"content\":" + content + "}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        }

        private static KeyValuePair<int, string> Text(string text)
        {
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" +
                new JValue(text).ToString(Newtonsoft.Json.Formatting.None) + "},\"finish_reason\":\"stop\"}]}");
        }

        private static KeyValuePair<int, string> Reply(string body, string text)
        {
            return body.Contains("\"stream\":true") ? Stream(text) : Text(text);
        }

        [Test]
        public void AQuestionNamingSomeoneGoesToThem_WithANoteSayingWhoAsked()
        {
            using (var h = new Harness((body, n) => Reply(body, n == 0 ? "Mina, are you coming?" : "Yes, Friday works.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 1);

                CollectionAssert.AreEqual(new[] { "Bob: Mina, are you coming?", "Mina: Yes, Friday works." }, h.Said());
                StringAssert.Contains("Bob just asked Mina that", h.Body(1));
            }
        }

        [Test]
        public void AQuestionNamingNoOneGoesBackToTheLastCharacter_UntilSomeoneAnswers()
        {
            string[] lines = { "Ask Mina.", "Why me?", "You know the most.", "Fine." };
            using (var h = new Harness((body, n) => Reply(body, n < lines.Length ? lines[n] : "Done.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count >= 3);

                CollectionAssert.AreEqual(new[] { "Bob: Ask Mina.", "Mina: Why me?", "Bob: You know the most." }, h.Said());
                Assert.AreEqual(3, h.Requests, "that wasn't a question, so it ends there");
            }
        }

        [Test]
        public void AQuestionAfterTheUserIsForTheUser()
        {
            using (var h = new Harness((body, n) => Reply(body, "And you, how are you?")))
            {
                h.Chat.SendMessage(h.Group, "Bob, how are you?", false);
                h.Settle(() => h.Said().Count > 0);

                Assert.AreEqual(1, h.Requests);
            }
        }

        [Test]
        public void WithTheOptionOff_OnlyNamesSetAnyoneOff()
        {
            using (var h = new Harness((body, n) => Reply(body, n == 0 ? "Ask Mina." : "Why me?"), questions: false))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 1);

                Assert.AreEqual(2, h.Requests, "Mina's question goes nowhere");
                StringAssert.DoesNotContain("just asked", h.Body(1));
            }
        }

        [Test]
        public void QuestionsCountUnderTheChainLimit()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Ask Mina, why not?" : "Why me?")))
            {
                h.Stored.ChainLimit = 2;
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Requests >= 3);
                Thread.Sleep(300);

                Assert.AreEqual(3, h.Requests, "the first reply and 2 chained");
            }
        }

        [Test]
        public void AGroupCallStartsQuiet()
        {
            using (var h = new Harness((body, n) => Reply(body, "Hi.")))
            {
                h.StartCall();
                Thread.Sleep(300);

                Assert.AreEqual(0, h.Requests);
            }
        }

        [Test]
        public void OnACallEveryoneSpeaksInTheirOwnVoice_WithAPauseBetween()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Ask Mina." : "Sure."), voiceForBob: true))
            {
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Bob?");
                h.Settle(() => h.Speaker.Finished.Count > 1);

                CollectionAssert.AreEqual(new[] { "Ask Mina.", "Sure." }, h.Speaker.Finished);
                Assert.AreEqual(h.Id("Bob"), h.Speaker.Turns[0].SpeakerId);
                Assert.AreEqual(Convert.ToBase64String(new byte[] { 1, 2, 3 }), h.Speaker.Turns[0].Sample);
                Assert.AreEqual(0, h.Speaker.Turns[0].PauseMilliseconds, "the first reply to you isn't held back");
                Assert.AreEqual(h.Id("Mina"), h.Speaker.Turns[1].SpeakerId);
                Assert.IsNull(h.Speaker.Turns[1].Sample, "no sample means the default voice");
                Assert.AreEqual(2000, h.Speaker.Turns[1].PauseMilliseconds);
            }
        }

        [Test]
        public void CharactersNeverInterruptEachOther_OnlyTheUserDoes()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Ask Mina." : "Sure.")))
            {
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Bob?");
                h.Settle(() => h.Speaker.Finished.Count > 1);
                Assert.AreEqual(1, h.Speaker.Interrupts, "once, for the user's message");

                h.Chat.SendSpoken(h.Group, "Bob again");
                h.Settle(() => h.Speaker.Finished.Count > 3);
                Assert.AreEqual(2, h.Speaker.Interrupts);
            }
        }

        [Test]
        public void TheNextReplyWaitsForTheLastToStartPlaying()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Ask Mina." : "Sure.")))
            {
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Bob?");
                h.Settle(() => h.Speaker.Finished.Count > 0);
                // Bob's reply is written but hasn't reached the speaker, as if it's being generated.
                h.Speaker.Waiting = 1;
                h.Chat.SendSpoken(h.Group, "Bob, once more");
                h.Settle(() => h.Speaker.Finished.Count > 0);

                int written = h.Requests;
                Thread.Sleep(400);
                Assert.AreEqual(written, h.Requests, "Mina waits");

                h.Speaker.Waiting = 0;
                h.Settle(() => h.Requests > written);
            }
        }

        [Test]
        public void TheTextDelayDoesntApplyOnACall()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Ask Mina." : "Sure.")))
            {
                h.Stored.DelayMinSeconds = 60;
                h.Stored.DelayMaxSeconds = 60;
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Bob?");
                h.Settle(() => h.Speaker.Finished.Count > 1);
            }
        }

        [Test]
        public void TheUserCuttingInEndsTheCycle()
        {
            using (var h = new Harness((body, n) => Reply(body, Speaker(body) == "Bob" ? "Over to you, Mina." : "Over to you, Bob.")))
            {
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Bob?");
                h.Settle(() => h.Speaker.Finished.Count >= 2);

                h.Chat.Interrupted(h.Group, new Dictionary<string, string> { { "gone", "" } });
                Thread.Sleep(300);
                int said = h.Requests;
                Thread.Sleep(300);

                Assert.AreEqual(said, h.Requests);
            }
        }

        [Test]
        public void MembersAreOnTheCall_SoTheyCanSpeakOnIt()
        {
            using (var h = new Harness((body, n) => Reply(body, "Hi.")))
            {
                h.StartCall();
                h.Chat.SendSpoken(h.Group, "Mina?");
                h.Settle(() => h.Speaker.Finished.Count > 0);

                Assert.AreEqual(h.Id("Mina"), h.Speaker.Turns[0].SpeakerId);
            }
        }

        [Test]
        public void OnACallQuietIsInSeconds()
        {
            using (var h = new Harness((body, n) => Reply(body, "Still there?")))
            {
                h.StartCall();

                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(10));
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests, "not 20 seconds yet");

                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(50));
                h.Settle(() => h.Requests > 0);
                StringAssert.Contains("own initiative", h.Body(0));
            }
        }

        [Test]
        public void QuietCountsFromWhenSpeechEnds()
        {
            using (var h = new Harness((body, n) => Reply(body, "Hi.")))
            {
                h.StartCall();
                h.Speaker.IsBusy = true;
                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(50));
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests, "someone's still speaking");

                h.Speaker.IsBusy = false;
                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(10));
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests, "the clock started when the speech ended");
            }
        }

        [Test]
        public void DoNotDisturbStopsUnpromptedTalkOnACall()
        {
            using (var h = new Harness((body, n) => Reply(body, "Hi.")))
            {
                h.StartCall();
                h.Me.ConnectionStatus = PresenceStatus.DoNotDisturb;
                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(50));
                Thread.Sleep(200);

                Assert.AreEqual(0, h.Requests);
            }
        }

        [Test]
        public void HangingUpGoesBackToMinutesOfQuiet()
        {
            using (var h = new Harness((body, n) => Reply(body, "Hi.")))
            {
                h.StartCall();
                h.Chat.SetSpeaker(h.Group, null);
                h.Chat.SetInCall(h.Group, false);

                h.Chat.CheckUnprompted(DateTime.UtcNow.AddSeconds(50));
                Thread.Sleep(200);

                Assert.AreEqual(0, h.Requests);
            }
        }

        [Test]
        public void CallSettingsSurviveARestart()
        {
            var store = new GroupStore(Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N")));
            store.Add(new StoredGroup { Name = "x", CallPauseMinSeconds = 4, CallPauseMaxSeconds = 5, CallQuietMinSeconds = 6, CallQuietMaxSeconds = 7 });

            StoredGroup loaded = store.LoadAll().Single();

            Assert.AreEqual(4, loaded.CallPauseMinSeconds);
            Assert.AreEqual(5, loaded.CallPauseMaxSeconds);
            Assert.AreEqual(6, loaded.CallQuietMinSeconds);
            Assert.AreEqual(7, loaded.CallQuietMaxSeconds);

            var defaults = new StoredGroup();
            Assert.AreEqual(1, defaults.CallPauseMinSeconds);
            Assert.AreEqual(3, defaults.CallPauseMaxSeconds);
            Assert.AreEqual(20, defaults.CallQuietMinSeconds);
            Assert.AreEqual(45, defaults.CallQuietMaxSeconds);
        }
    }
}
