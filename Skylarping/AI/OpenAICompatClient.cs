/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Minimal client for OpenAI-compatible servers (LM Studio, llama.cpp, Ollama, OpenAI,
// OpenRouter, ...). Ported from SimpleLLMChat's ModelsClient / LLMClient, on top of
// Skylarping.Net.Http so the curl fallback covers HTTPS on XP. Non-streaming for now;
// streaming and tool calls come with the character chat backend.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skylarping.Net;
using Skylarping.Preferences;

namespace Skylarping.AI
{
    /// <summary>Where to reach an OpenAI-compatible API, and which model to use.</summary>
    public sealed class LlmEndpoint
    {
        /// <param name="reasoningEffort">Sent as reasoning_effort ("none", "low", "medium", "high").
        /// Null or empty leaves it out, so the model uses its own default.</param>
        public LlmEndpoint(string baseUrl, string apiKey, string model, string reasoningEffort = null)
        {
            BaseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            ApiKey = (apiKey ?? "").Trim();
            Model = (model ?? "").Trim();
            ReasoningEffort = (reasoningEffort ?? "").Trim();
        }

        public string BaseUrl { get; private set; }
        public string ApiKey { get; private set; }
        public string Model { get; private set; }
        /// <summary>Value for reasoning_effort, or empty to not send it.</summary>
        public string ReasoningEffort { get; private set; }

        public static LlmEndpoint FromSettings()
        {
            return new LlmEndpoint(Settings.LlmServerUrl, Settings.LlmApiKey, Settings.LlmModel, Settings.LlmReasoningEffort);
        }

        /// <summary>Null when the URL is usable, otherwise a message saying what's wrong with it.</summary>
        public string ValidateUrl()
        {
            if (BaseUrl.Length == 0)
                return "No server URL has been entered.";
            Uri uri;
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "The server URL must start with http:// or https:// (http://ip:port/v1).";
            return null;
        }

        internal string Url(string path) { return BaseUrl + "/" + path; }
    }

    public enum LlmErrorKind
    {
        InvalidUrl,
        Unreachable,
        Unauthorized,
        NotFound,
        ServerError,
        BadResponse,
    }

    public sealed class LlmException : Exception
    {
        public LlmException(LlmErrorKind kind, string message, int statusCode = 0, Exception inner = null)
            : base(message, inner)
        {
            Kind = kind;
            StatusCode = statusCode;
        }

        public LlmErrorKind Kind { get; private set; }
        public int StatusCode { get; private set; }
    }

    public sealed class LlmChatMessage
    {
        public LlmChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public string Role { get; private set; }
        public string Content { get; private set; }

        public static LlmChatMessage FromSystem(string text) { return new LlmChatMessage("system", text); }
        public static LlmChatMessage FromUser(string text) { return new LlmChatMessage("user", text); }
        public static LlmChatMessage FromAssistant(string text) { return new LlmChatMessage("assistant", text); }
    }

    public sealed class LlmChatResult
    {
        public string Content { get; internal set; }
        /// <summary>The model's thinking, if the server returned any (reasoning_content or &lt;think&gt; tags).</summary>
        public string Reasoning { get; internal set; }
        public string FinishReason { get; internal set; }
        public string Model { get; internal set; }
    }

    public static class OpenAICompatClient
    {
        public const int ModelsTimeoutMilliseconds = 30000;
        public const int ChatTimeoutMilliseconds = 180000;

        /// <summary>GET {base}/models. Returns the model ids, sorted. Throws <see cref="LlmException"/>.</summary>
        public static IList<string> ListModels(LlmEndpoint endpoint)
        {
            string body = Send(endpoint, "models", "GET", null, ModelsTimeoutMilliseconds);

            JObject root = ParseObject(body, "model list");
            JArray data = root["data"] as JArray ?? root["models"] as JArray;
            if (data == null)
                throw new LlmException(LlmErrorKind.BadResponse, "The server's model list has no 'data' array.");

            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in data)
            {
                string id = item.Type == JTokenType.String ? (string)item : (string)item["id"];
                if (string.IsNullOrEmpty(id))
                    continue;
                id = id.Trim();
                if (id.Length > 0 && seen.Add(id))
                    ids.Add(id);
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        /// <summary>POST {base}/chat/completions (not streamed). Throws <see cref="LlmException"/>.</summary>
        public static LlmChatResult Chat(LlmEndpoint endpoint, IList<LlmChatMessage> messages, int maxTokens = 0,
            int timeoutMilliseconds = ChatTimeoutMilliseconds)
        {
            var payload = new JObject();
            if (endpoint.Model.Length > 0)
                payload["model"] = endpoint.Model;
            var array = new JArray();
            foreach (LlmChatMessage m in messages)
                array.Add(new JObject { ["role"] = m.Role, ["content"] = m.Content ?? "" });
            payload["messages"] = array;
            payload["stream"] = false;
            if (maxTokens > 0)
                payload["max_tokens"] = maxTokens;
            if (endpoint.ReasoningEffort.Length > 0)
                payload["reasoning_effort"] = endpoint.ReasoningEffort;

            string body = Send(endpoint, "chat/completions", "POST", payload.ToString(Newtonsoft.Json.Formatting.None), timeoutMilliseconds);

            JObject root = ParseObject(body, "chat reply");
            string inlineError = ExtractError(root);
            if (inlineError != null)
                throw new LlmException(LlmErrorKind.ServerError, inlineError);

            JObject choice = (root["choices"] as JArray)?.First as JObject;
            if (choice == null)
                throw new LlmException(LlmErrorKind.BadResponse, "The server's chat reply has no 'choices'.");

            JToken message = choice["message"];
            string content = message?["content"]?.Type == JTokenType.String ? (string)message["content"] : "";
            string reasoning = (string)message?["reasoning_content"] ?? (string)message?["reasoning"];

            // Many local models put their thinking inline as <think>...</think>.
            Match think = Regex.Match(content, @"^\s*<think>(.*?)(</think>|$)", RegexOptions.Singleline);
            if (think.Success)
            {
                if (string.IsNullOrEmpty(reasoning))
                    reasoning = think.Groups[1].Value.Trim();
                content = content.Substring(think.Length);
            }

            return new LlmChatResult
            {
                Content = content.Trim(),
                Reasoning = string.IsNullOrEmpty(reasoning) ? null : reasoning.Trim(),
                FinishReason = (string)choice["finish_reason"],
                Model = (string)root["model"],
            };
        }

        private static string Send(LlmEndpoint endpoint, string path, string method, string json, int timeout)
        {
            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
                throw new LlmException(LlmErrorKind.InvalidUrl, invalid);

            var request = new HttpRequest(endpoint.Url(path), method) { TimeoutMilliseconds = timeout };
            request.Headers["Accept"] = "application/json";
            if (endpoint.ApiKey.Length > 0)
                request.Headers["Authorization"] = "Bearer " + endpoint.ApiKey;
            if (json != null)
                request.WithText(json, "application/json");

            HttpResponse response;
            try
            {
                response = Http.Open(request);
            }
            catch (Exception ex)
            {
                throw new LlmException(LlmErrorKind.Unreachable, "Couldn't connect to " + endpoint.BaseUrl + ": " + ex.Message, 0, ex);
            }

            string body;
            using (response)
            {
                try
                {
                    body = Encoding.UTF8.GetString(response.ReadAllBytes());
                }
                catch (Exception ex)
                {
                    throw new LlmException(LlmErrorKind.Unreachable, "The connection was lost while reading the reply: " + ex.Message, 0, ex);
                }

                if (!response.IsSuccess)
                {
                    int code = response.StatusCode;
                    string detail = DescribeErrorBody(body);
                    string message = "HTTP " + code + (detail.Length > 0 ? ": " + detail : "");
                    LlmErrorKind kind =
                        code == 401 || code == 403 ? LlmErrorKind.Unauthorized
                        : code == 404 ? LlmErrorKind.NotFound
                        : LlmErrorKind.ServerError;
                    throw new LlmException(kind, message, code);
                }
            }
            return body;
        }

        private static JObject ParseObject(string body, string what)
        {
            if (string.IsNullOrEmpty(body) || body.Trim().Length == 0)
                throw new LlmException(LlmErrorKind.BadResponse, "The server sent an empty " + what + ".");
            try
            {
                JObject obj = JObject.Parse(body);
                return obj;
            }
            catch (Exception ex)
            {
                throw new LlmException(LlmErrorKind.BadResponse,
                    "The server's " + what + " isn't JSON. Is the URL pointing at the API (often ending in /v1)?", 0, ex);
            }
        }

        private static string ExtractError(JObject root)
        {
            JToken error = root["error"];
            if (error == null || error.Type == JTokenType.Null)
                return null;
            if (error.Type == JTokenType.String)
                return (string)error;
            return (string)error["message"] ?? error.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string DescribeErrorBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "";
            try
            {
                string fromJson = ExtractError(JObject.Parse(body));
                if (!string.IsNullOrEmpty(fromJson))
                    return fromJson;
            }
            catch { }
            body = body.Trim();
            return body.Length > 300 ? body.Substring(0, 300) + "..." : body;
        }
    }
}
