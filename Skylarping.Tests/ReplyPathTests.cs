// The paths that ask the model for a character's words: replies (typed, streamed, spoken on a
// call), messages the character starts, and mood messages. These run the whole service against
// a fake server and fake tools. Settings come from ChatServiceHooks: nothing here may write the
// real shared.xml or run a real tool.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.AI.Tools;
using Skylarping.Backend;
using Skylarping.Characters;
using Skylarping.Models;

namespace Skylarping.Tests
{
    [TestFixture]
    public class ReplyPathTests
    {
        private static readonly JArray Schema = JArray.Parse(
            "[{\"type\":\"function\",\"function\":{\"name\":\"look_up\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}]");

        private sealed class FakeTools : ICharacterTools
        {
            public readonly List<string> Ran = new List<string>();

            public JArray Schema { get { return ReplyPathTests.Schema; } }

            public List<string> ContextInjections(CancellationToken cancel)
            {
                return new List<string> { "Injected tool context." };
            }

            public ToolOutput Run(LlmToolCall call, CancellationToken cancel)
            {
                lock (Ran)
                    Ran.Add(call.Name);
                return new ToolOutput("tool result");
            }
        }

        private sealed class FakeSpeaker : ICallSpeaker
        {
            public readonly List<string> Finished = new List<string>();
            public int Partial;

            public void Speak(string messageId, string text, bool final)
            {
                lock (Finished)
                {
                    if (final)
                        Finished.Add(text);
                    else
                        Partial++;
                }
            }

            public IDictionary<string, string> Interrupt()
            {
                return new Dictionary<string, string>();
            }
        }

        private sealed class Harness : IDisposable
        {
            private readonly List<MessageEventArgs> _events = new List<MessageEventArgs>();
            private readonly List<string> _dialogs = new List<string>();
            private readonly User _me = new User("me", "me", "me");
            private readonly Func<string, int, KeyValuePair<int, string>> _script;
            private int _requests;

            public readonly FakeOpenAiServer Server = new FakeOpenAiServer();
            public readonly FakeTools Tools = new FakeTools();
            public readonly CharacterChatService Chat;
            public readonly string Conversation;
            public readonly User Character;
            public readonly List<string> Bodies = new List<string>();

            /// <param name="script">(request body, request number from 0) => the server's answer.</param>
            public Harness(Func<string, int, KeyValuePair<int, string>> script, CharacterCard card = null, bool tools = true)
            {
                _script = script;
                Server.Handler = (method, path, body, auth) =>
                {
                    int number;
                    lock (Bodies)
                    {
                        Bodies.Add(body);
                        number = _requests++;
                    }
                    return _script(body, number);
                };

                Chat = new CharacterChatService(() => _me, Record,
                    dialog => { lock (_dialogs) _dialogs.Add(dialog.Message); }, new ObservableCollection<User>());
                Chat.Hooks.Configured = () => true;
                Chat.Hooks.Endpoint = () => new LlmEndpoint(Server.BaseUrl, "", "");
                Chat.Hooks.StreamReplies = () => false;
                Chat.Hooks.MaxToolRounds = () => 8;
                Chat.Hooks.Tools = character => tools ? Tools : null;

                string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
                StoredCharacter stored = new CharacterStore(folder).Add(card ?? new CharacterCard { Name = "Test" }, null);
                DirectMessage conversation = Chat.Register(stored);
                Conversation = conversation.Identifier;
                Character = conversation.Partner;
            }

            private void Record(MessageEventArgs e)
            {
                lock (_events)
                    _events.Add(e);
            }

