/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Group chats: a session whose members are other characters' sessions.
//
//  - A cycle is the replies set off by one event, run one at a time through the normal reply
//    path, as the character speaking. Their tools, memories and "No action text" all apply.
//    After a message from the user: everyone named, otherwise one or two members weighted by
//    talkativeness. Any message from the user ends the cycle and starts a new one.
//  - Someone named by a character replies too, after a random delay. The chain limit caps
//    those per cycle; none by default.
//  - The unprompted loop: when the chat has been quiet for a random time, one or two members
//    speak, then the quiet starts again, up to the group's repeats. Do Not Disturb stops it,
//    along with chains; Away doesn't. A new message from the user resets the count.
//  - A chat's own history is all that's shared here: only saved memories cross between chats.
//    The one exception is mood messages, which see a little of each group (see MoodContext).
//  - Each member has a short summary so the others know who they are. It's written from the card
//    and kept in it, so it's exported too. Any change to what it was written from replaces it.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Skylarping.AI;
using Skylarping.Characters;
using Skylarping.Models;

namespace Skylarping.Backend
{
    public sealed partial class CharacterChatService
    {
        private const int MoodContextMessages = 12;   // from each group, for a mood message

        private sealed class GroupTurn
        {
            public Session Member;
            public bool Delayed;
            public string Nudge;
            public bool Silent; // nobody asked for it, so a failure isn't reported
        }

        private sealed class GroupCycle
        {
            public readonly Queue<GroupTurn> Turns = new Queue<GroupTurn>();
            public int Chained;
        }

        #region Groups

        /// <summary>Adds (or refreshes) a group, and returns its conversation.</summary>
        public Group RegisterGroup(StoredGroup group)
        {
            RememberUi();
            lock (_lock)
            {
                Session session;
                if (!_sessions.TryGetValue(group.Id, out session))
                {
                    session = new Session
                    {
                        Id = group.Id,
                        Group = group,
                        Variables = new MacroVariables(VariablesFile(group.Folder))
                    };
                    _sessions[group.Id] = session;
                }
                session.Group = group;
                if (session.Cycle == null)
                    ScheduleUnprompted(session);
                return new Group(group.Name, group.Id, 0, MemberUsers(session));
            }
        }

        public StoredGroup GetGroup(string id)
        {
            lock (_lock)
            {
                Session session;
                return _sessions.TryGetValue(id ?? "", out session) ? session.Group : null;
            }
        }

