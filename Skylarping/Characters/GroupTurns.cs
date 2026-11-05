/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Who speaks next in a group chat: anyone named, otherwise one or two members weighted by their
// talkativeness (SillyTavern's card setting, 0 to 1).
//
// A name counts as a whole word, with or without @, in any case. Each member answers to their
// full name, their nickname, and the first word of their name, since nobody calls Seraphina
// Moonwhisper by her full name.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Skylarping.Characters
{
    public sealed class GroupMember
    {
        public GroupMember(string id, CharacterCard card)
        {
            Id = id;
            Name = card.Name ?? "";
            Nickname = card.Nickname ?? "";
            Talkativeness = card.Talkativeness;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Nickname { get; private set; }
        public double Talkativeness { get; private set; }

        public IEnumerable<string> Names
        {
            get
            {
                var names = new List<string> { Name.Trim(), Nickname.Trim() };
                string[] words = Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 1)
                    names.Add(words[0]);
                return names.Where(n => n.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public static class GroupTurns
    {
        private const double LeastWeight = 0.05; // even the quietest member speaks now and then

        /// <summary>The members named in the text, in the order they're named.</summary>
        /// <param name="exceptId">A member to ignore: nobody is set off by their own name.</param>
        public static List<string> Mentioned(string text, IEnumerable<GroupMember> members, string exceptId = null)
        {
            var found = new List<KeyValuePair<int, string>>();
            foreach (GroupMember member in members)
            {
                if (member.Id == exceptId)
                    continue;
                int first = member.Names.Select(name => FirstIndex(text ?? "", name)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                if (first >= 0)
                    found.Add(new KeyValuePair<int, string>(first, member.Id));
            }
            return found.OrderBy(f => f.Key).Select(f => f.Value).ToList();
        }

        private static int FirstIndex(string text, string name)
        {
            Match match = Regex.Match(text, @"(?<![\p{L}\p{N}_])@?" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase);
            return match.Success ? match.Index : -1;
        }

        /// <summary>Whether a name fits what's typed after an @: its start, or the start of any word in it.</summary>
        public static bool MatchesTag(string name, string query)
        {
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return true;
            return name.Split(' ').Any(word => word.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>One or two members, more likely the more talkative they are.</summary>
        public static List<string> Pick(IEnumerable<GroupMember> members, Random random)
        {
            List<GroupMember> left = members.ToList();
            var picked = new List<string>();
            int count;
            lock (random)
                count = Math.Min(left.Count, random.Next(1, 3));
            while (picked.Count < count)
            {
                double total = left.Sum(Weight);
                double roll;
                lock (random)
                    roll = random.NextDouble() * total;
                GroupMember chosen = left[left.Count - 1];
                foreach (GroupMember member in left)
                {
                    roll -= Weight(member);
                    if (roll <= 0)
                    {
                        chosen = member;
                        break;
                    }
                }
                picked.Add(chosen.Id);
                left.Remove(chosen);
            }
            return picked;
        }

        private static double Weight(GroupMember member)
        {
            return Math.Max(LeastWeight, member.Talkativeness);
        }
    }
}
