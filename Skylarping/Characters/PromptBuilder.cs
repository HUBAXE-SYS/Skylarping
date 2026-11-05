/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Turns a character card + the chat history into the messages sent to the model.
//
//  system:  the card's system prompt (or our default), lorebook entries marked "before",
//           the card's description, personality, scenario (only if the character's
//           "Use a scenario" is on), lorebook entries marked "after", what the character
//           knows about the user (About me + Skype status), the example dialogue as style
//           reference, and post-history instructions.
//  then:    the real chat, newest messages that fit the budget.
//
// Rules decided for Skylarping:
//  - Follow the card as closely as possible: no instant-messenger style override.
//  - Example dialogue goes in the system message as reference text, never as fake
//    user/assistant turns (local models get confused by those).
//  - Budget: about 3 characters per token, like SimpleLLMChat's TokenEstimator; 90% of
//    the context window is used for the prompt, the rest is left for the reply. With no
//    known context window, the last 50 messages are kept.
//  - Post-history instructions are added at the end of the system message rather than
//    as a late system message, because many local chat templates reject a system
//    message anywhere but first.
//  - Consecutive messages from the same side are joined, since some chat templates
//    require user/assistant turns to alternate.
//  - Lorebook entries are matched against the recent messages (see Lorebook). Their budget
//    is the book's token_budget, or a quarter of the context window (1024 tokens if unknown).
//  - {{char}} is the V3 nickname when the card has one, otherwise the name.
//  - The system message goes through Macros (all of SillyTavern's that apply here); chat
//    history only gets the names.
//  - Tool context (e.g. Memory Tools' saved memories) goes near the end of the system
//    message, before post-history instructions, as-is (no macros). The tools schema's size
//    is passed as reservedTokens and comes out of the history budget.
//  - No action text (suppressActions): ActionFilter.Instruction goes in just before the
//    post-history instructions, and *actions* are cut out of the example dialogue and the
//    character's earlier messages. The user's messages are left as they are.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Skylarping.AI;

namespace Skylarping.Characters
{
    /// <summary>One message of the real chat, as the model should see it.</summary>
    public sealed class ChatTurn
    {
        public ChatTurn(bool fromCharacter, string text)
        {
            FromCharacter = fromCharacter;
            Text = text ?? "";
        }

        public bool FromCharacter { get; private set; }
        public string Text { get; private set; }
    }

    /// <summary>What the character knows about the user ({{user}}).</summary>
    public sealed class UserProfile
    {
        public string Name { get; set; }
        public string AboutMe { get; set; }
        /// <summary>Presence as shown in Skype, e.g. "Online", "Away", "Do Not Disturb".</summary>
        public string Presence { get; set; }
        /// <summary>The user's mood message, e.g. "Feeling good!".</summary>
        public string Mood { get; set; }
    }

    /// <summary>Someone else in a group chat, as the speaker knows them.</summary>
    public sealed class GroupPeer
    {
        public string Name;
        public string Mood;
        public string Summary;
    }

    /// <summary>The group chat a reply is written for.</summary>
    public sealed class GroupPrompt
    {
        public string Name;
        public List<string> Members = new List<string>();
        public List<GroupPeer> Others = new List<GroupPeer>();
    }

    public static class PromptBuilder
    {
        public const int CharsPerToken = 3;           // SimpleLLMChat's estimate
        public const double PromptShareOfContext = 0.9;
        public const int MessagesWithoutContextWindow = 50;

        public const string DefaultSystemPrompt =
            "You are {{char}}. Stay in character as {{char}} and write {{char}}'s next message in the conversation with {{user}}.";

        public static int EstimateTokens(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length / CharsPerToken;
        }

        public const int DefaultLoreBudgetWithoutContextWindow = 1024;