        /// <summary>Saves a group's changes, and says in the chat who joined or left.</summary>
        public Group UpdateGroup(StoredGroup updated)
        {
            List<User> joined, left;
            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(updated.Id ?? "", out session) || session.Group == null)
                    return null;
                EnsureLoaded(session);
                List<string> before = session.Group.MemberIds;
                joined = updated.MemberIds.Except(before).Select(UserOf).Where(u => u != null).ToList();
                left = before.Except(updated.MemberIds).Select(UserOf).Where(u => u != null).ToList();
            }
            Group group = RegisterGroup(updated);
            GroupStore.Save(updated);
            foreach (User user in left)
                PostNotice(session, user, false);
            foreach (User user in joined)
                PostNotice(session, user, true);
            return group;
        }

        /// <summary>Forgets a removed group.</summary>
        public void RemoveGroup(string id)
        {
            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session) || session.Group == null)
                    return;
                session.Cycle = null;
                StopReply(session);
                _sessions.Remove(id);
            }
            ClearTyping(session, SynchronizationContext.Current);
        }

        // A removed character leaves their groups, which say so in the chat. Called before the
        // character's session goes.
        private void LeaveGroups(Session character)
        {
            List<Session> groups;
            lock (_lock)
                groups = _sessions.Values.Where(s => s.Group != null && s.Group.MemberIds.Contains(character.Id)).ToList();
            foreach (Session session in groups)
            {
                lock (_lock)
                {
                    EnsureLoaded(session);
                    session.Group.MemberIds.Remove(character.Id);
                }
                try
                {
                    GroupStore.Save(session.Group);
                }
                catch (Exception)
                {
                    // Their id stays in the file, and is skipped from now on.
                }
                PostNotice(session, character.User, false);
            }
        }

        private void PostNotice(Session session, User who, bool joined)
        {
            var notice = new GroupNotice(NewId(session), who, joined, DateTime.Now);
            lock (_lock)
                session.Items.Add(notice);
            _raiseMessage(new MessageReceivedEventArgs(session.Id, notice));
        }

        private User UserOf(string id)
        {
            Session member;
            return _sessions.TryGetValue(id, out member) ? member.User : null;
        }

        private List<Session> Members(Session session)
        {
            var members = new List<Session>();
            foreach (string id in session.Group.MemberIds)
            {
                Session member;
                if (_sessions.TryGetValue(id, out member) && member.Character != null)
                    members.Add(member);
            }
            return members;
        }

        private User[] MemberUsers(Session session)
        {
            return Members(session).Select(m => m.User).ToArray();
        }

        private List<GroupMember> GroupMembers(IEnumerable<Session> members)
        {
            return members.Select(m => new GroupMember(m.Character.Id, m.Character.Card)).ToList();
        }

        private void ClearTyping(Session session, SynchronizationContext ui)
        {
            if (session.Group == null)
            {
                SetTyping(ui, session.User, false);
                return;
            }
            List<User> users;
            lock (_lock)
                users = Members(session).Select(m => m.User).ToList();
            foreach (User user in users)
                SetTyping(ui, user, false);
        }

        private GroupPrompt GroupPromptFor(Session session, Session speaker)
        {
            List<Session> members = Members(session);
            return new GroupPrompt
            {
                Name = session.Group.Name,
                Members = members.Select(m => PromptBuilder.CharName(m.Character.Card)).ToList(),
                Others = members.Where(m => m != speaker).Select(m => new GroupPeer
                {
                    Name = PromptBuilder.CharName(m.Character.Card),
                    Mood = m.User.Status,
                    Summary = PromptBuilder.SummaryOf(m.Character.Card, MyName())
                }).ToList()
            };
        }

        #endregion

        #region Cycles

        private void AnswerInGroup(Session session, Message message, SynchronizationContext ui)
        {
            var cycle = new GroupCycle();
            lock (_lock)
            {
                session.UnpromptedRuns = 0;
                List<Session> members = Available(session);
                List<string> named = GroupTurns.Mentioned(message.Text, GroupMembers(members));
                List<string> chosen = named.Count > 0 ? named : GroupTurns.Pick(GroupMembers(members), Rng);
                foreach (string id in chosen)
                    cycle.Turns.Enqueue(new GroupTurn { Member = _sessions[id] });
            }
            StartCycle(session, ui, cycle);
        }

        // Members who can talk right now: anyone on a call with the user is busy.
        private List<Session> Available(Session session)
        {
            return Members(session).Where(m => !m.InCall).ToList();
        }

        private void StartCycle(Session session, SynchronizationContext ui, GroupCycle cycle)
        {
            lock (_lock)
            {
                StopReply(session);
                session.Cycle = cycle;
                session.NextUnprompted = DateTime.MaxValue;
            }
            ClearTyping(session, ui);
            NextTurn(session, ui, cycle);
        }

        private void NextTurn(Session session, SynchronizationContext ui, GroupCycle cycle)
        {
            GroupTurn turn;
            lock (_lock)
            {
                if (session.Cycle != cycle || _disposed)
                    return;
                turn = cycle.Turns.Count > 0 ? cycle.Turns.Dequeue() : null;
                if (turn == null || (turn.Silent && DoNotDisturb()))
                {
                    session.Cycle = null;
                    ScheduleUnprompted(session);
                    return;
                }
            }

            int delay = turn.Delayed ? RandomDelay(session.Group) : 0;
            if (delay <= 0)
            {
                RunTurn(session, ui, cycle, turn);
                return;
            }
            TaskEx.Delay(delay).ContinueWith(_ => RunTurn(session, ui, cycle, turn));
        }

        private static int RandomDelay(StoredGroup group)
        {
            int min = Math.Max(0, Math.Min(group.DelayMinSeconds, group.DelayMaxSeconds));
            int max = Math.Max(min, Math.Max(group.DelayMinSeconds, group.DelayMaxSeconds));
            lock (Rng)
                return (int)((min + Rng.NextDouble() * (max - min)) * 1000);
        }

        private void RunTurn(Session session, SynchronizationContext ui, GroupCycle cycle, GroupTurn turn)
        {
            lock (_lock)
            {
                if (session.Cycle != cycle || _disposed)
                    return;
                if (!session.Group.MemberIds.Contains(turn.Member.Id) || turn.Member.InCall)
                    turn = null; // left the group during the wait
            }
            if (turn == null)
            {
                NextTurn(session, ui, cycle);
                return;
            }
            Reply(session, ui, turn.Nudge, turn.Member, turn.Silent,
                said => AfterTurn(session, ui, cycle, turn.Member, said));
        }

        private void AfterTurn(Session session, SynchronizationContext ui, GroupCycle cycle, Session member, string said)
        {
            lock (_lock)
            {
                if (session.Cycle != cycle)
                    return;
                int limit = session.Group.ChainLimit;
                foreach (string id in GroupTurns.Mentioned(said, GroupMembers(Available(session)), member.Id))
                {
                    if (limit > 0 && cycle.Chained >= limit)
                        break;
                    if (cycle.Turns.Any(t => t.Member.Id == id))
                        continue;
                    cycle.Chained++;
                    cycle.Turns.Enqueue(new GroupTurn { Member = _sessions[id], Delayed = true, Silent = true });
                }
            }
            NextTurn(session, ui, cycle);
        }

        private bool DoNotDisturb()
        {
            User me = _me();
            return me != null && (me.ConnectionStatus == PresenceStatus.DoNotDisturb
                || me.ConnectionStatus == PresenceStatus.DoNotDisturbMobile);
        }

        #endregion

        #region The unprompted loop

        private static void ScheduleUnprompted(Session session)
        {
            StoredGroup group = session.Group;
            if (!group.Unprompted || (group.Repeats > 0 && session.UnpromptedRuns >= group.Repeats))
            {
                session.NextUnprompted = DateTime.MaxValue;
                return;
            }
            int min = Math.Max(1, Math.Min(group.QuietMinMinutes, group.QuietMaxMinutes));
            int max = Math.Max(min, Math.Max(group.QuietMinMinutes, group.QuietMaxMinutes));
            double minutes;
            lock (Rng)
                minutes = min + Rng.NextDouble() * (max - min);
            session.NextUnprompted = DateTime.UtcNow.AddMinutes(minutes);
        }

        /// <summary>Starts a round in every group whose quiet time is up at the given time (UTC).</summary>
        public void CheckUnprompted(DateTime now)
        {
            if (DoNotDisturb() || !Hooks.Configured())
                return; // still due: it starts when Do Not Disturb ends
            var due = new List<KeyValuePair<Session, GroupCycle>>();
            lock (_lock)
            {
                if (_disposed)
                    return;
                foreach (Session session in _sessions.Values.Where(s => s.Group != null).ToList())
                {
                    if (now < session.NextUnprompted || session.Cycle != null || session.Busy)
                        continue;
                    EnsureLoaded(session);
                    List<Session> members = Available(session);
                    if (members.Count == 0)
                    {
                        ScheduleUnprompted(session);
                        continue;
                    }
                    session.UnpromptedRuns++;
                    var cycle = new GroupCycle();
                    string nudge = GroupQuietNudge(session);
                    foreach (string id in GroupTurns.Pick(GroupMembers(members), Rng))
                        cycle.Turns.Enqueue(new GroupTurn { Member = _sessions[id], Delayed = true, Silent = true, Nudge = nudge });
                    due.Add(new KeyValuePair<Session, GroupCycle>(session, cycle));
                }
            }
            foreach (KeyValuePair<Session, GroupCycle> round in due)
                StartCycle(round.Key, _ui, round.Value);
        }

        private static string GroupQuietNudge(Session session)
        {
            Message last = session.Items.OfType<Message>().LastOrDefault();
            string quiet = last == null ? "Nobody has said anything in this group chat yet."
                : "It has been " + DescribeDuration(DateTime.Now - (last.Time.Kind == DateTimeKind.Utc ? last.Time.ToLocalTime() : last.Time)) + " since the last message.";
            return "[Note, not from anyone in the chat: " + quiet + " Write {{char}}'s next message to the group on " +
                   "{{char}}'s own initiative: follow up on something, share what's on {{char}}'s mind, or start a " +
                   "new topic. Write only the message itself and don't mention this note.]";
        }

        #endregion

        #region Summaries

        /// <summary>
        ///  Writes the summary of a character who has none or whose card has changed, then the next one:
        ///  one at a time, in the background, so a slow local server isn't flooded.
        /// </summary>
        public void RefreshSummaries()
        {
            Session next;
            lock (_lock)
            {
                if (_summaryRunning || _disposed || !Hooks.Configured())
                    return;
                DateTime now = DateTime.UtcNow;
                next = _sessions.Values.FirstOrDefault(s => s.Character != null && s.Character.Card.NeedsGroupSummary && now >= s.NextSummaryTry);
                if (next == null)
                    return;
                _summaryRunning = true;
                next.NextSummaryTry = now + MoodRetryDelay; // in case this fails
            }

            StoredCharacter character = next.Character;
            CharacterCard card = character.Card;
            string writtenFrom = card.GroupSummaryFingerprint;
            string name = MyName();
            TaskEx.Run(() =>
            {
                try
                {
                    string text = AskUsingTools(Hooks.Endpoint(), PromptBuilder.SummaryRequest(card, name), null, null, 0);
                    string summary = PromptBuilder.CleanSummary(text);
                    if (summary != null)
                    {
                        lock (_lock)
                        {
                            if (!_disposed && card.GroupSummaryFingerprint == writtenFrom)
                            {
                                card.GroupSummary = summary;
                                card.GroupSummarySource = writtenFrom;
                                if (character.Folder != null)
                                    CharacterStore.WriteCard(character.Folder, card);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Nobody asked for it: the card's own text stands in, and it's tried again later.
                }
                finally
                {
                    lock (_lock)
                        _summaryRunning = false;
                }
                RefreshSummaries();
            });
        }

        #endregion

        #region Mood messages

        // A few recent messages from each of a character's groups, busiest first, for their mood
        // message. They're the first thing to go when the context window runs short.
        private List<string> MoodContext(Session member)
        {
            var sections = new List<KeyValuePair<DateTime, string>>();
            foreach (Session session in _sessions.Values.Where(s => s.Group != null && s.Group.MemberIds.Contains(member.Id)).ToList())
            {
                EnsureLoaded(session);
                List<Message> recent = session.Items.OfType<Message>().ToList();
                if (recent.Count == 0)
                    continue;
                DateTime lastTime = recent[recent.Count - 1].Time;
                var lines = recent.Skip(Math.Max(0, recent.Count - MoodContextMessages))
                                  .Where(m => !string.IsNullOrEmpty(m.Text))
                                  .Select(m => (m.Author?.DisplayName ?? "Someone") + ": " + m.Text);
                sections.Add(new KeyValuePair<DateTime, string>(lastTime,
                    "Recent messages in the group chat \"" + session.Group.Name + "\", which " +
                    PromptBuilder.CharName(member.Character.Card) + " is in:\n" + string.Join("\n", lines.ToArray())));
            }
            return sections.OrderByDescending(s => s.Key).Select(s => s.Value).ToList();
        }

        #endregion
    }
}
