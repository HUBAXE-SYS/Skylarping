/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for the character prompt builder and the streaming chat client.
/*==========================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class PromptBuilderTests
    {
        private static CharacterCard Card()
        {
            return new CharacterCard
            {
                Name = "Seraphina",
                Description = "{{char}} guards the forest. She likes <USER>.",
                Personality = "kind",
                Scenario = "{{user}} wakes in a glade.",
                MessageExamples = "<START>\n{{user}}: Hi\n{{char}}: Hello!\n<START>\n{{char}}: *waves*",
                PostHistoryInstructions = "Stay gentle.",
            };
        }

        private static UserProfile Me()
        {
            return new UserProfile { Name = "Alex", AboutMe = "I like hiking.", Presence = "Away", Mood = "tired" };
        }

        [Test]
        public void SystemMessageHasCardAndUser()
        {
            List<LlmChatMessage> messages = PromptBuilder.Build(Card(), Me(), new ChatTurn[0], 0);
            Assert.AreEqual(1, messages.Count);
            string system = messages[0].Content;
            Assert.AreEqual("system", messages[0].Role);
            StringAssert.StartsWith("You are Seraphina.", system);
            StringAssert.Contains("Seraphina guards the forest. She likes Alex.", system);
            StringAssert.Contains("Seraphina's personality: kind", system);
            StringAssert.Contains("I like hiking.", system);
            StringAssert.Contains("Alex's status is Away, with mood message: \"tired\".", system);
            StringAssert.Contains("Stay gentle.", system);
            StringAssert.DoesNotContain("{{", system);
        }

        [Test]
        public void ScenarioOnlyWhenTurnedOn()
        {
            CharacterCard card = Card();
            StringAssert.DoesNotContain("glade", PromptBuilder.Build(card, Me(), new ChatTurn[0], 0)[0].Content);
            card.UseScenario = true;
            StringAssert.Contains("Scenario: Alex wakes in a glade.", PromptBuilder.Build(card, Me(), new ChatTurn[0], 0)[0].Content);
        }

        [Test]
        public void ExamplesAreReferenceTextNotTurns()
        {
            var history = new[] { new ChatTurn(false, "hey") };
            List<LlmChatMessage> messages = PromptBuilder.Build(Card(), Me(), history, 0);
            Assert.AreEqual(2, messages.Count, "only system + the real message");
            string system = messages[0].Content;
            StringAssert.Contains("style reference only", system);
            StringAssert.Contains("Alex: Hi\nSeraphina: Hello!\n\n---\n\nSeraphina: *waves*", system);
            StringAssert.DoesNotContain("<START>", system);
        }

        [Test]
        public void CardSystemPromptReplacesDefaultAndSupportsOriginal()
        {
            CharacterCard card = Card();
            card.SystemPrompt = "Roleplay only. {{original}}";
            string system = PromptBuilder.Build(card, Me(), new ChatTurn[0], 0)[0].Content;
            StringAssert.StartsWith("Roleplay only. You are Seraphina.", system);
        }

        [Test]
        public void HistoryAlternatesAndMergesSameSide()
        {
            var history = new[]
            {
                new ChatTurn(true, "Welcome, {{user}}."),
                new ChatTurn(false, "hi"),
                new ChatTurn(false, "you there?"),
                new ChatTurn(true, "Yes!"),
            };
            List<LlmChatMessage> messages = PromptBuilder.Build(Card(), Me(), history, 0);
            CollectionAssert.AreEqual(new[] { "system", "assistant", "user", "assistant" }, messages.Select(m => m.Role).ToArray());
            Assert.AreEqual("Welcome, Alex.", messages[1].Content);
            Assert.AreEqual("hi\nyou there?", messages[2].Content);
        }

        [Test]
        public void KeepsLast50WithoutContextWindow()
        {
            var history = Enumerable.Range(0, 120).Select(i => new ChatTurn(i % 2 == 1, "m" + i)).ToList();
            List<ChatTurn> kept = PromptBuilder.SelectHistory(history, 0, 0);
            Assert.AreEqual(50, kept.Count);
            Assert.AreEqual("m70", kept[0].Text);
            Assert.AreEqual("m119", kept[49].Text);
        }

        [Test]
        public void FitsContextWindowAtThreeCharsPerToken()
        {
            // 30 chars = 10 tokens + 4 overhead = 14 per turn.
            var history = Enumerable.Range(0, 20).Select(i => new ChatTurn(false, new string('x', 30))).ToList();
            // window 100 -> 90 for the prompt, minus 20 system tokens = 70 -> 5 turns.
            List<ChatTurn> kept = PromptBuilder.SelectHistory(history, 100, 20);
            Assert.AreEqual(5, kept.Count);
            // Always keeps the latest message even if it doesn't fit.
            Assert.AreEqual(1, PromptBuilder.SelectHistory(new[] { new ChatTurn(false, new string('x', 9000)) }, 100, 20).Count);
            Assert.AreEqual(10, PromptBuilder.EstimateTokens(new string('x', 30)));
        }

        [Test]
        public void EmptyFieldsLeaveNoBlankSections()
        {
            var card = new CharacterCard { Name = "Bob" };
            string system = PromptBuilder.Build(card, new UserProfile { Name = "Al" }, new ChatTurn[0], 0)[0].Content;
            Assert.AreEqual(PromptBuilder.Expand(PromptBuilder.DefaultSystemPrompt, "Bob", "Al"), system);
        }
    }

    [TestFixture]
    public class ChatStreamTests
    {
        private static string Sse(params string[] events)
        {
            return string.Join("", events.Select(e => "data: " + e + "\n\n").ToArray());
        }

        [Test]
        public void StreamsVisibleTextAndHidesThinking()
        {
            using (var server = new FakeOpenAiServer())
            {
                string body = null;
                server.Handler = (m, p, b, a) =>
                {
                    body = b;
                    return new KeyValuePair<int, string>(200, Sse(
                        "{\"model\":\"m1\",\"choices\":[{\"delta\":{\"reasoning_content\":\"hmm\"}}]}",
                        "{\"choices\":[{\"delta\":{\"content\":\"<think>x</think>Hel\"}}]}",
                        "not json",
                        "{\"choices\":[{\"delta\":{\"content\":\"lo!\"},\"finish_reason\":\"stop\"}]}",
                        "[DONE]"));
                };
                var seen = new List<string>();
                LlmChatResult result = OpenAICompatClient.ChatStream(new LlmEndpoint(server.BaseUrl, "", "m1"),
                    new[] { LlmChatMessage.FromUser("hi") }, seen.Add, CancellationToken.None);

                StringAssert.Contains("\"stream\":true", body);
                Assert.AreEqual("Hello!", result.Content);
                Assert.AreEqual("hmm", result.Reasoning);
                Assert.AreEqual("stop", result.FinishReason);
                Assert.AreEqual("m1", result.Model);
                CollectionAssert.AreEqual(new[] { "Hel", "Hello!" }, seen);
            }
        }

        [Test]
        public void StreamErrorIsReported()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => new KeyValuePair<int, string>(200, Sse("{\"error\":{\"message\":\"model crashed\"}}"));
                var ex = Assert.Throws<LlmException>(() => OpenAICompatClient.ChatStream(new LlmEndpoint(server.BaseUrl, "", ""),
                    new[] { LlmChatMessage.FromUser("hi") }, null, CancellationToken.None));
                StringAssert.Contains("model crashed", ex.Message);
            }
        }

        [Test]
        public void HttpErrorBeforeStreamIsReported()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => new KeyValuePair<int, string>(401, "{\"error\":\"bad key\"}");
                var ex = Assert.Throws<LlmException>(() => OpenAICompatClient.ChatStream(new LlmEndpoint(server.BaseUrl, "", ""),
                    new[] { LlmChatMessage.FromUser("hi") }, null, CancellationToken.None));
                Assert.AreEqual(LlmErrorKind.Unauthorized, ex.Kind);
            }
        }
    }
}