        public static List<LlmChatMessage> Build(CharacterCard card, UserProfile user, IList<ChatTurn> history, int contextWindow,
            IList<string> toolContext = null, int reservedTokens = 0, bool suppressActions = false, MacroContext macros = null,
            GroupPrompt group = null, IList<string> optionalContext = null)
        {
            string charName = CharName(card);
            string userName = UserName(user);
            history = history ?? new ChatTurn[0];

            if (macros == null)
                macros = new MacroContext { History = history };
            macros.ContextWindow = contextWindow;

            List<LoreEntry> lore = ActiveLore(card, history, contextWindow, charName, userName);
            string system = BuildSystem(card, user, charName, userName, lore, toolContext, suppressActions, macros, group);

            if (suppressActions)
                history = history
                    .Select(t => t.FromCharacter ? new ChatTurn(true, ActionFilter.Strip(t.Text)) : t)
                    .Where(t => t.Text.Length > 0)
                    .ToList();

            List<ChatTurn> kept = SelectHistory(history, contextWindow, EstimateTokens(system) + Math.Max(0, reservedTokens));
            int spare = contextWindow <= 0 ? int.MaxValue
                : (int)(contextWindow * PromptShareOfContext) - EstimateTokens(system) - Math.Max(0, reservedTokens) - kept.Sum(t => EstimateTokens(t.Text) + 4);
            system = AddOptionalContext(system, optionalContext, spare);

            var messages = new List<LlmChatMessage> { LlmChatMessage.FromSystem(system) };
            foreach (ChatTurn turn in MergeSameSide(kept))
            {
                string text = Expand(turn.Text, charName, userName);
                messages.Add(turn.FromCharacter ? LlmChatMessage.FromAssistant(text) : LlmChatMessage.FromUser(text));
            }
            return messages;
        }

        // Context that's nice to have goes in last, so it never pushes out anything else: what
        // doesn't fit the spare room is left out, from the end of the list.
        private static string AddOptionalContext(string system, IList<string> sections, int spareTokens)
        {
            if (sections == null)
                return system;
            var result = new StringBuilder(system);
            foreach (string section in sections)
            {
                int cost = EstimateTokens(section);
                if (cost > spareTokens)
                    break;
                spareTokens -= cost;
                AppendSection(result, null, section);
            }
            return result.ToString();
        }

        public const int MaxMoodLength = 100;

        /// <summary>The hidden last user turn that asks the character for a new mood message.</summary>
        public static string MoodNote(string current)
        {
            string now = string.IsNullOrEmpty(current)
                ? "{{char}} doesn't have one yet."
                : "{{char}}'s current mood message is: \"" + current + "\".";
            return "[Note, not from {{user}}: {{char}} is setting a new mood message on {{char}}'s messenger profile, " +
                   "the short status line everyone in {{char}}'s contact list sees next to {{char}}'s name. " + now +
                   " Write the new one the way {{char}} would, showing how {{char}} feels or what {{char}} is up to " +
                   "right now. One short line, under 80 characters, no quotes. Reply with only the mood message.]";
        }

        /// <summary>The first line of the reply, without quotes or a "Mood:" label. Null if nothing's left.</summary>
        public static string CleanMood(string text, bool suppressActions)
        {
            if (text == null)
                return null;
            if (suppressActions)
                text = ActionFilter.Strip(text);
            string line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (line == null)
                return null;
            foreach (string label in new[] { "mood message:", "mood:", "status:" })
                if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(label.Length).Trim();
            const string quotes = "\"'`\u201C\u201D\u2018\u2019";
            while (line.Length >= 2 && quotes.IndexOf(line[0]) >= 0 && quotes.IndexOf(line[line.Length - 1]) >= 0)
                line = line.Substring(1, line.Length - 2).Trim();
            if (line.Length > MaxMoodLength)
            {
                int cut = line.LastIndexOf(' ', MaxMoodLength - 3);
                line = line.Substring(0, cut > MaxMoodLength / 2 ? cut : MaxMoodLength - 3).TrimEnd() + "...";
            }
            return line.Length == 0 ? null : line;
        }

