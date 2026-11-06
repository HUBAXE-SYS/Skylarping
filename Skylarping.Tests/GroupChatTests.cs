// Group chats: who speaks, what they see, the unprompted loop, summaries and mood context. Like
// ReplyPathTests, these run the whole service against a fake server and fake tools, and nothing
// here may write the real shared.xml or run a real tool.

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
    public class GroupTurnsTests
    {
        private static GroupMember Member(string id, string name, double talkativeness = 0.5, string nickname = null)
        {
            CharacterCard card = CharacterCard.FromJson(
                new JObject { ["spec"] = "chara_card_v3", ["data"] = new JObject { ["name"] = name, ["nickname"] = nickname ?? "" } }.ToString());
            card.Talkativeness = talkativeness;
            return new GroupMember(id, card);
        }

        private static readonly GroupMember[] Everyone =
        {
            Member("b", "Bob"), Member("s", "Seraphina Moonwhisper"), Member("m", "Mina", nickname: "Min"),
        };

        [TestCase("hello there")]
        [TestCase("a bobcat and a bobby pin")]
        [TestCase("email me at x@ybob.com")]
        public void NoOneIsNamed(string text)
        {
            CollectionAssert.IsEmpty(GroupTurns.Mentioned(text, Everyone));
        }

        [TestCase("Hi Bob", "b")]
        [TestCase("hi bob!", "b")]
        [TestCase("@Bob what do you think?", "b")]
        [TestCase("what about Seraphina Moonwhisper", "s")]
        [TestCase("what about Seraphina", "s")]
        [TestCase("@seraphina moonwhisper", "s")]
        [TestCase("Min, your turn", "m")]
        public void ANameCounts(string text, string expected)
        {
            CollectionAssert.AreEqual(new[] { expected }, GroupTurns.Mentioned(text, Everyone));
        }

        [Test]
        public void NamesAreInTheOrderTheyWereSaid()
        {
            CollectionAssert.AreEqual(new[] { "m", "b" }, GroupTurns.Mentioned("Mina and Bob, and Mina again", Everyone));
        }

        [Test]
        public void NobodyIsSetOffByTheirOwnName()
        {
            CollectionAssert.AreEqual(new[] { "m" }, GroupTurns.Mentioned("Bob says hi to Mina", Everyone, "b"));
        }

        [TestCase("Seraphina Moonwhisper", "", true)]
        [TestCase("Seraphina Moonwhisper", "ser", true)]
        [TestCase("Seraphina Moonwhisper", "moon", true)]
        [TestCase("Seraphina Moonwhisper", "Seraphina M", true)]
        [TestCase("Seraphina Moonwhisper", "whisper", false)]
        [TestCase("Bob", "x", false)]
        public void TagsMatchTheStartOfAnyWord(string name, string query, bool expected)
        {
            Assert.AreEqual(expected, GroupTurns.MatchesTag(name, query));
        }

        [Test]
        public void TagsAreFoundForBold()
        {
            var names = new[] { "Mina", "Mina Lee", "Bob" };

            Assert.AreEqual(new[] { new KeyValuePair<int, int>(3, 9), new KeyValuePair<int, int>(17, 4) },
                Skylarping.Formatting.Formatter.FindMentions("Hi @Mina Lee and @BOB", names).ToArray());
            CollectionAssert.IsEmpty(Skylarping.Formatting.Formatter.FindMentions("mail x@Bob or @Bobby", names));
            CollectionAssert.IsEmpty(Skylarping.Formatting.Formatter.FindMentions("@Bob", null));
        }

        [Test]
        public void PickChoosesOneOrTwoDifferentMembers()
        {
            var counts = new HashSet<int>();
            var random = new Random(1);
            for (int i = 0; i < 200; i++)
            {
                List<string> picked = GroupTurns.Pick(Everyone, random);
                counts.Add(picked.Count);
                Assert.AreEqual(picked.Count, picked.Distinct().Count());
            }
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, counts);
        }

        [Test]
        public void PickPrefersTheTalkative()
        {
            var members = new[] { Member("loud", "Loud", 1), Member("quiet", "Quiet", 0) };
            var random = new Random(2);
            int loudFirst = 0;
            for (int i = 0; i < 1000; i++)
                if (GroupTurns.Pick(members, random)[0] == "loud")
                    loudFirst++;
            Assert.Greater(loudFirst, 900);
            Assert.Less(loudFirst, 1000, "the quietest still speak now and then");
        }

        [Test]
        public void PickCopesWithSmallGroups()
        {
            CollectionAssert.IsEmpty(GroupTurns.Pick(new GroupMember[0], new Random()));
            CollectionAssert.AreEqual(new[] { "b" }, GroupTurns.Pick(new[] { Everyone[0] }, new Random()));
        }
    }

    [TestFixture]
    public class GroupDataTests
    {
        private static string Folder()
        {
            return Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void NewGroupsHaveTheAgreedDefaults()
        {
            var group = new StoredGroup();
            Assert.IsTrue(group.Unprompted);
            Assert.AreEqual(10, group.QuietMinMinutes);
            Assert.AreEqual(30, group.QuietMaxMinutes);
            Assert.AreEqual(3, group.Repeats);
            Assert.AreEqual(15, group.DelayMinSeconds);
            Assert.AreEqual(60, group.DelayMaxSeconds);
            Assert.AreEqual(0, group.ChainLimit, "no limit");
        }

        [Test]
        public void GroupsSurviveARestart()
        {
            var store = new GroupStore(Folder());
            StoredGroup saved = store.Add(new StoredGroup
            {
                Name = "Book Club",
                MemberIds = new List<string> { "char_a", "char_b" },
                Unprompted = false,
                QuietMinMinutes = 1,
                QuietMaxMinutes = 2,
                Repeats = 0,
                DelayMinSeconds = 3,
                DelayMaxSeconds = 4,
                ChainLimit = 5,
            });
            Assert.IsTrue(GroupStore.IsGroupId(saved.Id));

            StoredGroup loaded = new GroupStore(store.Folder).LoadAll().Single();

            Assert.AreEqual(saved.Id, loaded.Id);
            Assert.AreEqual("Book Club", loaded.Name);
            CollectionAssert.AreEqual(new[] { "char_a", "char_b" }, loaded.MemberIds);
            Assert.IsFalse(loaded.Unprompted);
            Assert.AreEqual(1, loaded.QuietMinMinutes);
            Assert.AreEqual(2, loaded.QuietMaxMinutes);
            Assert.AreEqual(0, loaded.Repeats);
            Assert.AreEqual(3, loaded.DelayMinSeconds);
            Assert.AreEqual(4, loaded.DelayMaxSeconds);
            Assert.AreEqual(5, loaded.ChainLimit);

            store.Delete(saved.Id);
            CollectionAssert.IsEmpty(store.LoadAll());
        }

        [Test]
        public void ADamagedGroupDoesntStopTheOthers()
        {
            var store = new GroupStore(Folder());
            store.Add(new StoredGroup { Name = "Good" });
            string bad = Path.Combine(store.Folder, "group_bad");
            Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad, "group.json"), "not json");

            Assert.AreEqual("Good", store.LoadAll().Single().Name);
        }

        [Test]
        public void SummaryAndTalkativenessTravelWithTheCard()
        {
            var card = new CharacterCard { Name = "Bob", GroupSummary = "A mechanic.", GroupSummarySource = "x", Talkativeness = 0.8 };

            CharacterCard loaded = CharacterCard.FromJson(card.ToJson());

            Assert.AreEqual("A mechanic.", loaded.GroupSummary);
            Assert.AreEqual("x", loaded.GroupSummarySource);
            Assert.AreEqual(0.8, loaded.Talkativeness);
        }

        [Test]
        public void TalkativenessIsReadFromSillyTavernCards()
        {
            string card = @"{""spec"":""chara_card_v2"",""data"":{""name"":""Bob"",""extensions"":{""talkativeness"":""0.2""}}}";
            Assert.AreEqual(0.2, CharacterCard.FromJson(card).Talkativeness);

            string missing = @"{""spec"":""chara_card_v2"",""data"":{""name"":""Bob""}}";
            Assert.AreEqual(0.5, CharacterCard.FromJson(missing).Talkativeness);

            string bad = @"{""spec"":""chara_card_v2"",""data"":{""name"":""Bob"",""extensions"":{""talkativeness"":""7""}}}";
            Assert.AreEqual(0.5, CharacterCard.FromJson(bad).Talkativeness);
        }

        [Test]
        public void ACardNeedsASummaryWhenItHasNoneOrChanges()
        {
            var card = new CharacterCard { Name = "Bob", Description = "A mechanic." };
            Assert.IsTrue(card.NeedsGroupSummary, "none yet");

            card.GroupSummary = "Bob fixes vans.";
            card.GroupSummarySource = card.GroupSummaryFingerprint;
            Assert.IsFalse(card.NeedsGroupSummary);

            card.GroupSummary = "Edited by hand.";
            Assert.IsFalse(card.NeedsGroupSummary, "an edited summary stays");

            card.ProactiveMessages = true;
            card.SuppressActions = true;
            card.Talkativeness = 1;
            Assert.IsFalse(card.NeedsGroupSummary, "settings don't count");

            card.Description = "A retired mechanic.";
            Assert.IsTrue(card.NeedsGroupSummary, "the character changed");
        }

        [TestCase("Name")]
        [TestCase("Description")]
        [TestCase("Personality")]
        [TestCase("Scenario")]
        public void EachFieldCountsAsAChange(string field)
        {
            var card = new CharacterCard { GroupSummary = "x" };
            card.GroupSummarySource = card.GroupSummaryFingerprint;

            typeof(CharacterCard).GetProperty(field).SetValue(card, "changed", null);

            Assert.IsTrue(card.NeedsGroupSummary);
        }

        [Test]
        public void SummaryFallsBackToTheCard()
        {
            Assert.AreEqual("Summary.", PromptBuilder.SummaryOf(new CharacterCard { GroupSummary = "Summary.", Personality = "Kind" }, "Al"));
            Assert.AreEqual("Kind to Al", PromptBuilder.SummaryOf(new CharacterCard { Personality = "Kind to {{user}}" }, "Al"));
            Assert.AreEqual("Short.", PromptBuilder.SummaryOf(new CharacterCard { Description = "Short." }, "Al"));

            string sentences = "First one is here. " + new string('x', 400) + ".";
            Assert.AreEqual("First one is here.", PromptBuilder.SummaryOf(new CharacterCard { Description = sentences }, "Al"));
        }

        [Test]
        public void SummaryIsTidiedUp()
        {
            Assert.AreEqual("One line now.", PromptBuilder.CleanSummary("  One\n line   now.  "));
            Assert.IsNull(PromptBuilder.CleanSummary("   "));
            Assert.IsNull(PromptBuilder.CleanSummary(null));
            Assert.LessOrEqual(PromptBuilder.CleanSummary(new string('x', 2000)).Length, PromptBuilder.MaxSummaryLength + 3);
        }

        [TestCase("Bob: Hello", "Hello")]
        [TestCase("bob : Hello", "Hello")]
        [TestCase("Hello\nMina: Hi Bob\nBob: Hi", "Hello")]
        [TestCase("Hello\n  al: Hi", "Hello")]
        [TestCase("Hello Mina: nice to meet you", "Hello Mina: nice to meet you")]
        [TestCase("Mina: Hi", "")]
        public void GroupRepliesAreOnlyTheSpeakersOwnWords(string text, string expected)
        {
            Assert.AreEqual(expected, PromptBuilder.CleanGroupReply(text, "Bob", new[] { "Mina", "Al" }));
        }

        [Test]
        public void TheGroupIsDescribedToTheSpeaker()
        {
            var group = new GroupPrompt
            {
                Name = "Book Club",
                Members = new List<string> { "Bob", "Mina" },
                Others = new List<GroupPeer> { new GroupPeer { Name = "Mina", Mood = "Painting", Summary = "A cheerful barista." } }
            };

            string system = PromptBuilder.Build(new CharacterCard { Name = "Bob" }, new UserProfile { Name = "Al" },
                new ChatTurn[0], 0, group: group)[0].Content;

            StringAssert.Contains("group chat called \"Book Club\"", system);
            StringAssert.Contains("- Mina (mood: \"Painting\"): A cheerful barista.", system);
        }

        [Test]
        public void OptionalContextIsTheFirstThingToGo()
        {
            var card = new CharacterCard { Name = "Bob" };
            var history = new[] { new ChatTurn(false, new string('h', 300)) };
            string first = new string('a', 300), second = new string('b', 30000);

            string roomy = PromptBuilder.Build(card, null, history, 100000, optionalContext: new[] { first, second })[0].Content;
            StringAssert.Contains(first, roomy);
            StringAssert.Contains(second, roomy);

            List<LlmChatMessage> tight = PromptBuilder.Build(card, null, history, 400, optionalContext: new[] { first, second });
            StringAssert.Contains(first, tight[0].Content, "the earlier sections go in first");
            StringAssert.DoesNotContain(second, tight[0].Content);
            Assert.AreEqual(2, tight.Count, "the history is untouched");

            List<LlmChatMessage> tighter = PromptBuilder.Build(card, null, history, 150, optionalContext: new[] { first });
            StringAssert.DoesNotContain(first, tighter[0].Content);
            Assert.AreEqual(2, tighter.Count);
        }

        [Test]
        public void MacrosKnowTheGroup()
        {
            var group = new MacroContext { CharName = "Bob", UserName = "Al", GroupMembers = new[] { "Bob", "Mina" } };
            Assert.AreEqual("Bob, Mina", Macros.Expand("{{group}}", group));
            Assert.AreEqual("Bob, Mina", Macros.Expand("{{groupNotMuted}}", group));
            Assert.AreEqual("Bob, Mina", Macros.Expand("<GROUP>", group));
            Assert.AreEqual("", Macros.Expand("{{charIfNotGroup}}", group));
            Assert.AreEqual("", Macros.Expand("<CHARIFNOTGROUP>", group));
            Assert.AreEqual("Mina, Al", Macros.Expand("{{notChar}}", group));

            var solo = new MacroContext { CharName = "Bob", UserName = "Al" };
            Assert.AreEqual("Bob", Macros.Expand("{{group}}", solo));
            Assert.AreEqual("Bob", Macros.Expand("{{charIfNotGroup}}", solo));
            Assert.AreEqual("Al", Macros.Expand("{{notChar}}", solo));
        }
    }

    [TestFixture]
    public class GroupChatTests
    {
        private static readonly JArray Schema = JArray.Parse(
            "[{\"type\":\"function\",\"function\":{\"name\":\"look_up\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}]");

        private sealed class FakeTools : ICharacterTools
        {
            private readonly Harness _harness;
            private readonly string _owner;

            public FakeTools(Harness harness, string owner)
            {
                _harness = harness;
                _owner = owner;
            }

            public JArray Schema { get { return GroupChatTests.Schema; } }

            public List<string> ContextInjections(CancellationToken cancel)
            {
                return new List<string> { "Memories of " + _owner + "." };
            }

            public ToolOutput Run(LlmToolCall call, CancellationToken cancel)
            {
                lock (_harness.ToolsRanBy)
                    _harness.ToolsRanBy.Add(_owner);
                return new ToolOutput("tool result");
            }
        }

        private sealed class Harness : IDisposable
        {
            private readonly List<MessageEventArgs> _events = new List<MessageEventArgs>();
            private readonly List<string> _dialogs = new List<string>();
            private readonly Dictionary<string, string> _ids = new Dictionary<string, string>();
            private readonly string _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            private readonly CharacterStore _store;
            private readonly GroupStore _groups;
            private int _number;

            public readonly User Me = new User("Al", "Al", "Al");
            public readonly FakeOpenAiServer Server = new FakeOpenAiServer();
            public readonly ObservableCollection<User> Visible = new ObservableCollection<User>();
            public readonly TypingTracker Typing;
            public readonly CharacterChatService Chat;
            public readonly List<string> Bodies = new List<string>();
            public readonly List<string> ToolsRanBy = new List<string>();
            public readonly Dictionary<string, User> Users = new Dictionary<string, User>();
            public Func<string, int, KeyValuePair<int, string>> Script;
            public Group Conversation;
            public StoredGroup Stored;

            public Harness(Func<string, int, KeyValuePair<int, string>> script, bool tools = false, params CharacterCard[] cards)
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

                Typing = new TypingTracker(Visible);
                Chat = new CharacterChatService(() => Me, Record,
                    dialog => { lock (_dialogs) _dialogs.Add(dialog.Message); }, Typing);
                Chat.Hooks.Configured = () => true;
                Chat.Hooks.Endpoint = () => new LlmEndpoint(Server.BaseUrl, "", "");
                Chat.Hooks.StreamReplies = () => false;
                Chat.Hooks.MaxToolRounds = () => 8;
                Chat.Hooks.Tools = character => tools ? new FakeTools(this, character.Card.Name) : null;
                Chat.Hooks.DirectQuestions = () => false;

                _store = new CharacterStore(Path.Combine(_folder, "Characters"));
                _groups = new GroupStore(Path.Combine(_folder, "Groups"));
                foreach (CharacterCard card in cards.Length > 0 ? cards : new[] { Person("Bob"), Person("Mina") })
                {
                    StoredCharacter character = _store.Add(card, null);
                    DirectMessage contact = Chat.Register(character);
                    _ids[card.Name] = character.Id;
                    Users[card.Name] = contact.Partner;
                }
                Stored = _groups.Add(new StoredGroup
                {
                    Name = "Book Club",
                    MemberIds = _ids.Values.ToList(),
                    DelayMinSeconds = 0,
                    DelayMaxSeconds = 0,
                });
                Conversation = Chat.RegisterGroup(Stored);
            }

            public string Id(string name)
            {
                return _ids[name];
            }

            public string Group
            {
                get { return Conversation.Identifier; }
            }

            private void Record(MessageEventArgs e)
            {
                lock (_events)
                    _events.Add(e);
            }

            /// <summary>"Bob: Hello" for each message from a character, oldest first.</summary>
            public List<string> Said()
            {
                lock (_events)
                {
                    var said = new List<string>();
                    foreach (var received in _events.OfType<MessageReceivedEventArgs>())
                    {
                        var message = received.Item as Message;
                        if (message != null && message.Author != Me
                            && !_events.OfType<MessageDeletedEventArgs>().Any(d => d.DeletedItemId == message.Identifier))
                            said.Add(message.Author.DisplayName + ": " + message.Text);
                    }
                    return said;
                }
            }

            public List<GroupNotice> Notices()
            {
                lock (_events)
                    return _events.OfType<MessageReceivedEventArgs>().Select(e => e.Item).OfType<GroupNotice>().ToList();
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

        private static CharacterCard Person(string name)
        {
            return new CharacterCard { Name = name, Description = "I am " + name + ".", GroupSummary = "" };
        }

        private static KeyValuePair<int, string> Text(string text)
        {
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" +
                new JValue(text).ToString(Newtonsoft.Json.Formatting.None) + "},\"finish_reason\":\"stop\"}]}");
        }

        private static KeyValuePair<int, string> Tool()
        {
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\"," +
                "\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"look_up\",\"arguments\":\"{}\"}}]}," +
                "\"finish_reason\":\"tool_calls\"}]}");
        }

        private static string Speaker(string body)
        {
            return body.Contains("You are Bob.") ? "Bob" : body.Contains("You are Mina.") ? "Mina" : "Cass";
        }

        private static DateTime Later
        {
            get { return DateTime.UtcNow.AddDays(1); }
        }

        [Test]
        public void SomeoneNamedReplies_AsThemselves()
        {
            using (var h = new Harness((body, n) => Text("Hello, I'm " + Speaker(body))))
            {
                h.Chat.SendMessage(h.Group, "Hi Mina", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Mina: Hello, I'm Mina" }, h.Said());
                Assert.AreEqual(1, h.Requests);
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void EveryoneNamedReplies_InTheOrderNamed()
        {
            using (var h = new Harness((body, n) => Text("Hello from " + Speaker(body).ToLowerInvariant() + "side")))
            {
                h.Chat.SendMessage(h.Group, "Mina and Bob, hi", false);
                h.Settle(() => h.Said().Count > 1);

                CollectionAssert.AreEqual(new[] { "Mina: Hello from minaside", "Bob: Hello from bobside" }, h.Said());
            }
        }

        [Test]
        public void WithNoNameOneOrTwoReply()
        {
            var counts = new HashSet<int>();
            for (int i = 0; i < 12 && counts.Count < 2; i++)
            {
                using (var h = new Harness((body, n) => Text("Hello.")))
                {
                    h.Chat.SendMessage(h.Group, "Morning all", false);
                    h.Settle(() => h.Said().Count > 0);
                    counts.Add(h.Said().Count);
                }
            }
            CollectionAssert.IsSubsetOf(counts, new[] { 1, 2 });
        }

        [Test]
        public void TheSpeakerSeesOthersLabelled_AndTheirOwnWordsUnlabelled()
        {
            using (var h = new Harness((body, n) => Text(n == 0 ? "Hello Al." : "Nice to meet you.")))
            {
                h.Chat.SendMessage(h.Group, "Hi Bob", false);
                h.Settle(() => h.Said().Count > 0);
                h.Chat.SendMessage(h.Group, "Hi Mina", false);
                h.Settle(() => h.Said().Count > 1);

                JArray messages = (JArray)JObject.Parse(h.Body(1))["messages"];
                StringAssert.Contains("You are Mina.", (string)messages[0]["content"]);
                StringAssert.Contains("group chat called \"Book Club\"", (string)messages[0]["content"]);
                StringAssert.Contains("- Bob: I am Bob.", (string)messages[0]["content"]);
                var last = messages.Last;
                Assert.AreEqual("user", (string)last["role"]);
                StringAssert.Contains("Al: Hi Bob\nBob: Hello Al.\nAl: Hi Mina", (string)last["content"]);

                h.Script = (body, n) => Text("Yes?");
                h.Chat.SendMessage(h.Group, "And you Bob", false);
                h.Settle(() => h.Said().Count > 2);
                JArray third = (JArray)JObject.Parse(h.Body(2))["messages"];
                Assert.IsTrue(third.Any(m => (string)m["role"] == "assistant" && (string)m["content"] == "Hello Al."), "their own message is theirs");
            }
        }

        [Test]
        public void OthersMoodsAreInTheContext()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                h.Users["Mina"].Status = "Painting today";
                h.Chat.SendMessage(h.Group, "Hi Bob", false);
                h.Settle(() => h.Said().Count > 0);

                StringAssert.Contains("Mina (mood: \\\"Painting today\\\")", h.Body(0));
            }
        }

        [Test]
        public void WritingForSomeoneElseIsCut()
        {
            using (var h = new Harness((body, n) => Text("Sure.\nMina: Me too!\nAl: Great")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Bob: Sure." }, h.Said());
            }
        }

        [Test]
        public void EachMemberUsesTheirOwnToolsAndMemories()
        {
            using (var h = new Harness((body, n) => n == 0 ? Tool() : Text("Done."), true))
            {
                h.Chat.SendMessage(h.Group, "Mina, look it up", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Mina" }, h.ToolsRanBy);
                StringAssert.Contains("Memories of Mina.", h.Body(0));
                StringAssert.DoesNotContain("Memories of Bob.", h.Body(0));
            }
        }

        [Test]
        public void GroupAndPrivateHistoriesStaySeparate()
        {
            using (var h = new Harness((body, n) => Text("Noted.")))
            {
                h.Chat.SendMessage(h.Id("Bob"), "My secret is pineapple", false);
                h.Settle(() => h.Said().Count > 0);

                h.Chat.SendMessage(h.Group, "Bob, hi", false);
                h.Settle(() => h.Said().Count > 1);

                StringAssert.DoesNotContain("pineapple", h.Body(1));
            }
        }

        [Test]
        public void ANamedCharacterRepliesToACharacter()
        {
            using (var h = new Harness((body, n) => Text(Speaker(body) == "Bob" ? "Ask Mina." : "Sure.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 1);

                CollectionAssert.AreEqual(new[] { "Bob: Ask Mina.", "Mina: Sure." }, h.Said());
            }
        }

        [Test]
        public void TheDelayAppliesToChainedReplies()
        {
            using (var h = new Harness((body, n) => Text(Speaker(body) == "Bob" ? "Ask Mina." : "Sure.")))
            {
                h.Stored.DelayMinSeconds = 1;
                h.Stored.DelayMaxSeconds = 1;
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 0);
                Assert.AreEqual(1, h.Said().Count, "still waiting");

                h.Settle(() => h.Said().Count > 1);
            }
        }

        [Test]
        public void ChainsAreUnlimitedByDefault_UntilTheUserWrites()
        {
            using (var h = new Harness((body, n) => Text(Speaker(body) == "Bob" ? "Over to you, Mina." : "Over to you, Bob.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count >= 12);

                h.Script = (body, n) => Text("Yes.");
                h.Chat.SendMessage(h.Group, "Stop, both of you. Bob?", false);
                h.Settle(() => h.Said().Last() == "Bob: Yes.");
                int count = h.Said().Count;
                Thread.Sleep(300);
                Assert.AreEqual(count, h.Said().Count, "the old chain is gone");
            }
        }

        [Test]
        public void TheChainLimitStopsAChain()
        {
            using (var h = new Harness((body, n) => Text(Speaker(body) == "Bob" ? "Over to you, Mina." : "Over to you, Bob.")))
            {
                h.Stored.ChainLimit = 3;
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count >= 4);
                Thread.Sleep(300);

                Assert.AreEqual(4, h.Said().Count, "the first reply and 3 chained");
            }
        }

        [Test]
        public void DoNotDisturbStopsAChain()
        {
            using (var h = new Harness((body, n) => Text(Speaker(body) == "Bob" ? "Over to you, Mina." : "Over to you, Bob.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count >= 2);
                h.Me.ConnectionStatus = PresenceStatus.DoNotDisturb;
                Thread.Sleep(300);
                int count = h.Said().Count;
                Thread.Sleep(300);

                Assert.AreEqual(count, h.Said().Count);
            }
        }

        [Test]
        public void TheUsersMessageInterruptsTheWholeCycle()
        {
            Harness harness = null;
            using (var h = new Harness((body, n) =>
            {
                if (n == 0)
                    harness.Chat.SendMessage(harness.Group, "Actually, Mina?", false);
                return Text(n == 0 ? "Old reply." : "New reply.");
            }))
            {
                harness = h;
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 0);

                CollectionAssert.AreEqual(new[] { "Mina: New reply." }, h.Said());
            }
        }

        [Test]
        public void NothingHappensWithoutAReason()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                h.Chat.CheckUnprompted(DateTime.UtcNow);
                Thread.Sleep(200);

                Assert.AreEqual(0, h.Requests, "it hasn't been quiet long enough");
                CollectionAssert.IsEmpty(h.Chat.FetchMessages(h.Group, Fetch.Newest, null), "groups don't open with a greeting");
            }
        }

        [Test]
        public void WhenQuiet_MembersSpeakUnprompted_ThroughAHiddenNote()
        {
            using (var h = new Harness((body, n) => Text("Anyone there?")))
            {
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Said().Count > 0);

                StringAssert.Contains("own initiative", h.Body(0));
                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void TheLoopStopsAfterItsRepeats_UntilTheUserWrites()
        {
            using (var h = new Harness((body, n) => Text("Hm.")))
            {
                h.Stored.Repeats = 2;
                h.Chat.RegisterGroup(h.Stored);

                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > 0);
                int afterOne = h.Requests;
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > afterOne);
                int afterTwo = h.Requests;
                h.Chat.CheckUnprompted(Later);
                Thread.Sleep(300);
                Assert.AreEqual(afterTwo, h.Requests, "no third round");

                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Requests > afterTwo);
                int afterUser = h.Requests;
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > afterUser);
            }
        }

        [Test]
        public void UnlimitedRepeatsKeepGoing()
        {
            using (var h = new Harness((body, n) => Text("Hm.")))
            {
                h.Stored.Repeats = 0;
                h.Chat.RegisterGroup(h.Stored);
                for (int i = 0; i < 6; i++)
                {
                    int before = h.Requests;
                    h.Chat.CheckUnprompted(Later);
                    h.Settle(() => h.Requests > before);
                }
            }
        }

        [Test]
        public void TheLoopCanBeSwitchedOff()
        {
            using (var h = new Harness((body, n) => Text("Hm.")))
            {
                h.Stored.Unprompted = false;
                h.Chat.RegisterGroup(h.Stored);

                h.Chat.CheckUnprompted(Later);
                Thread.Sleep(200);

                Assert.AreEqual(0, h.Requests);
            }
        }

        [Test]
        public void DoNotDisturbHoldsTheLoop_AndItStartsAfterwards()
        {
            using (var h = new Harness((body, n) => Text("Hm.")))
            {
                h.Me.ConnectionStatus = PresenceStatus.DoNotDisturb;
                h.Chat.CheckUnprompted(Later);
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests);

                h.Me.ConnectionStatus = PresenceStatus.Online;
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > 0);
            }
        }

        [Test]
        public void AwayDoesntPauseTheLoop()
        {
            using (var h = new Harness((body, n) => Text("Hm.")))
            {
                h.Me.ConnectionStatus = PresenceStatus.Away;
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > 0);
            }
        }

        [Test]
        public void UnpromptedErrorsAreSilent()
        {
            using (var h = new Harness((body, n) => FakeOpenAiServer.Reply(401, "{\"error\":{\"message\":\"nope\"}}")))
            {
                h.Chat.CheckUnprompted(Later);
                h.Settle(() => h.Requests > 0);

                CollectionAssert.IsEmpty(h.Dialogs());
            }
        }

        [Test]
        public void ErrorsAnsweringTheUserAreReported()
        {
            using (var h = new Harness((body, n) => FakeOpenAiServer.Reply(401, "{\"error\":{\"message\":\"nope\"}}")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Dialogs().Count > 0);

                StringAssert.Contains("Bob couldn't reply", h.Dialogs()[0]);
            }
        }

        [Test]
        public void NoNoticesWhenAGroupIsCreated()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
                CollectionAssert.IsEmpty(h.Notices());
        }

        [Test]
        public void JoiningAndLeavingGoInTheChatAndTheContext()
        {
            var cards = new[] { Person("Bob"), Person("Mina"), Person("Cass") };
            using (var h = new Harness((body, n) => Text("Hi."), false, cards))
            {
                h.Stored.MemberIds = new List<string> { h.Id("Bob"), h.Id("Mina") };
                h.Chat.RegisterGroup(h.Stored);

                StoredGroup updated = h.Stored.Copy();
                updated.MemberIds = new List<string> { h.Id("Bob"), h.Id("Cass") };
                Group group = h.Chat.UpdateGroup(updated);

                Assert.AreEqual(2, group.Members.Length);
                CollectionAssert.AreEquivalent(new[] { "Mina left the group.", "Cass joined the group." }, h.Notices().Select(n => n.Text));

                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 0);
                StringAssert.Contains("[Mina left the group.]", h.Body(0));
                StringAssert.Contains("[Cass joined the group.]", h.Body(0));
                StringAssert.DoesNotContain("- Mina", h.Body(0));
            }
        }

        [Test]
        public void SomeoneWhoLeftIsNotAsked()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                StoredGroup updated = h.Stored.Copy();
                updated.MemberIds.Remove(h.Id("Mina"));
                h.Chat.UpdateGroup(updated);

                h.Chat.SendMessage(h.Group, "Mina?", false);
                h.Settle(() => h.Said().Count > 0);

                Assert.IsTrue(h.Said().All(s => s.StartsWith("Bob:")));
            }
        }

        [Test]
        public void RemovingACharacterTakesThemOutOfTheirGroups()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                h.Chat.Remove(h.Id("Mina"));

                Assert.AreEqual("Mina left the group.", h.Notices().Single().Text);
                CollectionAssert.AreEqual(new[] { h.Id("Bob") }, h.Chat.GetGroup(h.Group).MemberIds);

                h.Chat.SendMessage(h.Group, "Anyone?", false);
                h.Settle(() => h.Said().Count > 0);
                StringAssert.Contains("[Mina left the group.]", h.Body(0));
            }
        }

        [Test]
        public void RegenerateRewritesOnlyTheLatestSpeakersTurn()
        {
            using (var h = new Harness((body, n) => Text(n == 0 ? "First." : "Rewritten.")))
            {
                h.Chat.SendMessage(h.Group, "Mina?", false);
                h.Settle(() => h.Said().Count > 0);
                string id = h.Chat.FetchMessages(h.Group, Fetch.Newest, null).OfType<Message>().Last().Identifier;
                Assert.IsTrue(h.Chat.IsLatestReply(h.Group, id));

                Assert.IsTrue(h.Chat.Regenerate(h.Group));
                h.Settle(() => h.Said().Count > 0 && h.Said().Last() == "Mina: Rewritten.");

                CollectionAssert.AreEqual(new[] { "Mina: Rewritten." }, h.Said());
            }
        }

        [Test]
        public void ClearingEndsTheCycleAndForgetsVariables()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                h.Chat.SendMessage(h.Group, "Bob?", false);
                h.Settle(() => h.Said().Count > 0);

                h.Chat.Clear(h.Group);

                CollectionAssert.IsEmpty(h.Chat.FetchMessages(h.Group, Fetch.Newest, null));
            }
        }

        [Test]
        public void RemovingAGroupStopsIt()
        {
            using (var h = new Harness((body, n) => Text("Hi.")))
            {
                h.Chat.RemoveGroup(h.Group);

                Assert.IsNull(h.Chat.GetGroup(h.Group));
                Assert.IsFalse(h.Chat.Handles(h.Group));
                h.Chat.CheckUnprompted(Later);
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests);
            }
        }

        [Test]
        public void SummariesAreWrittenAndSavedForEveryoneMissingOne()
        {
            using (var h = new Harness((body, n) => Text("Written for " + (body.Contains("Name: Bob") ? "Bob" : "Mina") + ".")))
            {
                h.Chat.RefreshSummaries();
                h.Settle(() => !h.Chat.GetCharacter(h.Id("Bob")).Card.NeedsGroupSummary
                            && !h.Chat.GetCharacter(h.Id("Mina")).Card.NeedsGroupSummary);

                StoredCharacter bob = h.Chat.GetCharacter(h.Id("Bob"));
                Assert.AreEqual("Written for Bob.", bob.Card.GroupSummary);
                Assert.AreEqual(2, h.Requests);
                string saved = File.ReadAllText(Path.Combine(bob.Folder, "card.json"));
                StringAssert.Contains("Written for Bob.", saved);

                h.Chat.RefreshSummaries();
                Thread.Sleep(200);
                Assert.AreEqual(2, h.Requests, "nothing more to do");
            }
        }

        [Test]
        public void ChangingACharacterRewritesTheirSummary_EvenAnEditedOne()
        {
            using (var h = new Harness((body, n) => Text("Fresh summary.")))
            {
                CharacterCard card = h.Chat.GetCharacter(h.Id("Bob")).Card;
                h.Chat.GetCharacter(h.Id("Mina")).Card.GroupSummary = "Done.";
                h.Chat.GetCharacter(h.Id("Mina")).Card.GroupSummarySource = h.Chat.GetCharacter(h.Id("Mina")).Card.GroupSummaryFingerprint;
                card.GroupSummary = "Edited by hand.";
                card.GroupSummarySource = card.GroupSummaryFingerprint;

                h.Chat.RefreshSummaries();
                Thread.Sleep(200);
                Assert.AreEqual(0, h.Requests, "an edited summary stays");

                card.Personality = "Grumpy";
                h.Chat.RefreshSummaries();
                h.Settle(() => card.GroupSummary == "Fresh summary.");
            }
        }

        [Test]
        public void AFailedSummaryWaitsAndTheCardStandsIn()
        {
            using (var h = new Harness((body, n) => FakeOpenAiServer.Reply(500, "{}")))
            {
                h.Chat.RefreshSummaries();
                h.Settle(() => h.Requests >= 2);
                Thread.Sleep(200);
                int requests = h.Requests;

                h.Chat.RefreshSummaries();
                Thread.Sleep(200);

                Assert.AreEqual(requests, h.Requests, "not again straight away");
                Assert.AreEqual("I am Bob.", PromptBuilder.SummaryOf(h.Chat.GetCharacter(h.Id("Bob")).Card, "Al"));
            }
        }

        private sealed class InlineContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
                d(state);
            }
        }

        [Test]
        public void TypingShowsOnlyInTheChatTheCharacterIsWritingIn()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new InlineContext());
            try
            {
                using (var asked = new ManualResetEvent(false))
                using (var answer = new ManualResetEvent(false))
                using (var h = new Harness((body, n) =>
                {
                    asked.Set();
                    answer.WaitOne(10000);
                    return Text("Hello");
                }))
                {
                    h.Typing.Open(h.Group);
                    h.Chat.SendMessage(h.Id("Bob"), "Hi", false);
                    Assert.IsTrue(asked.WaitOne(10000));
                    CollectionAssert.IsEmpty(h.Visible, "Bob is writing in private, not in the group");

                    h.Typing.Open(h.Id("Bob"));
                    CollectionAssert.AreEqual(new[] { h.Users["Bob"] }, h.Visible);

                    answer.Set();
                    h.Settle(() => h.Said().Count > 0);
                    CollectionAssert.IsEmpty(h.Visible);
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void MoodsSeeTheirGroups_ReplyContextDoesnt()
        {
            var bob = Person("Bob");
            bob.UpdateMood = true;
            using (var h = new Harness((body, n) => Text("Feeling good"), false, bob, Person("Mina")))
            {
                h.Chat.SendMessage(h.Group, "Bob, the club meets Friday", false);
                h.Settle(() => h.Said().Count > 0);
                int requests = h.Requests;

                h.Chat.EnsureMood(h.Id("Bob"));
                h.Settle(() => h.Users["Bob"].Status == "Feeling good");

                StringAssert.Contains("Recent messages in the group chat \\\"Book Club\\\"", h.Body(requests));
                StringAssert.Contains("the club meets Friday", h.Body(requests));
                StringAssert.DoesNotContain("the club meets Friday", ReplyInPrivate(h));
            }
        }

        private static string ReplyInPrivate(Harness h)
        {
            int before = h.Requests;
            h.Chat.SendMessage(h.Id("Bob"), "Hello in private", false);
            h.Settle(() => h.Requests > before);
            return h.Body(before);
        }
    }
}
