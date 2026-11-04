/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// SimpleLLMChat-compatible tools. Ported from SimpleLLMChat's ToolRegistry / ManifestScanner /
// ToolResultParser, so any SLLMC tool package works here unchanged:
//
//  Follows the SimpleLLMChat Tool SDK (github.com/randomNinja64/SimpleLLMChat-Tool-SDK):
//  - Packages are installed in tools\<package>\ next to Skylarping.exe: a <Package>.json
//    manifest (name, executable, options, tools, optional context_injector) beside its
//    executable and dependencies (a manifest directly in tools\ works too, as in SLLMC).
//    None are bundled; build.bat packages whatever is in deps\tools\.
//  - A call runs "<exe> <tool_name>" with { "arguments": {...}, "config": {...} } on stdin and
//    reads { "text": ..., "image": { "mime", "data" } } (or plain text) from stdout.
//  - The model gets "Command / Exit Code / Output" text, with stderr appended; an image
//    goes along with the tool message as an image part, as in SLLMC.
//  - config is the manifest's option defaults, overlaid with what the user set in
//    Tools > AI Services > Tools. Keys are SLLMC's ini keys (the option names), one shared
//    namespace. Skylarping's own defaults ship in ToolDefaults.ini (same format as SLLMC's
//    LLMSettings.ini) and sit between the manifest defaults and the user's settings.
//  - {character_folder} in any value is the folder of the character using the tool; the
//    shipped memoriesDirectory = {character_folder}\memories keeps memories per character.
//  - {config.name} and {config.name*N} in descriptions are filled in, as in SLLMC.
//
// Differences from SLLMC: no approvals (a tool enabled for a character is approved) and
// nothing is shown in chat (characters use tools out of sight). Timeouts are per tool as
// in SLLMC (tooltimeout.<tool>, seconds, 0 = none; set in AI Services > Tools), and a
// reply being cancelled kills a running tool.
// The app folder is put on the tool's PATH so tools that run curl.exe (e.g. SLLMC's
// WebTools) find the one next to Skylarping.exe on XP.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Skylarping.AI.Tools
{
    public sealed class ToolOption
    {
        public string Name { get; internal set; }
        public string Label { get; internal set; }
        /// <summary>"string", "int" or "bool" (SLLMC manifests use these).</summary>
        public string Type { get; internal set; }
        public string Default { get; internal set; }
    }

    public sealed class ToolFunction
    {
        public string Name { get; internal set; }
        /// <summary>The description as written in the manifest, placeholders not yet filled in.</summary>
        public string Description { get; internal set; }
        internal JArray Parameters { get; set; }
        public ToolPackage Package { get; internal set; }

        /// <summary>The name for people: "run_web_search" -> "Run Web Search" (see <see cref="ToolRegistry.DisplayName"/>).</summary>
        public string DisplayName => ToolRegistry.DisplayName(Name);
    }

    public sealed class ToolPackage
    {
        /// <summary>Display name from the manifest, e.g. "Web Tools".</summary>
        public string Name { get; internal set; }
        /// <summary>Executable name without extension, e.g. "WebTools". Used as the settings key.</summary>
        public string Key { get; internal set; }
        public string ExecutablePath { get; internal set; }
        public string ContextInjector { get; internal set; }
        public IList<ToolOption> Options { get; internal set; }
        public IList<ToolFunction> Functions { get; internal set; }
    }

    /// <summary>What a tool call gives back to the model.</summary>
    public sealed class ToolOutput
    {
        public ToolOutput(string text, string imageBase64 = null, string imageMime = null)
        {
            Text = text ?? "";
            ImageBase64 = string.IsNullOrEmpty(imageBase64) ? null : imageBase64;
            ImageMime = ImageBase64 == null ? null : (string.IsNullOrEmpty(imageMime) ? "image/png" : imageMime);
        }

        /// <summary>The formatted text for the tool message.</summary>
        public string Text { get; private set; }
        /// <summary>Base64 image the tool returned, or null.</summary>
        public string ImageBase64 { get; private set; }
        public string ImageMime { get; private set; }
    }

    public static class ToolRegistry
    {

        /// <summary>
        ///  Host placeholder, filled in in every option value when a character uses a tool:
        ///  that character's folder. E.g. memoriesDirectory = {character_folder}\memories gives
        ///  each character its own memories (removed with the character).
        /// </summary>
        public const string CharacterFolderPlaceholder = "{character_folder}";

        /// <summary>Skylarping's defaults for tool options, shipped next to the exe (see ToolDefaults.ini).</summary>
        public const string DefaultsFileName = "ToolDefaults.ini";

        private static readonly object Lock = new object();
        private static List<ToolPackage> _packages;
        private static Dictionary<string, string> _shippedDefaults;

        /// <summary>The values in ToolDefaults.ini (loaded once; see <see cref="Reload"/>).</summary>
        public static Dictionary<string, string> ShippedDefaults
        {
            get
            {
                lock (Lock)
                {
                    if (_shippedDefaults == null)
                        _shippedDefaults = LoadIni(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultsFileName));
                    return _shippedDefaults;
                }
            }
        }

        public static string ToolsDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools"); }
        }

        /// <summary>The installed tool packages (scanned once; see <see cref="Reload"/>).</summary>
        public static IList<ToolPackage> Packages
        {
            get
            {
                lock (Lock)
                {
                    if (_packages == null)
                        _packages = Scan(ToolsDirectory);
                    return _packages;
                }
            }
        }

        /// <summary>
        ///  An option's default: ToolDefaults.ini if it sets the key (the shipped file is only
        ///  read when <paramref name="shipped"/>), otherwise the manifest's.
        /// </summary>
        public static string DefaultFor(ToolOption option, bool shipped = true)
        {
            string value;
            if (shipped && ShippedDefaults.TryGetValue(option.Name, out value))
                return value;
            return option.Default ?? "";
        }

        /// <summary>
        ///  Reads an ini file the way SimpleLLMChat's IniFileHandler does: one key=value per line,
        ///  split at the first '=' (values may contain '='), keys case-insensitive, both sides
        ///  trimmed, the last value for a key wins, UTF-8 (a BOM is fine). Backslashes are literal.
        ///  Ignored: blank lines, comments starting with '#' (SLLMC) or ';', [section] headers,
        ///  lines without '=', and empty keys. A missing file is an empty result.
        /// </summary>
        public static Dictionary<string, string> LoadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return values;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                    return ParseIni(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tools] Couldn't read " + path + ": " + ex.Message);
                return values;
            }
        }

        public static Dictionary<string, string> ParseIni(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || (line[0] == '[' && line.EndsWith("]")))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                if (key.Length > 0)
                    values[key] = line.Substring(eq + 1).Trim();
            }
            return values;
        }

        /// <summary>Fills in host placeholders ({character_folder}) in an option value.</summary>
        public static string ExpandHostPlaceholders(string value, string characterFolder)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf(CharacterFolderPlaceholder, StringComparison.OrdinalIgnoreCase) < 0)
                return value ?? "";
            string folder = (characterFolder ?? "").TrimEnd('\\');
            return Regex.Replace(value, Regex.Escape(CharacterFolderPlaceholder), folder.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }

        public static void Reload()
        {
            lock (Lock)
            {
                _packages = null;
                _shippedDefaults = null;
            }
        }

        /// <summary>
        ///  Manifests have no display name for a tool, so one is made from its name, which by
        ///  SimpleLLMChat convention is word_word_word: underscores become spaces and each word
        ///  starts with a capital ("save_memory" -> "Save Memory"). The rest of each word is
        ///  left as is, so "read_URL" stays "Read URL".
        /// </summary>
        public static string DisplayName(string toolName)
        {
            string[] words = (toolName ?? "").Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return toolName ?? "";
            for (int i = 0; i < words.Length; i++)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        public static ToolFunction Find(string name)
        {
            foreach (ToolPackage package in Packages)
                foreach (ToolFunction function in package.Functions)
                    if (string.Equals(function.Name, name, StringComparison.OrdinalIgnoreCase))
                        return function;
            return null;
        }

        #region Manifests

        public static List<ToolPackage> Scan(string toolsDir)
        {
            var packages = new List<ToolPackage>();
            if (!Directory.Exists(toolsDir))
                return packages;
            var manifests = new List<string>(Directory.GetFiles(toolsDir, "*.json"));
            foreach (string dir in Directory.GetDirectories(toolsDir))
                manifests.AddRange(Directory.GetFiles(dir, "*.json"));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in manifests.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ToolPackage package = LoadManifest(path);
                    if (package == null)
                        continue;
                    // A tool name belongs to the first package that declares it.
                    package.Functions = package.Functions.Where(f => seen.Add(f.Name)).ToList();
                    if (package.Functions.Count > 0)
                        packages.Add(package);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Tools] Skipping manifest " + path + ": " + ex.Message);
                }
            }
            return packages;
        }

        private static ToolPackage LoadManifest(string path)
        {
            JObject manifest = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            string executable = (string)manifest["executable"];
            if (string.IsNullOrEmpty(executable) || !(manifest["tools"] is JArray))
                return null; // not a tool manifest
            string exePath = Path.Combine(Path.GetDirectoryName(path), executable);
            if (!File.Exists(exePath))
                return null;

            var package = new ToolPackage
            {
                Name = (string)manifest["name"] ?? Path.GetFileNameWithoutExtension(executable),
                Key = Path.GetFileNameWithoutExtension(executable),
                ExecutablePath = exePath,
                ContextInjector = (string)manifest["context_injector"],
                Options = new List<ToolOption>(),
                Functions = new List<ToolFunction>(),
            };

            foreach (JToken opt in manifest["options"] as JArray ?? new JArray())
            {
                string name = (string)opt["name"];
                if (string.IsNullOrEmpty(name))
                    continue;
                package.Options.Add(new ToolOption
                {
                    Name = name,
                    Label = (string)opt["label"] ?? name,
                    Type = ((string)opt["type"] ?? "string").ToLowerInvariant(),
                    Default = opt["default"] == null ? "" : opt["default"].ToString(),
                });
            }

            foreach (JToken tool in (JArray)manifest["tools"])
            {
                string name = (string)tool["name"];
                if (string.IsNullOrEmpty(name))
                    continue;
                package.Functions.Add(new ToolFunction
                {
                    Name = name,
                    Description = (string)tool["description"] ?? "",
                    Parameters = tool["parameters"] as JArray ?? new JArray(),
                    Package = package,
                });
            }
            return package;
        }

        #endregion

        #region Config

        /// <summary>
        ///  The config sent to a package, by ini key, later wins: the manifest default, then
        ///  ToolDefaults.ini, then the user's setting in AI Services (blank = not set), then
        ///  <paramref name="overrides"/> (blank ones are ignored). Host placeholders aren't
        ///  filled in here; see <see cref="ExpandHostPlaceholders"/>.
        /// </summary>
        public static Dictionary<string, string> Config(ToolPackage package, IDictionary<string, string> overrides = null,
            bool userSettings = true)
        {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ToolOption option in package.Options)
                config[option.Name] = DefaultFor(option, userSettings);
            if (userSettings)
            {
                foreach (ToolOption option in package.Options)
                {
                    string set = Preferences.Settings.GetToolOption(option.Name);
                    if (!string.IsNullOrEmpty(set))
                        config[option.Name] = set;
                }
            }
            if (overrides != null)
                foreach (var kv in overrides)
                    if (!string.IsNullOrEmpty(kv.Value))
                        config[kv.Key] = kv.Value;
            return config;
        }

        /// <summary>Fills in {config.key} and {config.key*N} as SimpleLLMChat does.</summary>
        public static string FillPlaceholders(string text, IDictionary<string, string> config)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            return Regex.Replace(text, @"\{config\.(\w+?)(?:\*(\d+))?\}", match =>
            {
                string value;
                if (!config.TryGetValue(match.Groups[1].Value, out value) || string.IsNullOrEmpty(value))
                    return match.Value;
                int multiplier, number;
                if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out multiplier) && int.TryParse(value, out number))
                    return (number * multiplier).ToString();
                return value;
            });
        }

        #endregion

        #region Requests

        /// <summary>The OpenAI "tools" array for the given functions.</summary>
        public static JArray BuildToolsArray(IEnumerable<ToolFunction> functions, Func<ToolPackage, IDictionary<string, string>> configFor)
        {
            var array = new JArray();
            foreach (ToolFunction function in functions)
            {
                IDictionary<string, string> config = configFor(function.Package);
                var properties = new JObject();
                var required = new JArray();
                foreach (JToken param in function.Parameters)
                {
                    string name = (string)param["name"];
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var property = new JObject
                    {
                        ["type"] = (string)param["type"] ?? "string",
                        ["description"] = FillPlaceholders((string)param["description"] ?? "", config),
                    };
                    if (param["items"] is JObject items)
                        property["items"] = items.DeepClone();
                    if (param["enum"] is JArray values)
                        property["enum"] = values.DeepClone();
                    properties[name] = property;
                    JToken req = param["required"];
                    if (req != null && req.Type == JTokenType.Boolean && (bool)req)
                        required.Add(name);
                }
                array.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = function.Name,
                        ["description"] = FillPlaceholders(function.Description, config),
                        ["parameters"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = required,
                        },
                    },
                });
            }
            return array;
        }

        #endregion

        #region Running

        /// <summary>
        ///  Runs one tool call and returns what goes back to the model: text in SimpleLLMChat's
        ///  "Command / Exit Code / Output" format, plus the tool's image if it sent one. Never
        ///  throws (except when cancelled): failures are reported to the model as the output.
        /// </summary>
        public static ToolOutput Run(ToolFunction function, string argumentsJson, IDictionary<string, string> config,
            int timeoutSeconds, CancellationToken cancel)
        {
            int timeoutMilliseconds = Math.Max(0, timeoutSeconds) * 1000; // 0 = no timeout, as in SimpleLLMChat
            JToken arguments;
            try
            {
                arguments = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JToken.Parse(argumentsJson);
            }
            catch (Exception)
            {
                return new ToolOutput(Format(function.Name, "error: the arguments aren't valid JSON: " + argumentsJson, 1));
            }

            try
            {
                ProcessResult result = RunProcess(function.Package.ExecutablePath, function.Name,
                    Payload(arguments, config), timeoutMilliseconds, cancel);
                if (result.TimedOut)
                    return new ToolOutput(Format(function.Name, "error: tool call timed out after " + timeoutSeconds + " second(s).", -1));
                string image, mime;
                string text = ParseOutput(result.Stdout, result.Stderr, out image, out mime);
                return new ToolOutput(Format(function.Name, text, result.ExitCode), image, mime);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ToolOutput(Format(function.Name, "error: " + ex.Message, -1));
            }
        }

        /// <summary>Runs a package's context injector; null when it has none, fails or says nothing.</summary>
        public static string RunContextInjector(ToolPackage package, IDictionary<string, string> config, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(package.ContextInjector))
                return null;
            try
            {
                ProcessResult result = RunProcess(package.ExecutablePath, package.ContextInjector,
                    Payload(new JObject(), config), 0, cancel); // no timeout, as in SLLMC; a new message still cancels it
                if (result.TimedOut || result.ExitCode != 0)
                    return null;
                string text = ParseOutput(result.Stdout, null).Trim();
                return text.Length == 0 ? null : text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tools] Context injector " + package.ContextInjector + " failed: " + ex.Message);
                return null;
            }
        }

        public static string Format(string command, string output, int exitCode)
        {
            return "Command: " + command + "\nExit Code: " + exitCode + "\nOutput:\n" + output;
        }

        private static string Payload(JToken arguments, IDictionary<string, string> config)
        {
            var configObj = new JObject();
            foreach (var kv in config)
                configObj[kv.Key] = kv.Value ?? "";
            return new JObject { ["arguments"] = arguments, ["config"] = configObj }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>The tool's { "text": ... } envelope, or its plain stdout; stderr is appended.</summary>
        public static string ParseOutput(string stdout, string stderr)
        {
            string image, mime;
            return ParseOutput(stdout, stderr, out image, out mime);
        }

        public static string ParseOutput(string stdout, string stderr, out string imageBase64, out string imageMime)
        {
            imageBase64 = null;
            imageMime = null;
            string text = stdout ?? "";
            string trimmed = text.Trim();
            if (trimmed.StartsWith("{"))
            {
                try
                {
                    if (JToken.Parse(trimmed) is JObject obj)
                    {
                        JToken t = obj["text"];
                        text = t == null || t.Type == JTokenType.Null ? ""
                            : t.Type == JTokenType.String ? (string)t : t.ToString();
                        if (obj["image"] is JObject image && image["data"] != null && image["data"].Type == JTokenType.String
                            && ((string)image["data"]).Length > 0)
                        {
                            imageBase64 = (string)image["data"];
                            imageMime = image["mime"] != null && image["mime"].Type == JTokenType.String ? (string)image["mime"] : null;
                            if (string.IsNullOrEmpty(imageMime))
                                imageMime = "image/png";
                            if (text.Length == 0)
                                text = "(no text)"; // as in SLLMC
                        }
                    }
                }
                catch (Exception)
                {
                    // not the envelope; keep stdout as is
                }
            }
            if (!string.IsNullOrEmpty(stderr))
                text = text.Length == 0 ? stderr : text.TrimEnd('\n') + "\n" + stderr;
            return text;
        }

        private sealed class ProcessResult
        {
            public string Stdout = "";
            public string Stderr = "";
            public int ExitCode;
            public bool TimedOut;
        }

        private static ProcessResult RunProcess(string exe, string arguments, string stdin, int timeoutMilliseconds,
            CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Tools look for helpers such as curl.exe on PATH; on XP there's no system curl, so
            // let them find the one shipped next to Skylarping.exe.
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string path = psi.EnvironmentVariables["PATH"] ?? "";
            psi.EnvironmentVariables["PATH"] = path.Length == 0 ? appDir : path + ";" + appDir;

            using (Process process = Process.Start(psi))
            {
                string stdout = "", stderr = "";
                var readOut = new Thread(() => { try { stdout = process.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                var readErr = new Thread(() => { try { stderr = process.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                readOut.Start();
                readErr.Start();

                try
                {
                    // Without a BOM: tools parse stdin as JSON.
                    var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
                    input.Write(stdin ?? "");
                    input.Close();
                }
                catch (IOException)
                {
                    // the tool exited without reading its input; its output still says why
                }

                bool exited;
                using (cancel.Register(() => Kill(process)))
                    exited = process.WaitForExit(timeoutMilliseconds > 0 ? timeoutMilliseconds : -1);
                if (!exited)
                    Kill(process);
                cancel.ThrowIfCancellationRequested();
                if (!exited)
                    return new ProcessResult { TimedOut = true, ExitCode = -1 };

                process.WaitForExit(); // let the output readers finish
                readOut.Join(5000);
                readErr.Join(5000);
                return new ProcessResult { Stdout = stdout, Stderr = stderr, ExitCode = process.ExitCode };
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
                process.WaitForExit(2000);
            }
            catch { }
        }

        #endregion
    }
}