        /// <summary>Runs every macro in text written outside a prompt, such as a greeting.</summary>
        public static string ExpandForCard(string text, CharacterCard card, UserProfile user, MacroContext macros)
        {
            return Macros.Expand(text, Describe(macros, card, user, CharName(card), UserName(user)));
        }

        private static MacroContext Describe(MacroContext macros, CharacterCard card, UserProfile user, string charName, string userName)
        {
            macros.Card = card;
            macros.CharName = charName;
            macros.UserName = userName;
            macros.Persona = user?.AboutMe ?? "";
            return macros;
        }

        private static string UserName(UserProfile user)
        {
            return string.IsNullOrEmpty(user?.Name) ? "User" : user.Name;
        }

        #region Group chats

        public const int MaxSummaryLength = 400;
        private const int SummaryFallbackLength = 300;
        private const int MinSentenceLength = 10; // shorter than this, a full stop is probably "Dr."

        /// <summary>How the others in a group know a character: their summary, or what the card has until there is one.</summary>
        public static string SummaryOf(CharacterCard card, string userName)
        {
            string text = card.GroupSummary.Trim();
            if (text.Length == 0)
                text = card.Personality.Trim();
            if (text.Length == 0)
                text = FirstSentences(card.Description, SummaryFallbackLength);
            return Expand(text, CharName(card), userName ?? "User").Replace('\n', ' ').Trim();
        }

        private static string FirstSentences(string text, int length)
        {
            text = CharacterCard.NormalizeNewlines(text).Trim();
            if (text.Length <= length)
                return text;
            string cut = text.Substring(0, length);
            int end = cut.LastIndexOfAny(new[] { '.', '!', '?' });
            if (end >= MinSentenceLength)
                return cut.Substring(0, end + 1);
            int space = cut.LastIndexOf(' ');
            return cut.Substring(0, space > 0 ? space : length) + "...";
        }

        /// <summary>The request that writes a character's summary.</summary>
        public static List<LlmChatMessage> SummaryRequest(CharacterCard card, string userName)
        {
            string name = CharName(card);
            var sb = new StringBuilder();
            AppendSection(sb, "Name: ", name);
            AppendSection(sb, "Description: ", card.Description);
            AppendSection(sb, "Personality: ", card.Personality);
            AppendSection(sb, "Scenario: ", card.Scenario);
            return new List<LlmChatMessage>
            {
                LlmChatMessage.FromSystem("You write short, factual summaries of fictional characters."),
                LlmChatMessage.FromUser(Expand(sb.ToString(), name, userName ?? "User") +
                    "\n\nWrite two or three plain sentences about " + name + ": who they are, what they're like, and how they " +
                    "come across to other people, as someone meeting them would see it. Write in the third person, with " +
                    "no secrets, lists or roleplay. Reply with only the summary.")
            };
        }

        /// <summary>A written summary, tidied up. Null if nothing's left.</summary>
        public static string CleanSummary(string text)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (text.Length > MaxSummaryLength)
                text = FirstSentences(text, MaxSummaryLength);
            return text.Length == 0 ? null : text;
        }

        /// <summary>
        ///  A group reply as the speaker's own words: without their name in front, and cut off where the
        ///  model starts writing for someone else.
        /// </summary>
        public static string CleanGroupReply(string text, string speaker, IEnumerable<string> others)
        {
            text = text ?? "";
            text = Regex.Replace(text, @"^\s*" + Regex.Escape(speaker) + @"\s*:\s*", "", RegexOptions.IgnoreCase);
            string names = string.Join("|", others.Select(Regex.Escape).ToArray());
            if (names.Length == 0)
                return text;
            Match someoneElse = Regex.Match(text, @"(^|\n)\s*(" + names + @")\s*:", RegexOptions.IgnoreCase);
            return someoneElse.Success ? text.Substring(0, someoneElse.Index).TrimEnd() : text;
        }

