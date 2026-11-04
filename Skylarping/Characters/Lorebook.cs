/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A card's lorebook ("character_book" in Character Card V2/V3): facts with trigger
// keywords. An entry is added to the prompt only while one of its keywords appears in the
// recent messages, so a character can know a lot without filling the context window.
//
// Spec fields handled (V2): name, description, scan_depth, token_budget,
// recursive_scanning; entries: keys, content, enabled, insertion_order, case_sensitive,
// name, comment, priority, id, selective, secondary_keys, constant, position
// ("before_char" / "after_char"). V3 adds use_regex. Unknown fields (extensions, V3
// decorators, SillyTavern's extras) are kept as-is and written back.
//
// Activation (what the spec leaves to the app):
//  - scans the last scan_depth messages (default 4), plus already-activated entries'
//    text when recursive_scanning is on (up to 3 rounds);
//  - plain keys match as case-insensitive substrings unless case_sensitive; keys are
//    regular expressions when use_regex is set or written SillyTavern-style as /pattern/flags;
//  - selective entries also need one of their secondary keys; constant entries always apply;
//  - over the token budget, lower priority entries are dropped first; the rest are
//    inserted in insertion_order (lower = higher up).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Skylarping.Characters
{
    public sealed class LoreEntry
    {
        private readonly JObject _raw;

        public LoreEntry() : this(new JObject()) { Enabled = true; InsertionOrder = 100; }

        internal LoreEntry(JObject raw)
        {
            _raw = raw;
            Keys = Strings(raw["keys"]);
            SecondaryKeys = Strings(raw["secondary_keys"]);
            Content = CharacterCard.NormalizeNewlines((string)Value(raw["content"]) ?? "");
            // Cards often carry an empty "comment" next to a filled "name" (or the reverse).
            Name = new[] { (string)Value(raw["comment"]), (string)Value(raw["name"]) }
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
            Enabled = Bool(raw["enabled"], true);
            InsertionOrder = Int(raw["insertion_order"], 100);
            CaseSensitive = Bool(raw["case_sensitive"], false);
            Priority = Int(raw["priority"], 0);
            Selective = Bool(raw["selective"], false);
            Constant = Bool(raw["constant"], false);
            UseRegex = Bool(raw["use_regex"], false);
            AfterDescription = (string)Value(raw["position"]) == "after_char";
        }

        public List<string> Keys { get; set; }
        public List<string> SecondaryKeys { get; set; }
        public string Content { get; set; }
        /// <summary>A label for the editor (comment / name); not sent to the model.</summary>
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public int InsertionOrder { get; set; }
        public bool CaseSensitive { get; set; }
        public int Priority { get; set; }
        /// <summary>Also needs one of the secondary keys to match.</summary>
        public bool Selective { get; set; }
        /// <summary>Always included, whatever is said.</summary>
        public bool Constant { get; set; }
        public bool UseRegex { get; set; }
        /// <summary>Inserted after the character's description instead of before it.</summary>
        public bool AfterDescription { get; set; }

        /// <summary>What the editor's list shows.</summary>
        public string Title
        {
            get
            {
                string label = Name.Trim().Length > 0 ? Name.Trim()
                    : Keys.Count > 0 ? string.Join(", ", Keys.ToArray())
                    : "(new entry)";
                return (Enabled ? "" : "[off] ") + (Constant ? "[always] " : "") + label;
            }
        }

        public override string ToString() { return Title; }

        internal JObject ToJObject(int index)
        {
            JObject o = (JObject)_raw.DeepClone();
            o["keys"] = new JArray(Keys);
            o["content"] = CharacterCard.NormalizeNewlines(Content);
            o["extensions"] = o["extensions"] as JObject ?? new JObject();
            o["enabled"] = Enabled;
            o["insertion_order"] = InsertionOrder;
            o["case_sensitive"] = CaseSensitive;
            o["priority"] = Priority;
            o["selective"] = Selective;
            o["secondary_keys"] = new JArray(SecondaryKeys);
            o["constant"] = Constant;
            o["position"] = AfterDescription ? "after_char" : "before_char";
            o["comment"] = Name ?? "";
            if (o["name"] != null)
                o["name"] = Name ?? "";
            if (UseRegex || o["use_regex"] != null)
                o["use_regex"] = UseRegex;
            if (o["id"] == null)
                o["id"] = index;
            return o;
        }

        #region Matching

        internal bool Matches(string text)
        {
            if (!Enabled || Content.Trim().Length == 0)
                return false;
            if (Constant)
                return true;
            if (!Keys.Any(k => KeyMatches(k, text)))
                return false;
            if (Selective && SecondaryKeys.Count > 0)
                return SecondaryKeys.Any(k => KeyMatches(k, text));
            return true;
        }

        private static readonly Regex SlashRegex = new Regex(@"^/(.+)/([a-z]*)$", RegexOptions.Singleline);

        private bool KeyMatches(string key, string text)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0)
                return false;
            Match slash = SlashRegex.Match(key);
            if (UseRegex || slash.Success)
            {
                string pattern = slash.Success ? slash.Groups[1].Value : key;
                bool ignoreCase = slash.Success ? slash.Groups[2].Value.Contains("i") : !CaseSensitive;
                try
                {
                    // (.NET 4.0 has no regex timeouts; card patterns are short in practice.)
                    return Regex.IsMatch(text, pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                }
                catch (ArgumentException) { return false; } // bad pattern
            }
            return text.IndexOf(key, CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        private static JToken Value(JToken t) { return t == null || t.Type == JTokenType.Null ? null : t; }

        private static bool Bool(JToken t, bool fallback)
        {
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
        }

        private static int Int(JToken t, int fallback)
        {
            if (t == null)
                return fallback;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                return (int)(double)t;
            int parsed;
            return t.Type == JTokenType.String && int.TryParse((string)t, out parsed) ? parsed : fallback;
        }

        private static List<string> Strings(JToken t)
        {
            if (t is JArray a)
                return a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).Where(s => s.Trim().Length > 0).ToList();
            if (t != null && t.Type == JTokenType.String)
                return CharacterCard.SplitTags((string)t);
            return new List<string>();
        }
    }

    public sealed class Lorebook
    {
        public const int DefaultScanDepth = 4;
        private const int MaxRecursion = 3;

        private readonly JObject _raw;

        public Lorebook() : this(new JObject()) { }

        internal Lorebook(JObject raw)
        {
            _raw = raw;
            Name = (string)raw["name"] ?? "";
            ScanDepth = IntOrZero(raw["scan_depth"]);
            TokenBudget = IntOrZero(raw["token_budget"]);
            RecursiveScanning = raw["recursive_scanning"]?.Type == JTokenType.Boolean && (bool)raw["recursive_scanning"];
            Entries = (raw["entries"] as JArray ?? new JArray()).OfType<JObject>().Select(e => new LoreEntry(e)).ToList();
        }

        public string Name { get; set; }
        /// <summary>How many recent messages are searched for keywords. 0 = default (4).</summary>
        public int ScanDepth { get; set; }
        /// <summary>Most tokens the entries may use. 0 = decided by the app (a quarter of the context window).</summary>
        public int TokenBudget { get; set; }
        public bool RecursiveScanning { get; set; }
        public List<LoreEntry> Entries { get; set; }

        internal JObject ToJObject()
        {
            JObject o = (JObject)_raw.DeepClone();
            if (Name.Length > 0 || o["name"] != null)
                o["name"] = Name;
            if (ScanDepth > 0) o["scan_depth"] = ScanDepth; else o.Remove("scan_depth");
            if (TokenBudget > 0) o["token_budget"] = TokenBudget; else o.Remove("token_budget");
            o["recursive_scanning"] = RecursiveScanning;
            o["extensions"] = o["extensions"] as JObject ?? new JObject();
            o["entries"] = new JArray(Entries.Select((e, i) => e.ToJObject(i)));
            return o;
        }

        /// <summary>
        ///  The entries that apply to the recent messages (newest last), within the token
        ///  budget, in insertion order.
        /// </summary>
        public List<LoreEntry> Activate(IList<string> recentMessages, int tokenBudget)
        {
            int depth = ScanDepth > 0 ? ScanDepth : DefaultScanDepth;
            string scan = string.Join("\n", recentMessages.Skip(Math.Max(0, recentMessages.Count - depth)).ToArray());

            var active = new List<LoreEntry>();
            for (int round = 0; round < (RecursiveScanning ? MaxRecursion : 1); round++)
            {
                var found = Entries.Where(e => !active.Contains(e) && e.Matches(scan)).ToList();
                if (found.Count == 0)
                    break;
                active.AddRange(found);
                scan += "\n" + string.Join("\n", found.Select(e => e.Content).ToArray());
            }

            // Budget: keep the highest priority entries (ties: earlier insertion order).
            var kept = new List<LoreEntry>();
            int used = 0;
            foreach (LoreEntry e in active.OrderByDescending(e => e.Priority).ThenBy(e => e.InsertionOrder))
            {
                int cost = PromptBuilder.EstimateTokens(e.Content);
                if (tokenBudget > 0 && used + cost > tokenBudget)
                    continue;
                used += cost;
                kept.Add(e);
            }
            return kept.OrderBy(e => e.InsertionOrder).ToList();
        }

        private static int IntOrZero(JToken t)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return 0;
            return Math.Max(0, (int)(double)t);
        }
    }
}
