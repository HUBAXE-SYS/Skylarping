/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Minimal client for OpenAI-compatible servers (LM Studio, llama.cpp, Ollama, OpenAI,
// OpenRouter, ...). Ported from SimpleLLMChat's ModelsClient / LLMClient, on top of
// Skylarping.Net.Http so the curl fallback covers HTTPS on XP. Chat() waits for the whole
// reply; ChatStream() reads it as server-sent events (SSE parsing follows SimpleLLMChat's
// SseStreamParser). Tool calls follow the OpenAI format: pass a "tools" array, get back
// LlmChatResult.ToolCalls, and send the calls and their results back as messages.
// Transcribe() sends audio to a speech-to-text model (/audio/transcriptions), Synthesize()
// gets it back from a text-to-speech model (/audio/speech).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

        /// <summary>
        /// Where speech-to-text goes. A blank URL means <paramref name="languageModel"/>'s server; its
        /// API key is only used then, so that key never reaches a different server.
        /// </summary>
        public static LlmEndpoint ForSpeechToText(string url, string apiKey, string model, LlmEndpoint languageModel)
        {
            return (url ?? "").Trim().Length > 0
                ? new LlmEndpoint(url, apiKey, model)
                : new LlmEndpoint(languageModel.BaseUrl, languageModel.ApiKey, model);
        }

        public static LlmEndpoint SpeechToTextFromSettings()
        {
            return ForSpeechToText(Settings.SttServerUrl, Settings.SttApiKey, Settings.SttModel, FromSettings());
        }

        /// <summary>Where text-to-speech goes, by the same rule as <see cref="ForSpeechToText"/>.</summary>
        public static LlmEndpoint ForTextToSpeech(string url, string apiKey, string model, LlmEndpoint languageModel)
        {
            return ForSpeechToText(url, apiKey, model, languageModel);
        }

        public static LlmEndpoint TextToSpeechFromSettings()
        {
            return ForTextToSpeech(Settings.TtsServerUrl, Settings.TtsApiKey, Settings.TtsModel, FromSettings());
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

    /// <summary>One function call the model asked for.</summary>
    public sealed class LlmToolCall
    {
        public LlmToolCall(string id, string name, string arguments)
        {
            Id = id ?? "";
            Name = name ?? "";
            Arguments = arguments ?? "";
        }

        public string Id { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>The arguments as the model wrote them (JSON text, possibly malformed).</summary>
        public string Arguments { get; internal set; }
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
        /// <summary>For an assistant message: the tools it called.</summary>
        public IList<LlmToolCall> ToolCalls { get; private set; }
        /// <summary>For a "tool" message: the call it answers.</summary>
        public string ToolCallId { get; private set; }
        /// <summary>Base64 image sent with the message (a tool's image), or null.</summary>
        public string ImageBase64 { get; private set; }
        public string ImageMime { get; private set; }

        public static LlmChatMessage FromAssistantToolCalls(string text, IList<LlmToolCall> calls)
        {
            return new LlmChatMessage("assistant", text) { ToolCalls = calls };
        }

        public static LlmChatMessage FromTool(string toolCallId, string output, string imageBase64 = null, string imageMime = null)
        {
            return new LlmChatMessage("tool", output)
            {
                ToolCallId = toolCallId,
                ImageBase64 = string.IsNullOrEmpty(imageBase64) ? null : imageBase64,
                ImageMime = string.IsNullOrEmpty(imageMime) ? "image/png" : imageMime,
            };
        }

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
        /// <summary>The tools the model wants to call (empty when it simply answered).</summary>
        public IList<LlmToolCall> ToolCalls { get; internal set; } = new List<LlmToolCall>();
    }

    public static class OpenAICompatClient
    {
        public const int ModelsTimeoutMilliseconds = 30000;
        public const int ChatTimeoutMilliseconds = 180000;
        public const int TranscribeTimeoutMilliseconds = 60000;
        public const int SpeechTimeoutMilliseconds = 120000; // the first request also loads the model

        /// <summary>
        /// POST {base}/audio/transcriptions with a WAV file. Returns the text. Throws <see cref="LlmException"/>.
        /// </summary>
        /// <param name="language">A language code such as "en". Blank lets the server detect it.</param>
        public static string Transcribe(LlmEndpoint endpoint, byte[] wav, string language = null,
            int timeoutMilliseconds = TranscribeTimeoutMilliseconds)
        {
            string boundary = "----Skylarping" + Guid.NewGuid().ToString("N");
            byte[] form;
            using (var stream = new MemoryStream(wav.Length + 512))
            {
                Action<string> write = text =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                };
                Action<string, string> field = (name, value) =>
                {
                    if (!string.IsNullOrEmpty(value))
                        write("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value + "\r\n");
                };

                field("model", endpoint.Model);
                field("language", (language ?? "").Trim());
                field("response_format", "json");
                write("--" + boundary + "\r\nContent-Disposition: form-data; name=\"file\"; filename=\"speech.wav\"\r\n" +
                      "Content-Type: audio/wav\r\n\r\n");
                stream.Write(wav, 0, wav.Length);
                write("\r\n--" + boundary + "--\r\n");
                form = stream.ToArray();
            }

            string body;
            using (HttpResponse response = Open(endpoint, "audio/transcriptions", "POST", form,
                "multipart/form-data; boundary=" + boundary, timeoutMilliseconds, "application/json"))
            {
                body = ReadText(response);
            }

            JObject root = ParseObject(body, "transcription");
            string error = ExtractError(root);
            if (error != null)
                throw new LlmException(LlmErrorKind.ServerError, error);
            return ((string)root["text"] ?? "").Trim();
        }

        /// <summary>POST {base}/audio/speech. Returns a WAV file. Throws <see cref="LlmException"/>.</summary>
        /// <param name="speedPercent">100 is normal speed, and isn't sent.</param>
        /// <param name="referenceWavBase64">A WAV clip of the voice to clone. Only OpenMOSS models use it.</param>
        /// <param name="voiceDescription">Has an OpenMOSS model design a voice to match this description.</param>
        public static byte[] Synthesize(LlmEndpoint endpoint, string text, int speedPercent = 100,
            string referenceWavBase64 = null, string voiceDescription = null, int timeoutMilliseconds = SpeechTimeoutMilliseconds)
        {
            var payload = new JObject { ["input"] = text, ["response_format"] = "wav" };
            if (endpoint.Model.Length > 0)
                payload["model"] = endpoint.Model;
            if (speedPercent != 100)
                payload["speed"] = speedPercent / 100.0;
            if (!string.IsNullOrEmpty(referenceWavBase64))
                payload["reference_wav_b64"] = referenceWavBase64;
            if ((voiceDescription ?? "").Trim().Length > 0)
                payload["voice_design_description"] = voiceDescription.Trim();

            byte[] audio;
            using (HttpResponse response = Open(endpoint, "audio/speech", "POST", payload.ToString(Newtonsoft.Json.Formatting.None),
                timeoutMilliseconds, "audio/wav"))
            {
                try
                {
                    audio = response.ReadAllBytes();
                }
                catch (Exception ex)
                {
                    throw new LlmException(LlmErrorKind.Unreachable, "The connection was lost while reading the reply: " + ex.Message, 0, ex);
                }
            }

            if (audio.Length < 12 || Encoding.ASCII.GetString(audio, 0, 4) != "RIFF")
                throw new LlmException(LlmErrorKind.BadResponse, "The server's speech isn't a WAV file.");
            return audio;
        }

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
            int timeoutMilliseconds = ChatTimeoutMilliseconds, JArray tools = null)
        {
            JObject payload = ChatPayload(endpoint, messages, maxTokens, false, tools);
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
            content = SplitThink(content, r => { if (string.IsNullOrEmpty(reasoning)) reasoning = r; });

            var calls = new List<LlmToolCall>();
            if (message?["tool_calls"] is JArray toolCalls)
            {
                foreach (JToken call in toolCalls)
                {
                    JToken function = call["function"];
                    string name = (string)function?["name"];
                    if (string.IsNullOrEmpty(name))
                        continue;
                    JToken args = function["arguments"];
                    string argText = args == null || args.Type == JTokenType.Null ? ""
                        : args.Type == JTokenType.String ? (string)args
                        : args.ToString(Newtonsoft.Json.Formatting.None); // some servers send an object
                    calls.Add(new LlmToolCall((string)call["id"], name, argText));
                }
            }

            return new LlmChatResult
            {
                Content = content.Trim(),
                Reasoning = string.IsNullOrEmpty(reasoning) ? null : reasoning.Trim(),
                FinishReason = (string)choice["finish_reason"],
                Model = (string)root["model"],
                ToolCalls = FillMissingIds(calls),
            };
        }

        /// <summary>Some local servers leave out call ids; the tool results still need one to point at.</summary>
        private static List<LlmToolCall> FillMissingIds(List<LlmToolCall> calls)
        {
            for (int i = 0; i < calls.Count; i++)
                if (calls[i].Id.Length == 0)
                    calls[i].Id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12) + "_" + i;
            return calls;
        }

        private static JObject ChatPayload(LlmEndpoint endpoint, IList<LlmChatMessage> messages, int maxTokens, bool stream,
            JArray tools = null)
        {
            var payload = new JObject();
            if (endpoint.Model.Length > 0)
                payload["model"] = endpoint.Model;
            var array = new JArray();
            foreach (LlmChatMessage m in messages)
            {
                var obj = new JObject { ["role"] = m.Role, ["content"] = m.Content ?? "" };
                if (m.ImageBase64 != null)
                {
                    // Text + image parts, as SimpleLLMChat sends a tool's image.
                    obj["content"] = new JArray
                    {
                        new JObject { ["type"] = "text", ["text"] = m.Content ?? "" },
                        new JObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JObject { ["url"] = "data:" + m.ImageMime + ";base64," + m.ImageBase64 },
                        },
                    };
                }
                if (!string.IsNullOrEmpty(m.ToolCallId))
                    obj["tool_call_id"] = m.ToolCallId;
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    var calls = new JArray();
                    foreach (LlmToolCall call in m.ToolCalls)
                        calls.Add(new JObject
                        {
                            ["id"] = call.Id,
                            ["type"] = "function",
                            ["function"] = new JObject { ["name"] = call.Name, ["arguments"] = call.Arguments ?? "" },
                        });
                    obj["tool_calls"] = calls;
                }
                array.Add(obj);
            }
            payload["messages"] = array;
            payload["stream"] = stream;
            if (tools != null && tools.Count > 0)
                payload["tools"] = tools;
            if (maxTokens > 0)
                payload["max_tokens"] = maxTokens;
            if (endpoint.ReasoningEffort.Length > 0)
                payload["reasoning_effort"] = endpoint.ReasoningEffort;
            return payload;
        }

        /// <summary>
        ///  POST {base}/chat/completions with stream = true. <paramref name="onText"/> is called
        ///  (on this thread) with the whole visible reply so far each time it grows; thinking
        ///  (reasoning_content, or a leading &lt;think&gt; block) is left out of it. Stops early and
        ///  returns what arrived so far (FinishReason "cancelled") when <paramref name="cancel"/> is set.
        ///  Throws <see cref="LlmException"/>.
        /// </summary>
        public static LlmChatResult ChatStream(LlmEndpoint endpoint, IList<LlmChatMessage> messages, Action<string> onText,
            CancellationToken cancel, int maxTokens = 0, int timeoutMilliseconds = ChatTimeoutMilliseconds, JArray tools = null)
        {
            JObject payload = ChatPayload(endpoint, messages, maxTokens, true, tools);
            HttpResponse response = Open(endpoint, "chat/completions", "POST",
                payload.ToString(Newtonsoft.Json.Formatting.None), timeoutMilliseconds, "text/event-stream");

            var content = new StringBuilder();
            var reasoning = new StringBuilder();
            // Streamed tool calls arrive in pieces keyed by index: id and name first, then the arguments.
            var partialCalls = new SortedDictionary<int, LlmToolCall>();
            string finishReason = null, model = null, lastVisible = "";
            using (response)
            using (cancel.Register(() => { try { response.Dispose(); } catch { } })) // unblocks a pending read
            {
                try
                {
                    var reader = new StreamReader(response.Body, Encoding.UTF8);
                    string line;
                    while (!cancel.IsCancellationRequested && (line = reader.ReadLine()) != null)
                    {
                        if (!line.StartsWith("data:"))
                            continue;
                        string json = line.Substring(5).Trim();
                        if (json == "[DONE]")
                            break;
                        JObject obj;
                        try { obj = JObject.Parse(json); }
                        catch { continue; } // ignore malformed fragments, as SLLMC does

                        string inlineError = ExtractError(obj);
                        if (inlineError != null)
                            throw new LlmException(LlmErrorKind.ServerError, inlineError);
                        if (model == null)
                            model = (string)obj["model"];

                        JObject choice = (obj["choices"] as JArray)?.First as JObject;
                        if (choice == null)
                            continue;
                        JToken delta = choice["delta"];
                        string think = (string)delta?["reasoning_content"] ?? (string)delta?["reasoning"];
                        if (!string.IsNullOrEmpty(think))
                            reasoning.Append(think);
                        string piece = delta?["content"]?.Type == JTokenType.String ? (string)delta["content"] : null;
                        if (!string.IsNullOrEmpty(piece))
                        {
                            content.Append(piece);
                            string visible = SplitThink(content.ToString(), null).Trim();
                            if (visible != lastVisible)
                            {
                                lastVisible = visible;
                                if (onText != null)
                                    onText(visible);
                            }
                        }
                        if (delta?["tool_calls"] is JArray callDeltas)
                        {
                            foreach (JToken callDelta in callDeltas)
                            {
                                int index = callDelta["index"] != null && callDelta["index"].Type == JTokenType.Integer
                                    ? (int)callDelta["index"] : partialCalls.Count;
                                LlmToolCall call;
                                if (!partialCalls.TryGetValue(index, out call))
                                    partialCalls[index] = call = new LlmToolCall("", "", "");
                                string id = (string)callDelta["id"];
                                if (!string.IsNullOrEmpty(id))
                                    call.Id = id;
                                JToken function = callDelta["function"];
                                string name = (string)function?["name"];
                                if (!string.IsNullOrEmpty(name))
                                    call.Name = name;
                                JToken args = function?["arguments"];
                                if (args != null && args.Type == JTokenType.String)
                                    call.Arguments += (string)args;
                                else if (args != null && args.Type == JTokenType.Object)
                                    call.Arguments = args.ToString(Newtonsoft.Json.Formatting.None);
                            }
                        }
                        string finish = (string)choice["finish_reason"];
                        if (!string.IsNullOrEmpty(finish))
                            finishReason = finish;
                    }
                }
                catch (LlmException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!cancel.IsCancellationRequested)
                        throw new LlmException(LlmErrorKind.Unreachable, "The connection was lost while reading the reply: " + ex.Message, 0, ex);
                }
            }

            string inlineThink = null;
            string text = SplitThink(content.ToString(), r => inlineThink = r);
            string allReasoning = reasoning.Length > 0 ? reasoning.ToString() : inlineThink;
            return new LlmChatResult
            {
                Content = text.Trim(),
                Reasoning = string.IsNullOrEmpty(allReasoning) ? null : allReasoning.Trim(),
                FinishReason = cancel.IsCancellationRequested ? "cancelled" : finishReason,
                Model = model,
                ToolCalls = cancel.IsCancellationRequested ? new List<LlmToolCall>()
                    : FillMissingIds(partialCalls.Values.Where(c => c.Name.Length > 0).ToList()),
            };
        }

        /// <summary>
        ///  Removes a leading &lt;think&gt;...&lt;/think&gt; block (many local models think inline). An
        ///  unclosed block hides everything after it, which is right while it's still streaming.
        /// </summary>
        private static string SplitThink(string content, Action<string> reasoning)
        {
            Match think = Regex.Match(content, @"^\s*<think>(.*?)(</think>|$)", RegexOptions.Singleline);
            if (!think.Success)
                return content;
            if (reasoning != null)
                reasoning(think.Groups[1].Value.Trim());
            return content.Substring(think.Length);
        }

        private static string Send(LlmEndpoint endpoint, string path, string method, string json, int timeout)
        {
            using (HttpResponse response = Open(endpoint, path, method, json, timeout, "application/json"))
                return ReadText(response);
        }

        private static string ReadText(HttpResponse response)
        {
            try
            {
                return Encoding.UTF8.GetString(response.ReadAllBytes());
            }
            catch (Exception ex)
            {
                throw new LlmException(LlmErrorKind.Unreachable, "The connection was lost while reading the reply: " + ex.Message, 0, ex);
            }
        }

        /// <summary>Sends the request; returns the successful response for the caller to read and dispose.</summary>
        private static HttpResponse Open(LlmEndpoint endpoint, string path, string method, string json, int timeout, string accept)
        {
            byte[] body = json == null ? null : Encoding.UTF8.GetBytes(json);
            return Open(endpoint, path, method, body, "application/json", timeout, accept);
        }

        private static HttpResponse Open(LlmEndpoint endpoint, string path, string method, byte[] body, string contentType,
            int timeout, string accept)
        {
            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
                throw new LlmException(LlmErrorKind.InvalidUrl, invalid);

            var request = new HttpRequest(endpoint.Url(path), method) { TimeoutMilliseconds = timeout };
            request.Headers["Accept"] = accept;
            if (endpoint.ApiKey.Length > 0)
                request.Headers["Authorization"] = "Bearer " + endpoint.ApiKey;
            if (body != null)
            {
                request.Body = body;
                request.ContentType = contentType;
            }

            HttpResponse response;
            try
            {
                response = Http.Open(request);
            }
            catch (Exception ex)
            {
                throw new LlmException(LlmErrorKind.Unreachable, "Couldn't connect to " + endpoint.BaseUrl + ": " + ex.Message, 0, ex);
            }

            if (response.IsSuccess)
                return response;

            string errorBody;
            using (response)
            {
                try
                {
                    errorBody = Encoding.UTF8.GetString(response.ReadAllBytes());
                }
                catch (Exception)
                {
                    errorBody = ""; // still report the status code
                }

                {
                    int code = response.StatusCode;
                    string detail = DescribeErrorBody(errorBody);
                    string message = "HTTP " + code + (detail.Length > 0 ? ": " + detail : "");
                    LlmErrorKind kind =
                        code == 401 || code == 403 ? LlmErrorKind.Unauthorized
                        : code == 404 ? LlmErrorKind.NotFound
                        : LlmErrorKind.ServerError;
                    throw new LlmException(kind, message, code);
                }
            }
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
