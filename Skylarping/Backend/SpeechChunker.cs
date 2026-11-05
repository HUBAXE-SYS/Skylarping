/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Cuts a reply into pieces for text-to-speech as it's written. With nothing being generated
// the first sentence goes straight out, so the character starts talking as soon as they can.
// While a piece is generating, short sentences are merged: on a slow server a piece on its
// own takes longer to generate than to play, leaving gaps in the speech. Measured:
//   OpenMOSS-TTS: 3-4 s of speech took 5 s to generate, 8 s took 7 s.
//   MOSS-TTS-Local on a GPU: 2 s of speech took 0.9 s, 4.5 s took 1.6 s.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Skylarping.Backend
{
    public sealed class SpeechChunk
    {
        public SpeechChunk(string text, int end)
        {
            Text = text;
            End = end;
        }

        /// <summary>The words to speak, cleaned of anything that shouldn't be read aloud.</summary>
        public string Text { get; private set; }
        /// <summary>Where this piece ends in the reply's text.</summary>
        public int End { get; private set; }
    }

    public sealed class SpeechChunker
    {
        private const int MinCharacters = 90; // about seven seconds of speech
        private const int MinEagerCharacters = 15; // "Oh." on its own isn't worth a request
        private const int MaxCharacters = 240;

        private static readonly Regex Links = new Regex(@"https?://\S+", RegexOptions.Compiled);
        // Markdown, and what text-to-speech would read out by name: emoji (the newer ones are
        // surrogate pairs, Cs; the rest, like hearts and stars, are symbols, So), their joiners (Cf)
        // and variation selectors (FE00-FE0F). The degree sign stays: "20°" should be read as degrees.
        private static readonly Regex Markup = new Regex(@"[*#`~]|(?<!\w)_|_(?!\w)|[\p{So}\p{Cf}\p{Cs}︀-️-[°]]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuation = new Regex(@" (?=[.,;:!?…])", RegexOptions.Compiled);
        private static readonly Regex Speakable = new Regex(@"[\p{L}\p{N}]", RegexOptions.Compiled);
        private static readonly string[] Abbreviations = { "mr", "mrs", "ms", "dr", "st", "vs", "prof", "sr", "jr" };

        private const string SentenceEnds = ".!?…";
        private const string Closers = "\"')]*”’";

        private int _consumed;

        /// <summary>Takes the reply so far and returns the pieces that are ready.</summary>
        /// <param name="final">The reply is complete, so whatever is left is ready too.</param>
        /// <param name="eager">Nothing is being generated, so the next piece needn't wait to be long.</param>
        public IList<SpeechChunk> Update(string text, bool final, bool eager = false)
        {
            var chunks = new List<SpeechChunk>();
            text = text ?? "";
            int end;
            while ((end = NextEnd(text, _consumed, eager && chunks.Count == 0 ? MinEagerCharacters : MinCharacters)) > 0)
                Add(chunks, text, end);
            if (final && _consumed < text.Length)
                Add(chunks, text, text.Length);
            return chunks;
        }

        private void Add(List<SpeechChunk> chunks, string text, int end)
        {
            string spoken = Clean(text.Substring(_consumed, end - _consumed));
            _consumed = end;
            if (spoken.Length > 0)
                chunks.Add(new SpeechChunk(spoken, end));
        }

        /// <summary>
        ///  Removes what would be read out as symbols: markdown, links and emoji. Empty when
        ///  nothing is left to say (an emoji on its own, "...").
        /// </summary>
        public static string Clean(string text)
        {
            text = Links.Replace(text ?? "", " ");
            text = Markup.Replace(text, "");
            text = Spaces.Replace(text, " ").Trim();
            text = SpaceBeforePunctuation.Replace(text, "");
            return Speakable.IsMatch(text) ? text : "";
        }

        // Where the next piece ends, or 0 if it isn't ready.
        private static int NextEnd(string text, int start, int minCharacters)
        {
            int lastBreak = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (i - start >= MaxCharacters) // a sentence that won't end
                    return lastBreak > 0 ? lastBreak : char.IsLowSurrogate(text[i]) ? i + 1 : i; // never through an emoji

                char ch = text[i];
                if (ch == ',' || ch == ';' || ch == ':' || char.IsWhiteSpace(ch))
                    lastBreak = i + 1;

                int end = SentenceEnd(text, i);
                if (end > 0 && end - start >= minCharacters)
                    return end;
            }
            return 0;
        }

        // The end of the sentence stopping at i (past any closing quote), or 0 if none stops there.
        private static int SentenceEnd(string text, int i)
        {
            if (text[i] == '\n')
                return i + 1;
            if (SentenceEnds.IndexOf(text[i]) < 0)
                return 0;

            int after = i + 1;
            while (after < text.Length && (SentenceEnds.IndexOf(text[after]) >= 0 || Closers.IndexOf(text[after]) >= 0))
                after++;
            // Whatever comes next hasn't arrived: this could still be "3.5" or "...".
            if (after >= text.Length || !char.IsWhiteSpace(text[after]))
                return 0;
            return text[i] == '.' && IsAbbreviation(text, i) ? 0 : after;
        }

        private static bool IsAbbreviation(string text, int dot)
        {
            int start = dot;
            while (start > 0 && char.IsLetter(text[start - 1]))
                start--;
            string word = text.Substring(start, dot - start).ToLowerInvariant();
            return word.Length == 1 || Array.IndexOf(Abbreviations, word) >= 0;
        }
    }
}
