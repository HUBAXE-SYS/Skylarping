/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// No action text (Edit Character > "No action text", and always during a
// call): the character talks like a person on an instant messenger, without *smiles warmly*
// style narration.
//
//  - The model is asked not to write actions (Instruction, added to the system message).
//  - Whatever it writes anyway is cut out of its replies: italic spans in Markdown,
//    *like this* or _like this_. Bold (**...**) and code are left alone, and so is
//    "2 * 3 * 4", because Markdown only starts italics at a "*" followed by a non-space.
//  - The character's older messages and the card's example dialogue are filtered too when
//    the prompt is built, so the model isn't shown the habit it's being asked to drop.
//  - The user's own messages (including /me actions) are never touched.
/*==========================================================*/

using System.Text.RegularExpressions;

namespace Skylarping.Characters
{
    public static class ActionFilter
    {
        public const string Instruction =
            "Write only what {{char}} types in the chat, like a real person messaging {{user}}. " +
            "Do not narrate actions, gestures, facial expressions, thoughts or surroundings, " +
            "and do not use asterisks or italics for them (no *smiles*, *waves*, *leans closer*).";

        private const char Gap = '\u0001'; // marks where a span was cut out, until the gaps are tidied

        // Code is matched first so it can be kept as-is. An italic span starts at a lone "*" or "_"
        // followed by a non-space and ends at a lone one preceded by a non-space, within a paragraph.
        // "_" must also sit outside a word, so snake_case_names survive.
        private static readonly Regex Spans = new Regex(
            @"(?<code>```[\s\S]*?(?:```|$)|`[^`\n]*`)" +
            @"|(?<!\*)\*(?![\s*])(?:[^*\n]|\n(?![ \t]*\n))*?(?<![\s*])\*(?!\*)" +
            @"|(?<!\w)_(?![\s_])(?:[^_\n]|\n(?![ \t]*\n))*?(?<![\s_])_(?!\w)",
            RegexOptions.Compiled);

        // While streaming: an italic span that has been opened but not closed yet.
        private static readonly Regex Unclosed = new Regex(@"(?<![*\w])[*_](?![\s*_])[^*_]*$", RegexOptions.Compiled);

        // A line that was nothing but an action (maybe with leftover punctuation).
        private static readonly Regex GapLine = new Regex(
            @"^[ \t]*\u0001(?:[ \t]*\u0001)*[ \t]*[.,;:!?]*[ \t]*(?:\n|$)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // A cut inside a line, with the spaces around it and a comma etc. right after it.
        private static readonly Regex GapRun = new Regex(
            @"[ \t]*\u0001(?:[ \t]*\u0001)*[ \t]*(?<punct>[,;:](?=[ \t]|$)[ \t]*)?",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        ///  Removes italic (action) spans from a character's message. With partial set (a reply that
        ///  is still being streamed), a span that has been opened but not closed yet is hidden too.
        /// </summary>
        public static string Strip(string text, bool partial = false)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf('*') < 0 && text.IndexOf('_') < 0))
                return text ?? "";
            string stripped = Spans.Replace(CharacterCard.NormalizeNewlines(text),
                m => m.Groups["code"].Success ? m.Value : Gap.ToString());
            if (partial && Regex.Matches(stripped, "```").Count % 2 == 0)
                stripped = Unclosed.Replace(stripped, Gap.ToString());
            return Tidy(stripped);
        }

        /// <summary>Closes the gaps the removed spans leave, without touching the rest (code indentation etc.).</summary>
        private static string Tidy(string text)
        {
            if (text.IndexOf(Gap) < 0)
                return text;
            string lines = GapLine.Replace(text, "");
            string joined = GapRun.Replace(lines, m =>
            {
                bool lineStart = m.Index == 0 || lines[m.Index - 1] == '\n';
                int after = m.Index + m.Length;
                bool lineEnd = after >= lines.Length || lines[after] == '\n';
                string punct = m.Groups["punct"].Success ? m.Groups["punct"].Value.Trim() : "";
                if (lineStart)
                    return "";                                          // "*nods*, sure" -> "sure"
                if (lineEnd)
                    return punct;
                if (punct.Length > 0)
                    return punct + " ";                                 // "Okay *nods*, sure" -> "Okay, sure"
                return ".!?".IndexOf(lines[after]) >= 0 ? "" : " ";     // "Hi *waves*!" -> "Hi!"
            });
            return Regex.Replace(joined, @"\n{3,}", "\n\n").Trim();
        }
    }
}