        private static string DescribeGroup(GroupPrompt group, string charName, string userName)
        {
            var people = new List<string> { userName };
            people.AddRange(group.Others.Select(o => o.Name));
            var sb = new StringBuilder("This is a group chat called \"" + group.Name + "\". " + charName + " is talking with " +
                string.Join(", ", people.ToArray()) + ". Messages from others start with their name. Write only " + charName +
                "'s next message, in " + charName + "'s voice, without writing for anyone else.");
            if (group.Others.Count > 0)
                sb.Append("\n\nOthers in this conversation:");
            foreach (GroupPeer peer in group.Others)
            {
                sb.Append("\n- ").Append(peer.Name);
                if (!string.IsNullOrEmpty(peer.Mood))
                    sb.Append(" (mood: \"").Append(peer.Mood).Append("\")");
                if (!string.IsNullOrEmpty(peer.Summary))
                    sb.Append(": ").Append(peer.Summary);
            }
            return sb.ToString();
        }

        #endregion

        public static string CharName(CharacterCard card)
        {
            if (card.Nickname.Trim().Length > 0)
                return card.Nickname.Trim();
            return string.IsNullOrEmpty(card.Name) ? "Character" : card.Name;
        }

        /// <summary>The lorebook entries that apply to the latest messages.</summary>
        public static List<LoreEntry> ActiveLore(CharacterCard card, IList<ChatTurn> history, int contextWindow,
            string charName, string userName)
        {
            if (!card.UseLorebook || card.Lorebook == null || card.Lorebook.Entries.Count == 0)
                return new List<LoreEntry>();
            int budget = card.Lorebook.TokenBudget > 0 ? card.Lorebook.TokenBudget
                : contextWindow > 0 ? contextWindow / 4
                : DefaultLoreBudgetWithoutContextWindow;
            var texts = history.Select(t => Expand(t.Text, charName, userName)).ToList();
            return card.Lorebook.Activate(texts, budget);
        }

        public static string BuildSystem(CharacterCard card, UserProfile user, string charName, string userName,
            IList<LoreEntry> lore = null, IList<string> toolContext = null, bool suppressActions = false, MacroContext macros = null,
            GroupPrompt group = null)
        {
            lore = lore ?? new LoreEntry[0];
            macros = Describe(macros ?? new MacroContext(), card, user, charName, userName);
            var sb = new StringBuilder();
            string systemPrompt = string.IsNullOrEmpty(card.SystemPrompt.Trim())
                ? DefaultSystemPrompt
                : Regex.Replace(card.SystemPrompt, @"\{\{original\}\}", DefaultSystemPrompt, RegexOptions.IgnoreCase);
            sb.Append(systemPrompt.Trim());

            AppendLore(sb, lore.Where(e => !e.AfterDescription));
            AppendSection(sb, null, card.Description);
            AppendSection(sb, "{{char}}'s personality: ", card.Personality);
            if (card.UseScenario)
                AppendSection(sb, "Scenario: ", card.Scenario);
            AppendLore(sb, lore.Where(e => e.AfterDescription));

            string about = DescribeUser(user);
            if (about.Length > 0)
                AppendSection(sb, "About {{user}}:\n", about);

            string examples = FormatExamples(card.MessageExamples);
            if (suppressActions)
                examples = ActionFilter.Strip(examples);
            if (examples.Length > 0)
                AppendSection(sb, "Examples of how {{char}} talks (style reference only, not part of this conversation):\n", examples);

            var system = new StringBuilder(Macros.Expand(sb.ToString(), macros));
            if (group != null)
                AppendSection(system, null, DescribeGroup(group, charName, userName));
            foreach (string text in toolContext ?? new string[0])
                AppendSection(system, null, text);
            if (suppressActions)
                AppendSection(system, null, Expand(ActionFilter.Instruction, charName, userName));
            AppendSection(system, null, Macros.Expand(card.PostHistoryInstructions, macros));
            return system.ToString();
        }

