/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A SillyTavern-style character card (Character Card V1/V2/V3).
//
//  - V1 (TavernAI): a flat object with name, description, personality, scenario,
//    first_mes, mes_example. Even older Pygmalion-era exports used char_name,
//    char_persona, world_scenario, char_greeting and example_dialogue.
//  - V2: { "spec": "chara_card_v2", "spec_version": "2.0", "data": { ... } }
//    https://github.com/malfoyslastname/character-card-spec-v2
//  - V3: { "spec": "chara_card_v3", "spec_version": "3.0", "data": { ... } }, a superset of V2
//    https://github.com/kwaroran/character-card-spec-v3
//
// Only the fields the editor shows are typed properties. Everything else in "data"
// (character_book, extensions, assets, nickname, ...) is kept as-is and written back
// on save, so a card survives import -> edit -> save without losing anything.
// Text keeps SillyTavern macros like {{char}} and {{user}} unexpanded; they are
// filled in when the prompt is built, not here.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Skylarping.Characters
{
    public enum CardSpec
    {
        /// <summary>Made in Skylarping, not imported.</summary>
        None,
        V1,
        V2,
        V3
    }

    public class CharacterCardException : Exception
    {
        public CharacterCardException(string message) : base(message) { }
        public CharacterCardException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class CharacterCard
    {
        public const string SpecV2 = "chara_card_v2";
        public const string SpecV3 = "chara_card_v3";
        /// <summary>Our own settings live in data.extensions.skylarping (the spec's place for app data).</summary>
        public const string ExtensionKey = "skylarping";

        // The card's "data" object as loaded, including fields we don't edit.
        private JObject _data = new JObject();

        public string Name { get; set; } = "";
        /// <summary>Main persona text: who they are, appearance, background. Most cards put nearly everything here.</summary>
        public string Description { get; set; } = "";
        /// <summary>Short personality summary.</summary>
        public string Personality { get; set; } = "";
        public string Scenario { get; set; } = "";
        /// <summary>first_mes: the greeting the character opens a new chat with.</summary>
        public string FirstMessage { get; set; } = "";
        /// <summary>mes_example: example dialogue, blocks separated by &lt;START&gt;.</summary>
        public string MessageExamples { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string PostHistoryInstructions { get; set; } = "";
        public string CreatorNotes { get; set; } = "";
        public string Creator { get; set; } = "";
        public string CharacterVersion { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> AlternateGreetings { get; set; } = new List<string>();

        /// <summary>
        ///  Whether a new chat opens with the character's greeting (first_mes, or one of the
        ///  alternate greetings). Off by default: Skylarping chats are open-ended, not scenes.
        ///  Saved as data.extensions.skylarping.use_greeting; other apps ignore it.
        /// </summary>
        public bool UseGreeting { get; set; }

        /// <summary>
        ///  Whether the card's scenario is sent to the model. Off by default: characters are
        ///  companions, not a scene. Saved as data.extensions.skylarping.use_scenario.
        /// </summary>
        public bool UseScenario { get; set; }

        /// <summary>
        ///  Whether the character messages the user on their own when the chat has been quiet
        ///  for a random ProactiveMinMinutes..ProactiveMaxMinutes. Off by default. Saved as
        ///  data.extensions.skylarping.proactive_messages (+ proactive_min_minutes/_max_minutes).
        ///  They stop after ProactiveMaxUnanswered messages in a row the user hasn't answered
        ///  (proactive_max_unanswered).
        /// </summary>
        public bool ProactiveMessages { get; set; }
        public int ProactiveMinMinutes { get; set; } = DefaultProactiveMinMinutes;
        public int ProactiveMaxMinutes { get; set; } = DefaultProactiveMaxMinutes;
        public const int DefaultProactiveMinMinutes = 30;
        public const int DefaultProactiveMaxMinutes = 120;
        public int ProactiveMaxUnanswered { get; set; } = DefaultProactiveMaxUnanswered;
        public const int DefaultProactiveMaxUnanswered = 3;

        /// <summary>
        ///  Whether the character writes a new mood message (the status line under their name) on
        ///  their own, after a random MoodMinHours..MoodMaxHours each time, and one straight away
        ///  if they have none. On by default. Off = they never write one. Saved as data.extensions.skylarping.update_mood
        ///  (+ mood_min_hours/_max_hours). The mood itself is in status.json, not the card.
        /// </summary>
        public bool UpdateMood { get; set; } = true;
        public int MoodMinHours { get; set; } = DefaultMoodMinHours;
        public int MoodMaxHours { get; set; } = DefaultMoodMaxHours;
        public const int DefaultMoodMinHours = 4;
        public const int DefaultMoodMaxHours = 8;

        /// <summary>
        ///  No action text: the character is asked not to write *actions*, and italic spans are
        ///  cut out of their replies (see ActionFilter). Off by default. Always on during a call,
        ///  whatever this says. Saved as data.extensions.skylarping.suppress_actions.
        /// </summary>
        public bool SuppressActions { get; set; }

        /// <summary>The format the card was read from (None for a card made here).</summary>
        public CardSpec SourceSpec { get; private set; }

        /// <summary>True if the card carries a lorebook (character_book). It is kept, but not used yet.</summary>
        public bool HasLorebook => Lorebook != null && Lorebook.Entries.Count > 0;

        /// <summary>The card's lorebook (character_book), or null if it has none.</summary>
        public Lorebook Lorebook { get; set; }

        /// <summary>
        ///  Whether lorebook entries are added to the prompt. On by default (unlike greeting and
        ///  scenario, a lorebook is just knowledge). Saved as data.extensions.skylarping.use_lorebook.
        /// </summary>
        public bool UseLorebook { get; set; } = true;

        /// <summary>
        ///  The SimpleLLMChat-compatible tools (function names, e.g. run_web_search, save_memory)
        ///  this character may use. Empty by default. Tool use is never shown in the chat.
        ///  Saved as data.extensions.skylarping.tools.
        /// </summary>
        public List<string> Tools { get; set; } = new List<string>();

        /// <summary>
        ///  V3 nickname: when set, {{char}} means the nickname instead of the name. Not editable
        ///  yet; kept from the card.
        /// </summary>
        public string Nickname => (string)_data["nickname"] ?? "";

        #region Reading

        public static CharacterCard FromJson(string json)
        {
            JObject root;
            try
            {
                root = JsonConvert.DeserializeObject<JToken>(json, new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None // keep dates exactly as written
                }) as JObject;
            }
            catch (JsonException ex)
            {
                throw new CharacterCardException("The character card isn't valid JSON.", ex);
            }
            if (root == null)
                throw new CharacterCardException("The character card isn't a JSON object.");
            return FromJObject(root);
        }

        public static CharacterCard FromJObject(JObject root)
        {
            var card = new CharacterCard();
            string spec = (string)root["spec"];
            JObject data = root["data"] as JObject;

            if (data != null && (spec == SpecV3 || spec == SpecV2 || spec == null))
            {
                card.SourceSpec = spec == SpecV3 ? CardSpec.V3 : CardSpec.V2;
                card._data = (JObject)data.DeepClone();
            }
            else if (spec != null && spec != SpecV2 && spec != SpecV3 && data != null)
            {
                // A future spec; the V2 field names have stayed stable, so read it as V3.
                card.SourceSpec = CardSpec.V3;
                card._data = (JObject)data.DeepClone();
            }
            else
            {
                card.SourceSpec = CardSpec.V1;
                card._data = new JObject();
            }

            // V2/V3 cards usually mirror the V1 fields at the top level too; fall back to them
            // (and to the Pygmalion-era names) whenever "data" is missing a field.
            card.Name = Text(card._data, root, "name", "char_name");
            card.Description = Text(card._data, root, "description", "char_persona");
            card.Personality = Text(card._data, root, "personality");
            card.Scenario = Text(card._data, root, "scenario", "world_scenario");
            card.FirstMessage = Text(card._data, root, "first_mes", "char_greeting");
            card.MessageExamples = Text(card._data, root, "mes_example", "example_dialogue");
            card.SystemPrompt = Text(card._data, null, "system_prompt");
            card.PostHistoryInstructions = Text(card._data, null, "post_history_instructions");
            card.CreatorNotes = Text(card._data, root, "creator_notes", "creatorcomment");
            card.Creator = Text(card._data, null, "creator");
            card.CharacterVersion = Text(card._data, null, "character_version");
            card.Tags = Strings(card._data["tags"] ?? root["tags"]);
            card.AlternateGreetings = Strings(card._data["alternate_greetings"]);
            card.UseGreeting = ReadFlag(card._data, "use_greeting");
            card.UseScenario = ReadFlag(card._data, "use_scenario");
            card.ProactiveMessages = ReadFlag(card._data, "proactive_messages");
            card.SuppressActions = ReadFlag(card._data, "suppress_actions");
            card.ProactiveMinMinutes = ReadMinutes(card._data, "proactive_min_minutes", DefaultProactiveMinMinutes);
            card.ProactiveMaxMinutes = ReadMinutes(card._data, "proactive_max_minutes", DefaultProactiveMaxMinutes);
            card.ProactiveMaxUnanswered = ReadMinutes(card._data, "proactive_max_unanswered", DefaultProactiveMaxUnanswered);
            ReadMoodSchedule(card);
            JToken useLore = card._data.SelectToken("extensions." + ExtensionKey + ".use_lorebook");
            card.UseLorebook = useLore == null || useLore.Type != JTokenType.Boolean || (bool)useLore;
            card.Tools = Strings(card._data.SelectToken("extensions." + ExtensionKey + ".tools") as JArray)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (card._data["character_book"] is JObject book)
                card.Lorebook = new Lorebook(book);

            if (string.IsNullOrEmpty(card.Name) && string.IsNullOrEmpty(card.Description) &&
                string.IsNullOrEmpty(card.Personality) && string.IsNullOrEmpty(card.MessageExamples))
                throw new CharacterCardException("This file doesn't look like a character card (no name, description or personality).");
            return card;
        }

        private static string Text(JObject data, JObject root, params string[] keys)
        {
            foreach (JObject source in new[] { data, root })
            {
                if (source == null)
                    continue;
                foreach (string key in keys)
                {
                    JToken token = source[key];
                    if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Object && token.Type != JTokenType.Array)
                    {
                        string value = (string)token;
                        if (!string.IsNullOrEmpty(value))
                            return NormalizeNewlines(value);
                    }
                }
            }
            return "";
        }

        /// <summary>Reads one of our data.extensions.skylarping flags; missing or not a bool = off.</summary>
        private static bool ReadFlag(JObject data, string name)
        {
            JToken token = data.SelectToken("extensions." + ExtensionKey + "." + name);
            return token != null && token.Type == JTokenType.Boolean && (bool)token;
        }

        /// <summary>Reads a whole number of minutes (at least 1) from our extensions, or the default.</summary>
        private static void ReadMoodSchedule(CharacterCard card)
        {
            JToken update = card._data.SelectToken("extensions." + ExtensionKey + ".update_mood");
            JToken legacy = card._data.SelectToken("extensions." + ExtensionKey + ".mood_hours");
            if (update == null && legacy != null && (legacy.Type == JTokenType.Integer || legacy.Type == JTokenType.Float))
            {
                // Early builds had one fixed interval, mood_hours (0 = never).
                int hours = (int)Math.Max(0, Math.Min(int.MaxValue, (double)legacy));
                card.UpdateMood = hours > 0;
                card.MoodMinHours = card.MoodMaxHours = hours > 0 ? hours : DefaultMoodMinHours;
                if (hours == 0)
                    card.MoodMaxHours = DefaultMoodMaxHours;
                return;
            }
            card.UpdateMood = update == null || update.Type != JTokenType.Boolean || (bool)update;
            card.MoodMinHours = ReadMinutes(card._data, "mood_min_hours", DefaultMoodMinHours);
            card.MoodMaxHours = ReadMinutes(card._data, "mood_max_hours", DefaultMoodMaxHours);
        }

        private static int ReadMinutes(JObject data, string name, int fallback)
        {
            JToken token = data.SelectToken("extensions." + ExtensionKey + "." + name);
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                return fallback;
            double value = (double)token;
            return value >= 1 && value <= int.MaxValue ? (int)value : fallback;
        }

        private static List<string> Strings(JToken token)
        {
            if (token is JArray array)
                return array.Where(t => t.Type == JTokenType.String)
                            .Select(t => NormalizeNewlines((string)t))
                            .Where(s => s.Length > 0).ToList();
            // Some old exports store tags as one comma-separated string.
            if (token != null && token.Type == JTokenType.String)
                return SplitTags((string)token);
            return new List<string>();
        }

        public static List<string> SplitTags(string text)
        {
            return (text ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)
                               .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Cards use \n; Windows text boxes give back \r\n.</summary>
        public static string NormalizeNewlines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        }

        #endregion

        #region Writing

        /// <summary>
        ///  Serializes the card. V3 cards stay V3; everything else is written as V2 (which every
        ///  SillyTavern-compatible app reads), with the V1 fields mirrored at the top level as
        ///  SillyTavern itself does.
        /// </summary>
        public JObject ToJObject()
        {
            JObject data = (JObject)_data.DeepClone();
            data["name"] = Name ?? "";
            data["description"] = NormalizeNewlines(Description);
            data["personality"] = NormalizeNewlines(Personality);
            data["scenario"] = NormalizeNewlines(Scenario);
            data["first_mes"] = NormalizeNewlines(FirstMessage);
            data["mes_example"] = NormalizeNewlines(MessageExamples);
            data["creator_notes"] = NormalizeNewlines(CreatorNotes);
            data["system_prompt"] = NormalizeNewlines(SystemPrompt);
            data["post_history_instructions"] = NormalizeNewlines(PostHistoryInstructions);
            data["alternate_greetings"] = new JArray(AlternateGreetings.Select(NormalizeNewlines));
            data["tags"] = new JArray(Tags);
            data["creator"] = Creator ?? "";
            data["character_version"] = CharacterVersion ?? "";
            if (!(data["extensions"] is JObject))
                data["extensions"] = new JObject();
            JObject extensions = (JObject)data["extensions"];
            if (!(extensions[ExtensionKey] is JObject))
                extensions[ExtensionKey] = new JObject();
            extensions[ExtensionKey]["use_greeting"] = UseGreeting;
            extensions[ExtensionKey]["use_scenario"] = UseScenario;
            extensions[ExtensionKey]["use_lorebook"] = UseLorebook;
            extensions[ExtensionKey]["proactive_messages"] = ProactiveMessages;
            extensions[ExtensionKey]["suppress_actions"] = SuppressActions;
            extensions[ExtensionKey]["proactive_min_minutes"] = ProactiveMinMinutes;
            extensions[ExtensionKey]["proactive_max_minutes"] = ProactiveMaxMinutes;
            extensions[ExtensionKey]["proactive_max_unanswered"] = ProactiveMaxUnanswered;
            extensions[ExtensionKey]["update_mood"] = UpdateMood;
            ((JObject)extensions[ExtensionKey]).Remove("voice"); // cards saved before voices were samples only
            extensions[ExtensionKey]["mood_min_hours"] = MoodMinHours;
            extensions[ExtensionKey]["mood_max_hours"] = MoodMaxHours;
            ((JObject)extensions[ExtensionKey]).Remove("mood_hours");
            extensions[ExtensionKey]["tools"] = new JArray((Tools ?? new List<string>()).ToArray());
            if (Lorebook != null)
                data["character_book"] = Lorebook.ToJObject();
            else
                data.Remove("character_book");

            bool v3 = SourceSpec == CardSpec.V3;
            if (v3)
            {
                // Required in V3; fill in anything a sloppy exporter left out.
                if (!(data["group_only_greetings"] is JArray))
                    data["group_only_greetings"] = new JArray();
                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                data["modification_date"] = now;
            }

            return new JObject
            {
                ["spec"] = v3 ? SpecV3 : SpecV2,
                ["spec_version"] = v3 ? "3.0" : "2.0",
                ["name"] = data["name"],
                ["description"] = data["description"],
                ["personality"] = data["personality"],
                ["scenario"] = data["scenario"],
                ["first_mes"] = data["first_mes"],
                ["mes_example"] = data["mes_example"],
                ["data"] = data
            };
        }

        public string ToJson() { return ToJObject().ToString(Newtonsoft.Json.Formatting.Indented); }

        #endregion

        public static string DescribeSpec(CardSpec spec)
        {
            switch (spec)
            {
                case CardSpec.V1: return "Character Card V1";
                case CardSpec.V2: return "Character Card V2";
                case CardSpec.V3: return "Character Card V3";
                default: return "Skylarping";
            }
        }
    }
}
