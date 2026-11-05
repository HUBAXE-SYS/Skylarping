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
            IList<string> toolContext = null, int reservedTokens = 0, bool suppressActions = false, MacroContext macros = null)
        {
            string charName = CharName(card);
            string userName = UserName(user);
            history = history ?? new ChatTurn[0];

            if (macros == null)
                macros = new MacroContext { History = history };
            macros.ContextWindow = contextWindow;

            List<LoreEntry> lore = ActiveLore(card, history, contextWindow, charName, userName);
            string system = BuildSystem(card, user, charName, userName, lore, toolContext, suppressActions, macros);
            var messages = new List<LlmChatMessage> { LlmChatMessage.FromSystem(system) };

            if (suppressActions)
                history = history
                    .Select(t => t.FromCharacter ? new ChatTurn(true, ActionFilter.Strip(t.Text)) : t)
                    .Where(t => t.Text.Length > 0)
                    .ToList();

            List<ChatTurn> kept = SelectHistory(history, contextWindow, EstimateTokens(system) + Math.Max(0, reservedTokens));
            foreach (ChatTurn turn in MergeSameSide(kept))
            {
                string text = Expand(turn.Text, charName, userName);
                messages.Add(turn.FromCharacter ? LlmChatMessage.FromAssistant(text) : LlmChatMessage.FromUser(text));
            }
            return messages;
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
            IList<LoreEntry> lore = null, IList<string> toolContext = null, bool suppressActions = false, MacroContext macros = null)
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