        private static void AppendLore(StringBuilder sb, IEnumerable<LoreEntry> entries)
        {
            string text = string.Join("\n", entries.Select(e => StripDecorators(e.Content)).Where(c => c.Length > 0).ToArray());
            AppendSection(sb, null, text);
        }

        /// <summary>
        ///  V3 lorebook decorators (@@depth 4, @@activate_only_after 2, @@@fallback ...) are lines at the
        ///  start of an entry's content. They aren't supported yet, but they must not reach the model.
        /// </summary>
        public static string StripDecorators(string content)
        {
            string[] lines = CharacterCard.NormalizeNewlines(content).Split('\n');
            int first = 0;
            while (first < lines.Length && lines[first].TrimStart().StartsWith("@@"))
                first++;
            return string.Join("\n", lines.Skip(first).ToArray()).Trim();
        }

        private static void AppendSection(StringBuilder sb, string label, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return;
            if (sb.Length > 0)
                sb.Append("\n\n");
            if (label != null)
                sb.Append(label);
            sb.Append(text);
        }

        private static string DescribeUser(UserProfile user)
        {
            if (user == null)
                return "";
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(user.AboutMe?.Trim()))
                lines.Add(user.AboutMe.Trim());
            string status = string.IsNullOrEmpty(user.Presence) ? "" : "{{user}}'s status is " + user.Presence;
            if (!string.IsNullOrEmpty(user.Mood?.Trim()))
                status += (status.Length > 0 ? ", with" : "{{user}}'s") + " mood message: \"" + user.Mood.Trim() + "\"";
            if (status.Length > 0)
                lines.Add(status + ".");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        ///  mes_example blocks start with &lt;START&gt;. Keep the text, drop the markers, and
        ///  separate the blocks so they read as separate examples.
        /// </summary>
        public static string FormatExamples(string mesExample)
        {
            string[] blocks = Regex.Split(CharacterCard.NormalizeNewlines(mesExample), @"^\s*<START>\s*$\n?",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var kept = blocks.Select(b => b.Trim()).Where(b => b.Length > 0).ToArray();
            return string.Join("\n\n---\n\n", kept);
        }

        /// <summary>
        ///  Fills in only the name macros: {{char}}, {{user}} and the older &lt;BOT&gt;, &lt;USER&gt;. For
        ///  chat history, which is re-sent with every request: a {{random}} or {{setvar}} in an
        ///  old message mustn't run again each time. Everything else goes through Macros.Expand.
        /// </summary>
        public static string Expand(string text, string charName, string userName)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = Regex.Replace(text, @"\{\{char\}\}|<BOT>|<CHAR>", charName.Replace("$", "$$"), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{\{user\}\}|<USER>", userName.Replace("$", "$$"), RegexOptions.IgnoreCase);
            return text;
        }

        /// <summary>The newest turns that fit; always at least the latest one.</summary>
        public static List<ChatTurn> SelectHistory(IList<ChatTurn> history, int contextWindow, int systemTokens)
        {
            var kept = new List<ChatTurn>();
            if (history.Count == 0)
                return kept;
            if (contextWindow <= 0)
                return history.Skip(Math.Max(0, history.Count - MessagesWithoutContextWindow)).ToList();

            int budget = (int)(contextWindow * PromptShareOfContext) - systemTokens;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                int cost = EstimateTokens(history[i].Text) + 4; // + a little for the role wrapper
                if (kept.Count > 0 && cost > budget)
                    break;
                budget -= cost;
                kept.Insert(0, history[i]);
            }
            return kept;
        }

        private static IEnumerable<ChatTurn> MergeSameSide(List<ChatTurn> turns)
        {
            ChatTurn pending = null;
            foreach (ChatTurn turn in turns)
            {
                if (pending != null && pending.FromCharacter == turn.FromCharacter)
                {
                    pending = new ChatTurn(turn.FromCharacter, pending.Text + "\n" + turn.Text);
                    continue;
                }
                if (pending != null)
                    yield return pending;
                pending = turn;
            }
            if (pending != null)
                yield return pending;
        }
    }
}
