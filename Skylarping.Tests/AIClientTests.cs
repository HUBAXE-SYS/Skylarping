/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for the OpenAI-compatible client and the connection test, against a
// fake server on loopback (HttpListener).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Skylarping.AI;

namespace Skylarping.Tests
{
    internal sealed class FakeOpenAiServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Thread _thread;

        /// <summary>(method, path, body, authorization) => (status, json)</summary>
        public Func<string, string, string, string, KeyValuePair<int, string>> Handler { get; set; }
        public List<string> Requests { get; } = new List<string>();
        public string BaseUrl { get; }

        public FakeOpenAiServer()
        {
            var rng = new Random();
            for (int attempt = 0; ; attempt++)
            {
                string url = "http://127.0.0.1:" + rng.Next(49152, 65535);
                var listener = new HttpListener();
                listener.Prefixes.Add(url + "/");
                try
                {
                    listener.Start();
                    _listener = listener;
                    BaseUrl = url + "/v1";
                    break;
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                    if (attempt > 30)
                        throw;
                }
            }
            _thread = new Thread(Serve) { IsBackground = true };
            _thread.Start();
        }

        private void Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }

                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
                string path = ctx.Request.Url.AbsolutePath;
                lock (Requests)
                    Requests.Add(ctx.Request.HttpMethod + " " + path + " " + body);

                KeyValuePair<int, string> reply = Handler(ctx.Request.HttpMethod, path, body, ctx.Request.Headers["Authorization"]);
                byte[] bytes = Encoding.UTF8.GetBytes(reply.Value ?? "");
                ctx.Response.StatusCode = reply.Key;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
        }

        public static KeyValuePair<int, string> Reply(int status, string json) { return new KeyValuePair<int, string>(status, json); }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    [TestFixture]
    public class AIClientTests
    {
        private const string ModelsJson = "{\"object\":\"list\",\"data\":[{\"id\":\"zeta-7b\"},{\"id\":\"alpha-13b\"},{\"id\":\"alpha-13b\"}]}";
        private const string ChatJson = "{\"model\":\"alpha-13b\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}";

        private static KeyValuePair<int, string> HappyServer(string method, string path, string body, string auth)
        {
            if (path.EndsWith("/models"))
                return FakeOpenAiServer.Reply(200, ModelsJson);
            if (path.EndsWith("/chat/completions"))
                return FakeOpenAiServer.Reply(200, ChatJson);
            return FakeOpenAiServer.Reply(404, "{\"error\":{\"message\":\"no route\"}}");
        }

        [Test]
        public void ListModels_ReturnsSortedDistinctIds_AndSendsBearerKey()
        {
            using (var server = new FakeOpenAiServer())
            {
                string seenAuth = null;
                server.Handler = (m, p, b, a) => { seenAuth = a; return HappyServer(m, p, b, a); };

                IList<string> models = OpenAICompatClient.ListModels(new LlmEndpoint(server.BaseUrl + "/", "sk-test", ""));

                CollectionAssert.AreEqual(new[] { "alpha-13b", "zeta-7b" }, models);
                Assert.AreEqual("Bearer sk-test", seenAuth);
            }
        }

        [Test]
        public void Chat_SendsModelAndMessages_AndSplitsThinkTags()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => FakeOpenAiServer.Reply(200,
                    "{\"choices\":[{\"message\":{\"content\":\"<think>pondering</think>\\n\\nHello!\"},\"finish_reason\":\"stop\"}]}");

                LlmChatResult result = OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", "alpha-13b"),
                    new[] { LlmChatMessage.FromSystem("sys"), LlmChatMessage.FromUser("hi") });

                Assert.AreEqual("Hello!", result.Content);
                Assert.AreEqual("pondering", result.Reasoning);
                string request = server.Requests.Single();
                StringAssert.StartsWith("POST /v1/chat/completions", request);
                StringAssert.Contains("\"model\":\"alpha-13b\"", request);
                StringAssert.Contains("\"stream\":false", request);
                StringAssert.Contains("{\"role\":\"user\",\"content\":\"hi\"}", request);
            }
        }

        [Test]
        public void Chat_ReadsReasoningContentField()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => FakeOpenAiServer.Reply(200,
                    "{\"choices\":[{\"message\":{\"content\":\"Hi\",\"reasoning_content\":\"thought\"}}]}");
                LlmChatResult result = OpenAICompatClient.Chat(new LlmEndpoint(server.BaseUrl, "", ""), new[] { LlmChatMessage.FromUser("x") });
                Assert.AreEqual("Hi", result.Content);
                Assert.AreEqual("thought", result.Reasoning);
            }
        }

        [Test]
        public void Unauthorized_IsReportedWithServerMessage()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => FakeOpenAiServer.Reply(401, "{\"error\":{\"message\":\"Invalid API key\"}}");
                var ex = Assert.Throws<LlmException>(() => OpenAICompatClient.ListModels(new LlmEndpoint(server.BaseUrl, "bad", "")));
                Assert.AreEqual(LlmErrorKind.Unauthorized, ex.Kind);
                StringAssert.Contains("Invalid API key", ex.Message);
            }
        }

        [Test]
        public void InvalidUrl_FailsWithoutNetwork()
        {
            var ex = Assert.Throws<LlmException>(() => OpenAICompatClient.ListModels(new LlmEndpoint("localhost:1234", "", "")));
            Assert.AreEqual(LlmErrorKind.InvalidUrl, ex.Kind);
        }

        [Test]
        public void ConnectionTest_AllStepsPass()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = HappyServer;
                var progress = new List<string>();
                ConnectionTestResult result = ConnectionTest.Run(new LlmEndpoint(server.BaseUrl, "", "alpha-13b"),
                    s => progress.Add(s.Name + ":" + s.Status));

                Assert.IsTrue(result.Succeeded, result.Summary);
                Assert.IsTrue(result.Steps.All(s => s.Status == TestStepStatus.Passed));
                CollectionAssert.Contains(progress, ConnectionTest.StepChat + ":Passed");
            }
        }

        [Test]
        public void ConnectionTest_UnknownModel_IsAWarning()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = HappyServer;
                ConnectionTestResult result = ConnectionTest.Run(new LlmEndpoint(server.BaseUrl, "", "missing-model"));
                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(TestStepStatus.Warning, result.Steps[2].Status);
            }
        }

        [Test]
        public void ConnectionTest_NoModelsEndpoint_FallsBackToChat()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => p.EndsWith("/models")
                    ? FakeOpenAiServer.Reply(404, "Not Found")
                    : FakeOpenAiServer.Reply(200, ChatJson);
                ConnectionTestResult result = ConnectionTest.Run(new LlmEndpoint(server.BaseUrl, "", "x"));
                Assert.IsTrue(result.Succeeded, result.Summary);
                Assert.AreEqual(TestStepStatus.Warning, result.Steps[1].Status);
                Assert.AreEqual(TestStepStatus.Passed, result.Steps[3].Status);
            }
        }

        [Test]
        public void ConnectionTest_BadKey_FailsAndSkipsTheRest()
        {
            using (var server = new FakeOpenAiServer())
            {
                server.Handler = (m, p, b, a) => FakeOpenAiServer.Reply(403, "{\"error\":\"forbidden\"}");
                ConnectionTestResult result = ConnectionTest.Run(new LlmEndpoint(server.BaseUrl, "nope", "x"));
                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(TestStepStatus.Failed, result.Steps[1].Status);
                StringAssert.Contains("API key", result.Steps[1].Detail);
                Assert.AreEqual(TestStepStatus.Skipped, result.Steps[3].Status);
            }
        }

        [Test]
        public void ConnectionTest_ServerDown_FailsAtReach()
        {
            // Nothing listens on port 9 (discard) on a dev box.
            ConnectionTestResult result = ConnectionTest.Run(new LlmEndpoint("http://127.0.0.1:9/v1", "", ""));
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(TestStepStatus.Failed, result.Steps[1].Status);
        }

        [Test]
        public void ApiKeyProtector_RoundTrips_AndHidesTheKey()
        {
            string stored = ApiKeyProtector.Protect("sk-secret-123");
            StringAssert.StartsWith("dpapi:", stored);
            StringAssert.DoesNotContain("sk-secret", stored);
            Assert.AreEqual("sk-secret-123", ApiKeyProtector.Unprotect(stored));
            Assert.AreEqual("plain-key", ApiKeyProtector.Unprotect("plain-key"));
            Assert.AreEqual("", ApiKeyProtector.Unprotect("dpapi:not-base64!"));
        }
    }
}
