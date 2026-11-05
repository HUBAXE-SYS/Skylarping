/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for SimpleLLMChat-compatible tools: card storage, manifests, running SimpleLLMChat's
// MemoryTools.exe, and tool calls in the chat client. Tools aren't part of the repo: tests
// that need them run when deps\tools\MemoryTools (and WebTools) exist, and are skipped otherwise.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.AI.Tools;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class ToolTests
    {
        private static string ToolsDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools"); }
        }

        private static ToolPackage Package(string key)
        {
            ToolPackage package = ToolRegistry.Scan(ToolsDir).FirstOrDefault(p => p.Key == key);
            if (package == null)
                Assert.Ignore(key + " isn't in deps\\tools (tools are optional)");
            return package;
        }

        [Test]
        public void CardKeepsTools()
        {
            var card = new CharacterCard { Name = "Sera", Description = "x" };
            card.Tools = new List<string> { "save_memory", "run_web_search" };
            CharacterCard again = CharacterCard.FromJson(card.ToJson());
            CollectionAssert.AreEqual(new[] { "save_memory", "run_web_search" }, again.Tools);
        }

        [Test]
        public void IniIsParsedLikeSimpleLlmChat()
        {
            Dictionary<string, string> ini = ToolRegistry.ParseIni(
                "﻿# comment\r\n" +
                "; also a comment\n" +
                "[Tools]\n" +
                "\n" +
                "  memoriesDirectory = {character_folder}\\memories  \r\n" +
                "url=http://x/?a=b&c=d\n" +
                "no equals sign\n" +
                "=no key\n" +
                "empty=\n" +
                "MaxMemories=10\n" +
                "maxmemories=20\n");

            Assert.AreEqual(@"{character_folder}\memories", ini["MEMORIESDIRECTORY"], "trimmed, backslashes literal, keys case-insensitive");
            Assert.AreEqual("http://x/?a=b&c=d", ini["url"], "split at the first '=' only");
            Assert.AreEqual("", ini["empty"]);
            Assert.AreEqual("20", ini["maxMemories"], "the last value wins");
            CollectionAssert.AreEquivalent(new[] { "memoriesDirectory", "url", "empty", "MaxMemories" }, ini.Keys.ToList());
        }

        [Test]
        public void ShippedDefaultsKeepMemoriesPerCharacter()
        {
            Assert.AreEqual(@"{character_folder}\memories", ToolRegistry.ShippedDefaults["memoriesDirectory"],
                "ToolDefaults.ini is copied next to the exe");
            Assert.AreEqual(0, ToolRegistry.LoadIni(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini")).Count, "a missing file is fine");
        }

        [Test]
        public void ToolNamesAreMadeReadable()
        {
            Assert.AreEqual("Run Web Search", ToolRegistry.DisplayName("run_web_search"));
            Assert.AreEqual("Save Memory", ToolRegistry.DisplayName("save_memory"));
            Assert.AreEqual("Read URL", ToolRegistry.DisplayName("read_URL"), "only the first letter is changed");
            Assert.AreEqual("Screenshot", ToolRegistry.DisplayName("screenshot"));
            Assert.AreEqual("A B", ToolRegistry.DisplayName("_a__b_"), "stray underscores don't make empty words");
            Assert.AreEqual("", ToolRegistry.DisplayName(""));
        }

        [Test]
        public void CharacterFolderPlaceholderIsFilledIn()
        {
            Assert.AreEqual(@"C:\P\Characters\char_a\memories",
                ToolRegistry.ExpandHostPlaceholders(@"{character_folder}\memories", @"C:\P\Characters\char_a\"));
            Assert.AreEqual(@"C:\P\x", ToolRegistry.ExpandHostPlaceholders(@"{CHARACTER_FOLDER}\x", @"C:\P"), "case-insensitive");
            Assert.AreEqual("plain", ToolRegistry.ExpandHostPlaceholders("plain", @"C:\P"));
        }

        [Test]
        public void CardWithoutToolsHasNone()
        {
            CharacterCard card = CharacterCard.FromJson("{\"spec\":\"chara_card_v2\",\"data\":{\"name\":\"A\",\"description\":\"b\"}}");
            Assert.AreEqual(0, card.Tools.Count);
        }

        [Test]
        public void SimpleLlmChatManifestsAreRead()
        {
            ToolPackage memory = Package("MemoryTools");
            ToolPackage web = Package("WebTools");
            Assert.AreEqual("get_memory_context", memory.ContextInjector);
            CollectionAssert.IsSubsetOf(new[] { "save_memory", "recall_memory", "list_memories" }, memory.Functions.Select(f => f.Name).ToList());
            CollectionAssert.IsSubsetOf(new[] { "run_web_search", "read_website" }, web.Functions.Select(f => f.Name).ToList());
        }

        [Test]
        public void SchemaFillsConfigPlaceholders()
        {
            ToolPackage memory = Package("MemoryTools");
            var config = ToolRegistry.Config(memory, new Dictionary<string, string> { { "maxContentLength", "123" } }, false);
            JArray schema = ToolRegistry.BuildToolsArray(memory.Functions.Where(f => f.Name == "save_memory"), p => config);

            JToken function = schema.Single()["function"];
            Assert.AreEqual("save_memory", (string)function["name"]);
            StringAssert.Contains("123 characters", (string)function["parameters"]["properties"]["content"]["description"]);
            CollectionAssert.AreEquivalent(new[] { "name", "content" }, ((JArray)function["parameters"]["required"]).Select(t => (string)t).ToList());
        }

        [Test]
        public void MemoryToolsSaveAndInjectPerFolder()
        {
            ToolPackage memory = Package("MemoryTools");
            string dir = Path.Combine(Path.GetTempPath(), "skylarping-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var config = ToolRegistry.Config(memory, new Dictionary<string, string> { { "memoriesDirectory", dir } }, false);
                ToolFunction save = memory.Functions.Single(f => f.Name == "save_memory");

                string output = ToolRegistry.Run(save, "{\"name\":\"favourite_food\",\"content\":\"Alex loves ramen.\"}", config, 30, CancellationToken.None).Text;
                StringAssert.StartsWith("Command: save_memory\nExit Code: 0\nOutput:\n", output);
                Assert.IsTrue(File.Exists(Path.Combine(dir, "favourite_food.md")), "saved in the character's own folder");

                string context = ToolRegistry.RunContextInjector(memory, config, CancellationToken.None);
                StringAssert.Contains("favourite_food: Alex loves ramen.", context);

                // Another folder (another character) sees nothing.
                var other = ToolRegistry.Config(memory, new Dictionary<string, string> { { "memoriesDirectory", dir + "-other" } }, false);
                Assert.IsNull(ToolRegistry.RunContextInjector(memory, other, CancellationToken.None));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Test]
        public void BadArgumentsAreReportedToTheModel()
        {
            ToolPackage memory = Package("MemoryTools");
            string output = ToolRegistry.Run(memory.Functions.First(), "{not json", ToolRegistry.Config(memory, null, false), 30, CancellationToken.None).Text;
            StringAssert.Contains("Exit Code: 1", output);
            StringAssert.Contains("aren't valid JSON", output);
        }

        [Test]
        public void ToolOutputEnvelopeIsParsed()
        {
            Assert.AreEqual("hi", ToolRegistry.ParseOutput("{\"text\":\"hi\"}", null));
            Assert.AreEqual("plain", ToolRegistry.ParseOutput("plain", null));
            Assert.AreEqual("hi\noops", ToolRegistry.ParseOutput("{\"text\":\"hi\"}", "oops"));

            string image, mime;
            Assert.AreEqual("(no text)", ToolRegistry.ParseOutput("{\"image\":{\"mime\":\"image/jpeg\",\"data\":\"QUJD\"}}", null, out image, out mime));
            Assert.AreEqual("QUJD", image);
            Assert.AreEqual("image/jpeg", mime);
        }

        [Test]
        public void NoToolsInstalledMeansNoPackages()
        {
            string missing = Path.Combine(Path.GetTempPath(), "skylarping-notools-" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual(0, ToolRegistry.Scan(missing).Count, "a missing tools folder is fine");
        }

        [Test]
        public void ToolContextGoesBeforePostHistory()
        {
            var card = new CharacterCard { Name = "Sera", Description = "desc", PostHistoryInstructions = "Stay gentle." };
            string system = PromptBuilder.Build(card, new UserProfile { Name = "Al" }, new ChatTurn[0], 0,
                new[] { "Current Memories:\nfood: {{user}} likes ramen" })[0].Content;
            int memories = system.IndexOf("Current Memories:", StringComparison.Ordinal);
            Assert.Greater(memories, system.IndexOf("desc", StringComparison.Ordinal));
            Assert.Less(memories, system.IndexOf("Stay gentle.", StringComparison.Ordinal));
            StringAssert.Contains("{{user}} likes ramen", system, "tool text is passed on as is");
        }
    }

    [TestFixture]
    public class ToolCallClientTests
    {
        private static readonly JArray Tools = JArray.Parse(
            "[{\"type\":\"function\",\"function\":{\"name\":\"save_memory\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}]");

        private static KeyValuePair<int, string> TextReply(string text)
        {
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + text + "\"},\"finish_reason\":\"stop\"}]}");
        }

        private static KeyValuePair<int, string> ToolReply(string text)
        {
            return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + text + "\"," +
                "\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"save_memory\",\"arguments\":\"{}\"}}]}," +
                "\"finish_reason\":\"tool_calls\"}]}");
        }

        private static string AskUsingTools(FakeOpenAiServer server, int maxRounds, List<string> ran)
        {
            return Skylarping.Backend.CharacterChatService.AskUsingTools(new LlmEndpoint(server.BaseUrl, "", ""),
                new List<LlmChatMessage> { LlmChatMessage.FromUser("write a mood") }, Tools,
                call => { ran.Add(call.Name); return new ToolOutput("tool result"); }, maxRounds);
        }

        [Test]
        public void AskUsingToolsRunsThemAndKeepsOnlyTheLastText()
        {
            using (var server = new FakeOpenAiServer())
            {
                var bodies = new List<string>();
                server.Handler = (m, p, b, a) =>
                {
                    bodies.Add(b);
                    return bodies.Count == 1 ? ToolReply("Let me check.") : TextReply("Feeling good");
                };
                var ran = new List<string>();

                Assert.AreEqual("Feeling good", AskUsingTools(server, 8, ran));

                CollectionAssert.AreEqual(new[] { "save_memory" }, ran);
                Assert.AreEqual(2, bodies.Count);
                StringAssert.Contains("tool result", bodies[1]);
            }
        }

        [Test]
        public void AskUsingToolsKeepsEarlierTextIfTheLastRoundHasNone()
        {
            using (var server = new FakeOpenAiServer())
            {
                int calls = 0;
                server.Handler = (m, p, b, a) => ++calls == 1 ? ToolReply("Feeling good") : TextReply("");

                Assert.AreEqual("Feeling good", AskUsingTools(server, 8, new List<string>()));
            }
        }

        [Test]
        public void AskUsingToolsDropsThemAfterTheLastRound()
        {
            using (var server = new FakeOpenAiServer())
            {
                var bodies = new List<string>();
                server.Handler = (m, p, b, a) =>
                {
                    bodies.Add(b);
                    return bodies.Count == 1 ? ToolReply("") : TextReply("Done");
                };

                Assert.AreEqual("Done", AskUsingTools(server, 1, new List<string>()));
                StringAssert.Contains("\"tools\":[", bodies[0]);
                StringAssert.DoesNotContain("\"tools\":[", bodies[1]);
            }
        }

        [Test]
        public void AskUsingToolsAnswersWithoutThemIfTheServerRejectsThem()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => b.Contains("\"tools\":[")
                    ? FakeOpenAiServer.Reply(500, "{\"error\":{\"message\":\"no tools here\"}}")
                    : TextReply("Done");

                Assert.AreEqual("Done", AskUsingTools(server, 8, new List<string>()));
            }
        }

        [Test]
        public void ChatReturnsToolCallsAndSendsThemBack()
        {
            using (var server = new FakeOpenAiServer())
            {
                string body = null;
                server.Handler = (m, p, b, a) =>
                {
                    body = b;
                    return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null," +
                        "\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"save_memory\",\"arguments\":\"{\\\"name\\\":\\\"x\\\"}\"}}]}," +
                        "\"finish_reason\":\"tool_calls\"}]}");
                };
                var messages = new List<LlmChatMessage> { LlmChatMessage.FromUser("remember x") };
                LlmChatResult result = OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", ""), messages, tools: Tools);

                StringAssert.Contains("\"tools\":[", body);
                Assert.AreEqual(1, result.ToolCalls.Count);
                Assert.AreEqual("c1", result.ToolCalls[0].Id);
                Assert.AreEqual("save_memory", result.ToolCalls[0].Name);
                Assert.AreEqual("{\"name\":\"x\"}", result.ToolCalls[0].Arguments);
                Assert.AreEqual("", result.Content);

                messages.Add(LlmChatMessage.FromAssistantToolCalls(result.Content, result.ToolCalls));
                messages.Add(LlmChatMessage.FromTool("c1", "saved"));
                OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", ""), messages, tools: Tools);
                JArray sent = (JArray)JObject.Parse(body)["messages"];
                Assert.AreEqual("c1", (string)sent[1]["tool_calls"][0]["id"]);
                Assert.AreEqual("save_memory", (string)sent[1]["tool_calls"][0]["function"]["name"]);
                Assert.AreEqual("tool", (string)sent[2]["role"]);
                Assert.AreEqual("c1", (string)sent[2]["tool_call_id"]);
            }
        }

        [Test]
        public void ToolImageIsSentAsImagePart()
        {
            using (var server = new FakeOpenAiServer())
            {
                string body = null;
                server.Handler = (m, p, b, a) =>
                {
                    body = b;
                    return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"content\":\"nice\"}}]}");
                };
                OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", ""), new[]
                {
                    LlmChatMessage.FromAssistantToolCalls("", new[] { new LlmToolCall("c1", "screenshot", "{}") }),
                    LlmChatMessage.FromTool("c1", "Command: screenshot", "QUJD", "image/jpeg"),
                });
                JToken content = JObject.Parse(body)["messages"][1]["content"];
                Assert.AreEqual("Command: screenshot", (string)content[0]["text"]);
                Assert.AreEqual("data:image/jpeg;base64,QUJD", (string)content[1]["image_url"]["url"]);
            }
        }

        [Test]
        public void NoToolsMeansNoToolsField()
        {
            using (var server = new FakeOpenAiServer())
            {
                string body = null;
                server.Handler = (m, p, b, a) =>
                {
                    body = b;
                    return FakeOpenAiServer.Reply(200, "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]}");
                };
                OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", ""), new[] { LlmChatMessage.FromUser("x") });
                StringAssert.DoesNotContain("\"tools\"", body);
            }
        }

        [Test]
        public void StreamedToolCallsAreAssembled()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => new KeyValuePair<int, string>(200, string.Join("", new[]
                {
                    "{\"choices\":[{\"delta\":{\"content\":\"Let me check.\"}}]}",
                    "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"type\":\"function\",\"function\":{\"name\":\"run_web_search\",\"arguments\":\"\"}}]}}]}",
                    "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"query\\\":\"}}]}}]}",
                    "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"cats\\\"}\"}}]}}]}",
                    "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"name\":\"read_website\",\"arguments\":\"{}\"}}]}}]}",
                    "{\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                    "[DONE]",
                }.Select(e => "data: " + e + "\n\n").ToArray()));

                var seen = new List<string>();
                LlmChatResult result = OpenAICompatClient.ChatStream(new LlmEndpoint(server.BaseUrl, "", ""),
                    new[] { LlmChatMessage.FromUser("cats?") }, seen.Add, CancellationToken.None, tools: Tools);

                Assert.AreEqual("Let me check.", result.Content);
                Assert.AreEqual("tool_calls", result.FinishReason);
                Assert.AreEqual(2, result.ToolCalls.Count);
                Assert.AreEqual("call_a", result.ToolCalls[0].Id);
                Assert.AreEqual("run_web_search", result.ToolCalls[0].Name);
                Assert.AreEqual("{\"query\":\"cats\"}", result.ToolCalls[0].Arguments);
                Assert.AreEqual("read_website", result.ToolCalls[1].Name);
                Assert.IsNotEmpty(result.ToolCalls[1].Id, "a missing id is filled in");
            }
        }
    }
}
