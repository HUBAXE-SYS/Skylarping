/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// SillyTavern's macros ({{user}}, {{time}}, {{pick::a::b}}, {{setvar::x::1}}, {{if}} ...), for
// cards written for it. docs/Macros.md lists each one and what it does here.
//
//  - Names are case-insensitive. Arguments: {{name arg}}, {{name::a::b}}, or the old {{name:arg}}.
//  - Inner macros run first: {{getvar::{{char}}_mood}}. Unknown macros are left as they are,
//    as SillyTavern does. \{\{x\}\} gives a literal {{x}}.
//  - SillyTavern macros that make no sense here (instruct templates, swipes, the input box) are
//    accepted and give nothing, so they don't reach the model as raw text.
//  - Times use moment.js's English formats, since that's what SillyTavern's cards expect.
//  - {{pick}} is stable per chat. SillyTavern seeds it from the whole text and the macro's
//    position; our system prompt changes as lorebook entries come and go, so here the seed is
//    the macro's own text and which occurrence of that text it is.
//  - Variables are saved: per chat in the character's folder, global next to the characters.
//    As in SillyTavern, macros run every time a prompt is built, so {{incvar}} in a card counts
//    prompts (regenerating and mood messages too), not messages.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Skylarping.Characters
{
    /// <summary>Macro variables, kept in one JSON file (or only in memory without a path). Any thread.</summary>
    public sealed class MacroVariables
    {
        private readonly object _lock = new object();
        private readonly string _path;
        private Dictionary<string, string> _values;
        private string _seed;

        public MacroVariables(string path = null)
        {
            _path = path;
        }

        /// <summary>Random per chat; {{pick}} is seeded from it, so a cleared chat picks again.</summary>
        public string Seed
        {
            get
            {
                lock (_lock)
                {
                    Load();
                    if (_seed == null)
                    {
                        _seed = Guid.NewGuid().ToString("N");
                        Save();
                    }
                    return _seed;
                }
            }
        }

        /// <summary>The value, or null if there's no such variable.</summary>
        public string Get(string name)
        {
            lock (_lock)
            {
                Load();
                string value;
                return _values.TryGetValue(name, out value) ? value : null;
            }
        }

        public void Set(string name, string value)
        {
            lock (_lock)
            {
                Load();
                _values[name] = value ?? "";
                Save();
            }
        }

        public void Delete(string name)
        {
            lock (_lock)
            {
                Load();
                if (_values.Remove(name))
                    Save();
            }
        }

        /// <summary>Forgets everything, including the seed.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _values = new Dictionary<string, string>();
                _seed = null;
                try
                {
                    if (_path != null && File.Exists(_path))
                        File.Delete(_path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Only stale variables are left behind.
                }
            }
        }

        private void Load()
        {
            if (_values != null)
                return;
            _values = new Dictionary<string, string>();
            if (_path == null)
                return;
            try
            {
                if (!File.Exists(_path))
                    return;
                JObject root = JObject.Parse(File.ReadAllText(_path, Encoding.UTF8));
                _seed = (string)root["seed"];
                var values = root["values"] as JObject;
                if (values != null)
                    foreach (JProperty value in values.Properties())
                        _values[value.Name] = (string)value.Value ?? "";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                // A damaged file: start again rather than stop the character replying.
            }
        }

        private void Save()
        {
            if (_path == null)
                return;
            var values = new JObject();
            foreach (KeyValuePair<string, string> value in _values)
                values[value.Key] = value.Value;
            var root = new JObject { ["seed"] = _seed, ["values"] = values };
            try
            {
                string temp = _path + ".tmp";
                File.WriteAllText(temp, root.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(temp, _path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Kept in memory for this run.
            }
        }
    }

    /// <summary>What macros can see.</summary>
    public sealed class MacroContext
    {
        public string CharName = "Character";
        public string UserName = "User";
        public CharacterCard Card;
        /// <summary>"About me".</summary>
        public string Persona = "";
        public IList<ChatTurn> History = new ChatTurn[0];
        public DateTime Now = DateTime.Now;
        /// <summary>The user's last message before the newest message in the chat, or null.</summary>
        public DateTime? LastUserMessage;
        public int ContextWindow;
        public string Model = "";
        /// <summary>"normal", or "quiet" for what the user didn't ask for (proactive and mood messages).</summary>
        public string GenerationType = "normal";
        public MacroVariables Local = new MacroVariables();
        public MacroVariables Global = new MacroVariables();
    }

    public static class Macros
    {
        private const int MaxDepth = 8;      // a card field that uses its own macro
        private const int MaxRepeat = 100;   // {{newline::n}}, {{space::n}}
        private const int MaxDice = 1000;
        private const char TrimMark = '';

        private static readonly Random Rng = new Random();
        private static readonly CultureInfo En = CultureInfo.InvariantCulture;
        private static readonly Regex Legacy = new Regex(@"<(USER|BOT|CHAR|GROUP|CHARIFNOTGROUP)>", RegexOptions.IgnoreCase);
        private static readonly Regex Name = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*");
        private static readonly Regex Dice = new Regex(@"^\s*(\d*)\s*d\s*(\d+)\s*(?:([+-])\s*(\d+))?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex Offset = new Regex(@"^\s*(?:UTC)?\s*([+-]\s*\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex Shorthand = new Regex(
            @"^([.$])([A-Za-z](?:[A-Za-z0-9_-]*[A-Za-z0-9])?)\s*(\+\+|--|\|\|=|\?\?=|\|\||\?\?|\+=|-=|==|!=|>=|<=|=|>|<)?\s*(.*)$",
            RegexOptions.Singleline);
        private static readonly Regex MomentTokens = new Regex(
            @"\[[^\]]*\]|Do|YYYY|YY|MMMM|MMM|MM|M|DD|D|dddd|ddd|dd|d|HH|H|hh|h|mm|m|ss|s|SSS|SS|S|A|a|ZZ|Z|X|x");

        // SillyTavern macros with nothing behind them here.
        private static readonly HashSet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "noop", "input", "banned", "outlet", "summary", "original", "charDepthPrompt",
            "authorsNote", "charAuthorsNote", "defaultAuthorsNote",
            "instructStoryStringPrefix", "instructStoryStringSuffix", "instructUserPrefix", "instructUserSuffix",
            "instructAssistantPrefix", "instructAssistantSuffix", "instructSeparator", "instructSystemPrefix",
            "instructSystemSuffix", "instructFirstAssistantPrefix", "instructLastAssistantPrefix",
            "instructFirstUserPrefix", "instructLastUserPrefix", "instructStop", "instructUserFiller",
            "instructSystemInstructionPrefix", "chatSeparator", "chatStart",
            "reasoningPrefix", "reasoningSuffix", "reasoningSeparator", "charPrefix", "charNegativePrefix",
        };

        /// <summary>Runs every macro in the text.</summary>
        public static string Expand(string text, MacroContext context)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            string result = new Run(context).Expand(text, 0);
            return result.IndexOf(TrimMark) < 0 ? result
                : Regex.Replace(result, "(?:\\r?\\n)*" + TrimMark + "(?:\\r?\\n)*", "");
        }

        /// <summary>moment.js's humanize(): "a few seconds", "5 minutes", "an hour", "3 days", "2 months".</summary>
        public static string Humanize(TimeSpan span)
        {
            span = span.Duration();
            double seconds = Math.Round(span.TotalSeconds), minutes = Math.Round(span.TotalMinutes);
            double hours = Math.Round(span.TotalHours), days = Math.Round(span.TotalDays);
            double months = Math.Round(span.TotalDays / 30.436875), years = Math.Round(span.TotalDays / 365.25);
            if (seconds < 45) return "a few seconds";
            if (minutes < 2) return "a minute";
            if (minutes < 45) return minutes.ToString(En) + " minutes";
            if (hours < 2) return "an hour";
            if (hours < 22) return hours.ToString(En) + " hours";
            if (days < 2) return "a day";
            if (days < 26) return days.ToString(En) + " days";
            if (months < 2) return "a month";
            if (months < 11) return months.ToString(En) + " months";
            if (years < 2) return "a year";
            return years.ToString(En) + " years";
        }

        /// <summary>Formats a time with moment.js's tokens, e.g. "YYYY-MM-DD HH:mm:ss", "[at] h:mm A".</summary>
        public static string FormatMoment(DateTime time, string format)
        {
            return MomentTokens.Replace(format ?? "", match =>
            {
                string token = match.Value;
                if (token[0] == '[')
                    return token.Substring(1, token.Length - 2);
                switch (token)
                {
                    case "YYYY": return time.Year.ToString("0000", En);
                    case "YY": return (time.Year % 100).ToString("00", En);
                    case "MMMM": return time.ToString("MMMM", En);
                    case "MMM": return time.ToString("MMM", En);
                    case "MM": return time.Month.ToString("00", En);
                    case "M": return time.Month.ToString(En);
                    case "DD": return time.Day.ToString("00", En);
                    case "D": return time.Day.ToString(En);
                    case "Do": return time.Day.ToString(En) + Ordinal(time.Day);
                    case "dddd": return time.ToString("dddd", En);
                    case "ddd": return time.ToString("ddd", En);
                    case "dd": return time.ToString("dddd", En).Substring(0, 2);
                    case "d": return ((int)time.DayOfWeek).ToString(En);
                    case "HH": return time.Hour.ToString("00", En);
                    case "H": return time.Hour.ToString(En);
                    case "hh": return Hour12(time).ToString("00", En);
                    case "h": return Hour12(time).ToString(En);
                    case "mm": return time.Minute.ToString("00", En);
                    case "m": return time.Minute.ToString(En);
                    case "ss": return time.Second.ToString("00", En);
                    case "s": return time.Second.ToString(En);
                    case "SSS": return time.Millisecond.ToString("000", En);
                    case "SS": return (time.Millisecond / 10).ToString("00", En);
                    case "S": return (time.Millisecond / 100).ToString(En);
                    case "A": return time.Hour < 12 ? "AM" : "PM";
                    case "a": return time.Hour < 12 ? "am" : "pm";
                    case "Z": return UtcOffset(time, ":");
                    case "ZZ": return UtcOffset(time, "");
                    case "X": return ((long)(time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString(En);
                    default: return ((long)(time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString(En);
                }
            });
        }

        private static int Hour12(DateTime time)
        {
            return time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        }

        private static string Ordinal(int day)
        {
            if (day >= 11 && day <= 13)
                return "th";
            switch (day % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }

        private static string UtcOffset(DateTime time, string separator)
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(time).Duration();
            bool behind = TimeZoneInfo.Local.GetUtcOffset(time) < TimeSpan.Zero;
            return (behind ? "-" : "+") + offset.Hours.ToString("00", En) + separator + offset.Minutes.ToString("00", En);
        }

        private static bool Falsy(string value)
        {
            value = (value ?? "").Trim();
            return value.Length == 0 || value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Number(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim(), NumberStyles.Float, En, out value);
        }

        private static string Show(double value)
        {
            return value.ToString("R", En);
        }

        private static uint Fnv(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
                hash = (hash ^ c) * 16777619;
            return hash;
        }

        private static int NextRandom(int count)
        {
            lock (Rng)
                return Rng.Next(count);
        }

        /// <summary>One expansion: {{pick}} counts occurrences across it.</summary>
        private sealed class Run
        {
            private readonly MacroContext _c;
            private readonly Dictionary<string, int> _picks = new Dictionary<string, int>();

            public Run(MacroContext context)
            {
                _c = context;
            }

            public string Expand(string text, int depth)
            {
                text = Legacy.Replace(text, m =>
                {
                    switch (m.Groups[1].Value.ToUpperInvariant())
                    {
                        case "USER": return _c.UserName;
                        default: return _c.CharName; // solo chats: <GROUP> is the character too
                    }
                });

                var sb = new StringBuilder();
                int i = 0;
                while (i < text.Length)
                {
                    if (string.CompareOrdinal(text, i, @"\{\{", 0, 4) == 0 || string.CompareOrdinal(text, i, @"\}\}", 0, 4) == 0)
                    {
                        sb.Append(text[i + 1], 2);
                        i += 4;
                        continue;
                    }
                    if (string.CompareOrdinal(text, i, "{{", 0, 2) != 0)
                    {
                        sb.Append(text[i++]);
                        continue;
                    }
                    int close = FindClose(text, i);
                    if (close < 0)
                    {
                        sb.Append(text, i, text.Length - i);
                        break;
                    }
                    string raw = text.Substring(i + 2, close - i - 2);
                    string head = raw.Trim();
                    i = close + 2;

                    if (head == "//")
                    {
                        i = AfterBlockComment(text, i);
                    }
                    else if (head.StartsWith("//", StringComparison.Ordinal))
                    {
                        // A comment: removed.
                    }
                    else if (IsIf(head))
                    {
                        i = ExpandIf(text, head, i, depth, sb);
                    }
                    else
                    {
                        sb.Append(Evaluate(head, depth) ?? "{{" + raw + "}}");
                    }
                }
                return sb.ToString();
            }

            private static bool IsIf(string head)
            {
                return head.StartsWith("if ", StringComparison.OrdinalIgnoreCase)
                    || head.StartsWith("#if ", StringComparison.OrdinalIgnoreCase);
            }

            // The }} that closes the {{ at 'open', counting nested ones; -1 if there isn't one.
            private static int FindClose(string text, int open)
            {
                int depth = 0;
                for (int i = open; i < text.Length - 1; i++)
                {
                    if (text[i] == '{' && text[i + 1] == '{')
                    {
                        depth++;
                        i++;
                    }
                    else if (text[i] == '}' && text[i + 1] == '}')
                    {
                        if (--depth == 0)
                            return i;
                        i++;
                    }
                }
                return -1;
            }

            // {{ // }} ... {{ /// }}: returns where the text carries on.
            private static int AfterBlockComment(string text, int start)
            {
                int i = start;
                while ((i = text.IndexOf("{{", i, StringComparison.Ordinal)) >= 0)
                {
                    int close = FindClose(text, i);
                    if (close < 0)
                        break;
                    if (text.Substring(i + 2, close - i - 2).Trim() == "///")
                        return close + 2;
                    i = close + 2;
                }
                return text.Length;
            }

            private int ExpandIf(string text, string head, int start, int depth, StringBuilder sb)
            {
                bool keepSpace = head[0] == '#';
                string condition = head.Substring(keepSpace ? 4 : 3).Trim();

                int nested = 0, elseAt = -1, elseEnd = -1;
                int i = start;
                while ((i = text.IndexOf("{{", i, StringComparison.Ordinal)) >= 0)
                {
                    int close = FindClose(text, i);
                    if (close < 0)
                        break;
                    string inner = text.Substring(i + 2, close - i - 2).Trim();
                    if (IsIf(inner))
                    {
                        nested++;
                    }
                    else if (inner.Equals("/if", StringComparison.OrdinalIgnoreCase))
                    {
                        if (nested == 0)
                        {
                            string yes = text.Substring(start, (elseAt >= 0 ? elseAt : i) - start);
                            string no = elseAt >= 0 ? text.Substring(elseEnd, i - elseEnd) : "";
                            string chosen = Test(condition, depth) ? yes : no;
                            sb.Append(Expand(keepSpace ? chosen : Dedent(chosen), depth + 1));
                            return close + 2;
                        }
                        nested--;
                    }
                    else if (nested == 0 && elseAt < 0 && inner.Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        elseAt = i;
                        elseEnd = close + 2;
                    }
                    i = close + 2;
                }
                return start; // no {{/if}}: only the opening tag goes
            }

            private bool Test(string condition, int depth)
            {
                bool invert = condition.StartsWith("!", StringComparison.Ordinal);
                if (invert)
                    condition = condition.Substring(1).Trim();
                string value = condition.Contains("{{")
                    ? Expand(condition, depth + 1)
                    : Evaluate(condition, depth) ?? condition; // {{if description}} means the macro
                return Falsy(value) == invert;
            }

            private static string Dedent(string text)
            {
                string[] lines = text.Replace("\r\n", "\n").Trim('\n').Split('\n');
                int indent = lines.Where(l => l.Trim().Length > 0)
                                  .Select(l => l.Length - l.TrimStart().Length)
                                  .DefaultIfEmpty(0).Min();
                return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart()).ToArray()).Trim();
            }

            // Splits on :: outside nested macros.
            private static List<string> Split(string text)
            {
                var parts = new List<string>();
                int nested = 0, start = 0;
                for (int i = 0; i < text.Length - 1; i++)
                {
                    if (text[i] == '{' && text[i + 1] == '{') { nested++; i++; }
                    else if (text[i] == '}' && text[i + 1] == '}') { nested--; i++; }
                    else if (nested == 0 && text[i] == ':' && text[i + 1] == ':')
                    {
                        parts.Add(text.Substring(start, i - start));
                        start = i + 2;
                        i++;
                    }
                }
                parts.Add(text.Substring(start));
                return parts;
            }

            // The macro's value, or null if it isn't one.
            private string Evaluate(string head, int depth)
            {
                if (depth > MaxDepth)
                    return "";
                if (head.Length == 0)
                    return null;
                if (head[0] == '.' || head[0] == '$')
                    return EvaluateShorthand(head, depth);

                Match nameMatch = Name.Match(head);
                if (!nameMatch.Success)
                    return null;
                string name = nameMatch.Value;
                string rest = head.Substring(name.Length);

                if (name.Equals("time_UTC", StringComparison.OrdinalIgnoreCase))
                    return Time(rest);

                string trimmed = rest.TrimStart();
                var raw = new List<string>();
                if (trimmed.StartsWith("::", StringComparison.Ordinal))
                    raw = Split(trimmed.Substring(2));
                else if (trimmed.StartsWith(":", StringComparison.Ordinal))
                    raw.Add(trimmed.Substring(1));
                else if (trimmed.Length > 0 && rest.Length != trimmed.Length)
                    raw.Add(trimmed);
                else if (trimmed.Length > 0)
                    return null;

                List<string> args = raw.Select(a => Expand(a.Trim(), depth + 1)).ToList();
                return Call(name, head, args, depth);
            }

            private string Field(string text, int depth)
            {
                return string.IsNullOrEmpty(text) ? "" : Expand(text, depth + 1);
            }

            private string Call(string name, string head, List<string> args, int depth)
            {
                CharacterCard card = _c.Card;
                string first = args.Count > 0 ? args[0] : "";
                IList<ChatTurn> history = _c.History ?? new ChatTurn[0];
                int lastId = history.Count - 1;

                switch (name.ToLowerInvariant())
                {
                    // Names (a chat here is always one character)
                    case "user": case "notchar": return _c.UserName;
                    case "char": case "group": case "groupnotmuted": case "charifnotgroup": return _c.CharName;

                    // The character card
                    case "description": return Field(card?.Description, depth);
                    case "personality": return Field(card?.Personality, depth);
                    case "scenario": return card != null && card.UseScenario ? Field(card.Scenario, depth) : "";
                    case "persona": return Field(_c.Persona, depth);
                    case "charprompt": return Field(card?.SystemPrompt, depth);
                    case "charinstruction": return Field(card?.PostHistoryInstructions, depth);
                    case "charcreatornotes": return Field(card?.CreatorNotes, depth);
                    case "charversion": return card?.CharacterVersion ?? "";
                    case "mesexamples": return Field(PromptBuilder.FormatExamples(card?.MessageExamples ?? ""), depth);
                    case "mesexamplesraw": return Field(card?.MessageExamples, depth);
                    case "charfirstmessage": return Field(Greeting(first), depth);
                    case "systemprompt": case "defaultsystemprompt": return Field(PromptBuilder.DefaultSystemPrompt, depth);

                    // The chat
                    case "lastmessage": return lastId < 0 ? "" : Turn(history[lastId]);
                    case "lastusermessage": return Turn(history.LastOrDefault(t => !t.FromCharacter));
                    case "lastcharmessage": return Turn(history.LastOrDefault(t => t.FromCharacter));
                    case "lastmessageid": return lastId < 0 ? "" : lastId.ToString(En);
                    case "firstincludedmessageid": case "firstdisplayedmessageid": return "0";
                    case "allchatrange": return lastId < 0 ? "" : "0-" + lastId.ToString(En);
                    case "lastswipeid": case "currentswipeid": return "1";

                    // Time
                    case "time": return args.Count == 0 ? FormatMoment(_c.Now, "h:mm A") : Time(first);
                    case "date": return FormatMoment(_c.Now, "MMMM D, YYYY");
                    case "weekday": return FormatMoment(_c.Now, "dddd");
                    case "isotime": return FormatMoment(_c.Now, "HH:mm");
                    case "isodate": return FormatMoment(_c.Now, "YYYY-MM-DD");
                    case "datetimeformat": return FormatMoment(_c.Now, first);
                    case "idleduration": case "idle_duration":
                        return _c.LastUserMessage.HasValue ? Humanize(_c.Now - _c.LastUserMessage.Value) : "just now";
                    case "timediff": return args.Count < 2 ? null : TimeDiff(args[0], args[1]);

                    // Chance
                    case "random":
                        List<string> randomOptions = Options(args);
                        return randomOptions.Count == 0 ? "" : randomOptions[NextRandom(randomOptions.Count)];
                    case "pick":
                        List<string> pickOptions = Options(args);
                        return pickOptions.Count == 0 ? "" : pickOptions[Stable(head, pickOptions.Count)];
                    case "roll": return Roll(first);

                    // Variables
                    case "getvar": return _c.Local.Get(first) ?? "";
                    case "setvar": _c.Local.Set(first, args.Count > 1 ? args[1] : ""); return "";
                    case "addvar": Add(_c.Local, first, args.Count > 1 ? args[1] : ""); return "";
                    case "incvar": return Step(_c.Local, first, 1);
                    case "decvar": return Step(_c.Local, first, -1);
                    case "hasvar": case "varexists": return _c.Local.Get(first) != null ? "true" : "false";
                    case "deletevar": case "flushvar": _c.Local.Delete(first); return "";
                    case "getglobalvar": return _c.Global.Get(first) ?? "";
                    case "setglobalvar": _c.Global.Set(first, args.Count > 1 ? args[1] : ""); return "";
                    case "addglobalvar": Add(_c.Global, first, args.Count > 1 ? args[1] : ""); return "";
                    case "incglobalvar": return Step(_c.Global, first, 1);
                    case "decglobalvar": return Step(_c.Global, first, -1);
                    case "hasglobalvar": case "globalvarexists": return _c.Global.Get(first) != null ? "true" : "false";
                    case "deleteglobalvar": case "flushglobalvar": _c.Global.Delete(first); return "";

                    // Settings
                    case "maxcontexttokens": case "maxcontext": return _c.ContextWindow.ToString(En);
                    case "maxprompt": return ((int)(_c.ContextWindow * PromptBuilder.PromptShareOfContext)).ToString(En);
                    case "maxresponsetokens": case "maxresponse":
                        return (_c.ContextWindow - (int)(_c.ContextWindow * PromptBuilder.PromptShareOfContext)).ToString(En);
                    case "model": return _c.Model ?? "";
                    case "ismobile": case "hasextension": return "false";
                    case "lastgenerationtype": return _c.GenerationType ?? "normal";

                    // Text
                    case "newline": return Repeat('\n', args);
                    case "space": return Repeat(' ', args);
                    case "trim": return TrimMark.ToString();
                    case "reverse": return Reverse(first);
                }
                return Empty.Contains(name) ? "" : null;
            }

            private string Turn(ChatTurn turn)
            {
                return turn == null ? "" : PromptBuilder.Expand(turn.Text, _c.CharName, _c.UserName);
            }

            private string Greeting(string index)
            {
                CharacterCard card = _c.Card;
                if (card == null)
                    return "";
                int number;
                if (!int.TryParse(index, NumberStyles.Integer, En, out number) || number == 0)
                    return card.FirstMessage;
                return number > 0 && number <= card.AlternateGreetings.Count ? card.AlternateGreetings[number - 1] : "";
            }

            private string Time(string offsetText)
            {
                Match match = Offset.Match(offsetText ?? "");
                double hours;
                if (!match.Success || !Number(match.Groups[1].Value.Replace(" ", ""), out hours))
                    return null;
                return FormatMoment(_c.Now.ToUniversalTime().AddHours(hours), "h:mm A");
            }

            private static string TimeDiff(string left, string right)
            {
                DateTime a, b;
                if (!DateTime.TryParse(left, En, DateTimeStyles.None, out a) || !DateTime.TryParse(right, En, DateTimeStyles.None, out b))
                    return "";
                string span = Humanize(a - b);
                return a >= b ? "in " + span : span + " ago";
            }

            // {{random::a::b}}, or a comma list with \, for a literal comma: {{random:a,b}}
            private static List<string> Options(List<string> args)
            {
                if (args.Count != 1)
                    return args;
                const char comma = '';
                return args[0].Replace(@"\,", comma.ToString()).Split(',')
                    .Select(o => o.Replace(comma, ',').Trim()).ToList();
            }

            private int Stable(string head, int count)
            {
                int occurrence;
                _picks.TryGetValue(head, out occurrence);
                _picks[head] = occurrence + 1;
                uint seed = Fnv(_c.Local.Seed + "|" + head + "|" + occurrence.ToString(En));
                return new Random((int)(seed & 0x7FFFFFFF)).Next(count);
            }

            private static string Roll(string formula)
            {
                formula = (formula ?? "").Trim();
                if (formula.Length > 0 && formula.All(char.IsDigit))
                    formula = "1d" + formula;
                Match match = Dice.Match(formula);
                int dice, sides, bonus = 0;
                if (!match.Success
                    || !int.TryParse(match.Groups[1].Length == 0 ? "1" : match.Groups[1].Value, out dice)
                    || !int.TryParse(match.Groups[2].Value, out sides)
                    || (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, out bonus))
                    || dice < 1 || dice > MaxDice || sides < 1)
                    return "";
                long total = match.Groups[3].Value == "-" ? -bonus : bonus;
                for (int i = 0; i < dice; i++)
                    total += 1 + NextRandom(sides);
                return total.ToString(En);
            }

            private static void Add(MacroVariables store, string name, string value)
            {
                string current = store.Get(name);
                double a, b;
                if (current == null)
                    store.Set(name, value);
                else if (Number(current, out a) && Number(value, out b))
                    store.Set(name, Show(a + b));
                else
                    store.Set(name, current + value);
            }

            private static string Step(MacroVariables store, string name, int by)
            {
                double current;
                Number(store.Get(name), out current);
                string value = Show(current + by);
                store.Set(name, value);
                return value;
            }

            private static string Repeat(char c, List<string> args)
            {
                int count = 1;
                if (args.Count > 0)
                    int.TryParse(args[0], out count);
                return new string(c, Math.Max(0, Math.Min(MaxRepeat, count)));
            }

            private static string Reverse(string text)
            {
                var sb = new StringBuilder(text.Length);
                for (int i = text.Length - 1; i >= 0; i--)
                {
                    if (char.IsLowSurrogate(text[i]) && i > 0 && char.IsHighSurrogate(text[i - 1]))
                        sb.Append(text[--i]).Append(text[i + 1]);
                    else
                        sb.Append(text[i]);
                }
                return sb.ToString();
            }

            // {{.x}}, {{$x = 1}}, {{.x ++}}, {{.x || none}} ...: . is this chat, $ is global.
            private string EvaluateShorthand(string head, int depth)
            {
                Match match = Shorthand.Match(head);
                if (!match.Success)
                    return null;
                MacroVariables store = match.Groups[1].Value == "." ? _c.Local : _c.Global;
                string name = match.Groups[2].Value, op = match.Groups[3].Value, rhs = match.Groups[4].Value.Trim();
                if (op.Length == 0 && rhs.Length > 0)
                    return null;

                string value = store.Get(name);
                double a, b;
                switch (op)
                {
                    case "": return value ?? "";
                    case "=": store.Set(name, Expand(rhs, depth + 1)); return "";
                    case "++": return Step(store, name, 1);
                    case "--": return Step(store, name, -1);
                    case "+=": Add(store, name, Expand(rhs, depth + 1)); return "";
                    case "-=":
                        if (Number(Expand(rhs, depth + 1), out b))
                        {
                            Number(value, out a);
                            store.Set(name, Show(a - b));
                        }
                        return "";
                    case "||": return Falsy(value) ? Expand(rhs, depth + 1) : value;
                    case "??": return value ?? Expand(rhs, depth + 1);
                    case "||=":
                    case "??=":
                        if (op == "||=" ? Falsy(value) : value == null)
                        {
                            value = Expand(rhs, depth + 1);
                            store.Set(name, value);
                        }
                        return value;
                    case "==": return (value ?? "") == Expand(rhs, depth + 1) ? "true" : "false";
                    case "!=": return (value ?? "") != Expand(rhs, depth + 1) ? "true" : "false";
                }

                if (!Number(value, out a) || !Number(Expand(rhs, depth + 1), out b))
                    return "false";
                switch (op)
                {
                    case ">": return a > b ? "true" : "false";
                    case ">=": return a >= b ? "true" : "false";
                    case "<": return a < b ? "true" : "false";
                    default: return a <= b ? "true" : "false";
                }
            }
        }
    }
}