            /// <summary>The character's messages as they stand, oldest first.</summary>
            public List<string> Said()
            {
                lock (_events)
                {
                    var said = new List<KeyValuePair<string, string>>();
                    foreach (MessageEventArgs e in _events)
                    {
                        var received = e as MessageReceivedEventArgs;
                        var edited = e as MessageEditedEventArgs;
                        if (received != null && ((Message)received.Item).Author != _me)
                            said.Add(new KeyValuePair<string, string>(received.Item.Identifier, ((Message)received.Item).Text));
                        else if (edited != null)
                            said[said.FindIndex(m => m.Key == edited.OldItemId)] =
                                new KeyValuePair<string, string>(edited.OldItemId, ((Message)edited.NewItem).Text);
                    }
                    return said.Select(m => m.Value).ToList();
                }
            }

            public List<string> Dialogs()
            {
                lock (_dialogs)
                    return _dialogs.ToList();
            }

            public string Body(int number)
            {
                lock (Bodies)
                    return Bodies[number];
            }

            public int Requests
            {
                get { lock (Bodies) return Bodies.Count; }
            }

            /// <summary>Waits for a condition, then a moment longer for anything that shouldn't happen.</summary>
            public void Settle(Func<bool> done)
            {
                DateTime give_up = DateTime.UtcNow.AddSeconds(10);
                while (!done())
                {
                    if (DateTime.UtcNow > give_up)
                        Assert.Fail("Timed out. Said: [" + string.Join(" | ", Said().ToArray()) + "], dialogs: ["
                            + string.Join(" | ", Dialogs().ToArray()) + "], requests: " + Requests);
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

        private static KeyValuePair<int, string> Text(string text, string finish = "stop")
        {
            var content = new JValue(text).ToString(Newtonsoft.Json.Formatting.None);
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + content +
                "},\"finish_reason\":\"" + finish + "\"}]}");
        }

        private static KeyValuePair<int, string> Tool(string text = "")
        {
            var content = new JValue(text).ToString(Newtonsoft.Json.Formatting.None);
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + content + "," +
                "\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"look_up\",\"arguments\":\"{}\"}}]}," +
                "\"finish_reason\":\"tool_calls\"}]}");
        }

        private static KeyValuePair<int, string> Failure(int status)
        {
            return FakeOpenAiServer.Reply(status, "{\"error\":{\"message\":\"nope\"}}");
        }

        private static string Stream(params string[] events)
        {
            return string.Join("", events.Concat(new[] { "[DONE]" }).Select(e => "data: " + e + "\n\n").ToArray());
        }

        private static KeyValuePair<int, string> StreamedText(string first, string second)
        {
            return FakeOpenAiServer.Reply(200, Stream(
                "{\"choices\":[{\"delta\":{\"content\":\"" + first + "\"}}]}",
                "{\"choices\":[{\"delta\":{\"content\":\"" + second + "\"}}]}",
                "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}"));
        }

        private static KeyValuePair<int, string> StreamedTool(string text)
        {
            return FakeOpenAiServer.Reply(200, Stream(
                "{\"choices\":[{\"delta\":{\"content\":\"" + text + "\"}}]}",
                "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"look_up\",\"arguments\":\"{}\"}}]}}]}",
                "{\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}"));
        }

        private const string OffersTools = "\"tools\":[";

        [Test]
        public void PlainReply_IsPostedOnce()
        {
            using (var h = new Harness((body, n) => Text("Hello"), tools: false))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Hello" }, h.Said());
                Assert.AreEqual(1, h.Requests);
                StringAssert.DoesNotContain(OffersTools, h.Body(0));
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void ToolRound_PostsTheTextOfEachRoundAndSendsTheResultBack()
        {
            using (var h = new Harness((body, n) => n == 0 ? Tool("Let me check.") : Text("Found it.")))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 1);

                CollectionAssert.AreEqual(new[] { "Let me check.", "Found it." }, h.Said());
                CollectionAssert.AreEqual(new[] { "look_up" }, h.Tools.Ran);
                Assert.AreEqual(2, h.Requests);
                StringAssert.Contains(OffersTools, h.Body(0));
                StringAssert.Contains("Injected tool context.", h.Body(0));
                StringAssert.Contains("tool result", h.Body(1));
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void ToolsStopBeingOfferedAfterTheLastRound()
        {
            using (var h = new Harness((body, n) => n < 2 ? Tool() : Tool("Done anyway.")))
            {
                h.Chat.Hooks.MaxToolRounds = () => 2;
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 0);

                Assert.AreEqual(3, h.Requests);
                Assert.AreEqual(2, h.Tools.Ran.Count, "a tool asked for in the last round isn't run");
                StringAssert.Contains(OffersTools, h.Body(1));
                StringAssert.DoesNotContain(OffersTools, h.Body(2));
                CollectionAssert.AreEqual(new[] { "Done anyway." }, h.Said());
            }
        }

        [TestCase(500)]
        [TestCase(404)]
        public void ServerThatRejectsTools_IsAskedAgainWithoutThem(int status)
        {
            using (var h = new Harness((body, n) => body.Contains(OffersTools) ? Failure(status) : Text("Hello")))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Hello" }, h.Said());
                Assert.AreEqual(2, h.Requests);
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void OtherErrors_AreNotRetried()
        {
            using (var h = new Harness((body, n) => Failure(401)))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Dialogs().Count > 0);

                Assert.AreEqual(1, h.Requests);
                StringAssert.Contains("couldn't reply", h.Dialogs().Single());
            }
        }

        [Test]
        public void ErrorAfterAToolRound_IsNotRetried_AndKeepsWhatWasSaid()
        {
            using (var h = new Harness((body, n) => n == 0 ? Tool("Let me check.") : Failure(500)))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Dialogs().Count > 0);

                Assert.AreEqual(2, h.Requests);
                CollectionAssert.AreEqual(new[] { "Let me check." }, h.Said());
            }
        }

        [Test]
        public void EmptyReply_IsReported()
        {
            using (var h = new Harness((body, n) => Text("")))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Dialogs().Count > 0);

                StringAssert.Contains("an empty reply", h.Dialogs().Single());
            }
        }

        [Test]
        public void ReplyCutOffByLength_IsReported()
        {
            using (var h = new Harness((body, n) => Text("", "length")))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Dialogs().Count > 0);

                StringAssert.Contains("ran out of reply length", h.Dialogs().Single());
            }
        }

        [Test]
        public void NoActionText_StripsActionsFromEachRound()
        {
            var card = new CharacterCard { Name = "Test", SuppressActions = true };
            using (var h = new Harness((body, n) => n == 0 ? Tool("*looks* Checking.") : Text("Found it. *smiles*"), card))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 1);

                CollectionAssert.AreEqual(new[] { "Checking.", "Found it." }, h.Said());
            }
        }

        [Test]
        public void NoActionText_ReportsAReplyThatWasOnlyActions()
        {
            var card = new CharacterCard { Name = "Test", SuppressActions = true };
            using (var h = new Harness((body, n) => Text("*smiles*"), card))
            {
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Dialogs().Count > 0);

                StringAssert.Contains("only *actions*", h.Dialogs().Single());
                CollectionAssert.IsEmpty(h.Said());
            }
        }

        [Test]
        public void ANewMessage_StopsTheReplyBeingWritten()
        {
            Harness harness = null;
            using (var h = new Harness((body, n) =>
            {
                if (n > 0)
                    return Text("New");
                harness.Chat.SendMessage(harness.Conversation, "Wait", false);
                return Tool("Old");
            }))
            {
                harness = h;
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "New" }, h.Said());
                CollectionAssert.IsEmpty(h.Tools.Ran, "the old reply's tool call is dropped");
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void StreamedReply_IsOneMessagePerRound()
        {
            using (var h = new Harness((body, n) => n == 0 ? StreamedTool("Checking.") : StreamedText("Found ", "it.")))
            {
                h.Chat.Hooks.StreamReplies = () => true;
                h.Chat.SendMessage(h.Conversation, "Hi", false);
                h.Settle(() => h.Said().Contains("Found it."));

                CollectionAssert.AreEqual(new[] { "Checking.", "Found it." }, h.Said());
                CollectionAssert.AreEqual(new[] { "look_up" }, h.Tools.Ran);
                StringAssert.Contains("\"stream\":true", h.Body(0));
            }
        }

        [Test]
        public void CallReply_IsSpokenRoundByRound_EvenWithStreamingOff()
        {
            var speaker = new FakeSpeaker();
            using (var h = new Harness((body, n) => n == 0 ? StreamedTool("Checking.") : StreamedText("Found ", "it.")))
            {
                h.Chat.SetSpeaker(h.Conversation, speaker);
                h.Chat.SendSpoken(h.Conversation, "Hi");
                h.Settle(() => speaker.Finished.Count > 1);

                CollectionAssert.AreEqual(new[] { "Checking.", "Found it." }, speaker.Finished);
                Assert.Greater(speaker.Partial, 0, "spoken while it's written");
                CollectionAssert.AreEqual(new[] { "Checking.", "Found it." }, h.Said());
            }
        }

        [Test]
        public void ReplyTheyStarted_IsPromptedByAHiddenNote()
        {
            using (var h = new Harness((body, n) => Text("Hello!")))
            {
                h.Chat.AnswerCall(h.Conversation);
                h.Settle(() => h.Said().Count > 0);

                StringAssert.Contains("just called", h.Body(0));
                CollectionAssert.AreEqual(new[] { "Hello!" }, h.Said());
            }
        }

        [Test]
        public void ReplyTheyStarted_FailsSilently()
        {
            using (var h = new Harness((body, n) => Failure(401)))
            {
                h.Chat.AnswerCall(h.Conversation);
                h.Settle(() => h.Requests > 0);

                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        private static CharacterCard MoodCard()
        {
            return new CharacterCard { Name = "Test", UpdateMood = true };
        }

        [Test]
        public void Mood_UsesToolsAndContext_AndOnlyTheLastTextCounts()
        {
            using (var h = new Harness((body, n) => n == 0 ? Tool("Let me check.") : Text("Feeling good"), MoodCard()))
            {
                h.Chat.EnsureMood(h.Conversation);
                h.Settle(() => h.Character.Status == "Feeling good");

                CollectionAssert.AreEqual(new[] { "look_up" }, h.Tools.Ran);
                StringAssert.Contains("Injected tool context.", h.Body(0));
                StringAssert.Contains("tool result", h.Body(1));
                CollectionAssert.IsEmpty(h.Said(), "nothing in the chat");
            }
        }

        [Test]
        public void Mood_AnswersWithoutToolsIfTheServerRejectsThem()
        {
            using (var h = new Harness((body, n) => body.Contains(OffersTools) ? Failure(500) : Text("Feeling good"), MoodCard()))
            {
                h.Chat.EnsureMood(h.Conversation);
                h.Settle(() => h.Character.Status == "Feeling good");

                Assert.AreEqual(2, h.Requests);
            }
        }

        [Test]
        public void Mood_ErrorsAreSilent()
        {
            using (var h = new Harness((body, n) => Failure(401), MoodCard()))
            {
                h.Chat.EnsureMood(h.Conversation);
                h.Settle(() => h.Requests > 0);

                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void Greeting_RunsMacrosOnce()
        {
            var card = new CharacterCard
            {
                Name = "Test",
                UseGreeting = true,
                FirstMessage = "Hi {{user}}, I'm {{char}}.{{setvar::met::yes}}{{getvar::met}}",
            };
            using (var h = new Harness((body, n) => Text("unused"), card))
            {
                List<ConversationItem> items = h.Chat.FetchMessages(h.Conversation, Fetch.Newest, null);

                Assert.AreEqual("Hi me, I'm Test.yes", ((Message)items.Single()).Text);
                Assert.AreEqual(0, h.Requests);
            }
        }
    }
}
