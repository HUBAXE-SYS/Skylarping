/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Chatting with characters: one session (chat memory) per character.
//
//  - Memory comes from the profile database, the same history the chat window shows, so
//    it survives a restart and Clear Chat History / Edit / Remove act on both at once.
//    A session loads its history from the database the first time it's used, then keeps
//    it up to date itself. The view model saves every message it's told about, so this
//    class never writes to the database directly.
//  - The prompt is built by Characters.PromptBuilder from the card, About me, the user's
//    current status and the history.
//  - Sending while a reply is being written cancels it and starts a new one that answers
//    everything, like a person who reads your new message before hitting send.
//  - With Options > Chats > "Stream replies" on, the reply appears as it's written: it is
//    posted after the first words, then updated (partial edits, not saved) and finished
//    with a final edit (saved).
//  - Tools (Edit Character > Tools): the character may call SimpleLLMChat tools such as web
//    search and memory while writing a reply. The tool calls themselves are never shown,
//    just a longer "typing"; text the model writes in each round (before and after a tool)
//    is posted as its own message. After Settings.MaxToolRounds (AI Services > Tools) the model is
//    asked once more without tools so it has to answer. A server that rejects the tools
//    parameter (e.g. llama.cpp without --jinja) gets the request again without them.
//  - Messaging first (Edit Character > "Message me on their own"): when a chat has been
//    quiet for a random number of minutes in the character's range, the character writes
//    to the user unprompted. The clock restarts with every message either side sends, and
//    after a call. Never while on a call with them, while a reply is being written, or
//    after MaxUnansweredProactive messages in a row the user hasn't answered (so a night
//    away doesn't leave a pile of messages). Failures are silent: nobody asked for it.
//  - No action text (Edit Character > "No action text"): the prompt asks for
//    no *actions*, and Characters.ActionFilter cuts them out of each reply (while streaming
//    too) before it's posted, so what's saved is what was shown. The greeting is filtered the
//    same way. It's always on while on a call with the character, since *actions* make no
//    sense spoken aloud.
//  - Mood messages (Edit Character > "Update their mood message on their own"): when it's on,
//    a character without a mood message writes one straight away (when added, or when it's
//    switched on), then a new one after a random MoodMinHours..MoodMaxHours each time. When
//    it's off they never write one; any old mood stays as it is. The mood and when the next one is
//    due are saved in status.json in their folder, so it
//    survives a restart. One character at a time, never during a call or a reply. A failure
//    keeps the old mood (or none) and is tried again MoodRetryDelay later, silently.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Skylarping.AI;
using Skylarping.AI.Tools;
using Skylarping.Characters;
using Skylarping.Databases;
using Skylarping.Models;
using Newtonsoft.Json.Linq;
using Skylarping.Preferences;

namespace Skylarping.Backend
{
    public sealed class CharacterChatService : IDisposable
    {
        private const int HistoryLoadLimit = 2000;          // the prompt builder trims to the context window
        private static readonly TimeSpan StreamUpdateInterval = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan ProactiveCheckInterval = TimeSpan.FromSeconds(20);
        public const int MaxUnansweredProactive = 3;
        private static readonly TimeSpan MoodRetryDelay = TimeSpan.FromMinutes(30);
        private const int MoodHistoryTurns = 20;            // recent messages the mood is written from

        private sealed class Session
        {
            public StoredCharacter Character;
            public User User;
            public readonly List<ConversationItem> Items = new List<ConversationItem>();
            public bool Loaded;
            public int Generation;                  // bumped to make a running reply stale
            public CancellationTokenSource Cts;
            public bool Busy;                       // a reply is being written
            public bool InCall;                     // on a call (or ringing) with the user
            public ICallSpeaker Speaker;            // speaks replies out loud; null unless on a call
            public readonly HashSet<string> CutOff = new HashSet<string>(); // interrupted replies: their text is final
            public DateTime NextProactive = DateTime.MaxValue; // when they message first; MaxValue = never
            public int ProactiveMin, ProactiveMax;  // the range NextProactive was drawn from
            public int UnansweredProactive;         // proactive messages since the user last wrote
            public DateTime MoodUpdated = DateTime.MinValue; // when User.Status was written (UTC)
            public DateTime NextMoodTry = DateTime.MinValue; // no new attempt before this (after a failure)
            public DateTime NextMood = DateTime.MinValue;    // when the next mood message is due (UTC)
            public int MoodMin, MoodMax;                     // the range NextMood was drawn from
            public bool MoodRunning;                // a new mood message is being written
        }

        private readonly Func<User> _me;
        private readonly Action<MessageEventArgs> _raiseMessage;
        private readonly Action<DialogRequestEventArgs> _raiseDialog;
        private readonly ObservableCollection<User> _typing;
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly object _lock = new object();
        private static readonly Random Rng = new Random();

        public CharacterChatService(Func<User> me, Action<MessageEventArgs> raiseMessage,
            Action<DialogRequestEventArgs> raiseDialog, ObservableCollection<User> typing)
        {
            _me = me;
            _raiseMessage = raiseMessage;
            _raiseDialog = raiseDialog;
            _typing = typing;
            _ui = SynchronizationContext.Current;
            _proactiveTimer = new Timer(_ => Tick(), null, ProactiveCheckInterval, ProactiveCheckInterval);
        }

        private SynchronizationContext _ui;         // for the typing list, which the UI binds to
        private readonly Timer _proactiveTimer;

        private void RememberUi()
        {
            if (SynchronizationContext.Current != null)
                _ui = SynchronizationContext.Current;
        }

        /// <summary>The signed-in profile's database (attached after sign-in). Chat memory is read from it.</summary>
        public DatabaseManager Database { get; set; }

        #region Characters

        /// <summary>Adds (or refreshes) a character and returns its contact.</summary>
        public DirectMessage Register(StoredCharacter character)
        {
            RememberUi();
            lock (_lock)
            {
                Session session;
                if (!_sessions.TryGetValue(character.Id, out session))
                {
                    session = new Session
                    {
                        User = new User(character.Card.Name, character.Id, character.Id, character.Mood, PresenceStatus.Online, character.Avatar),
                        MoodUpdated = character.Mood == null ? DateTime.MinValue : character.MoodUpdated
                    };
                    _sessions[character.Id] = session;
                    if (character.Mood != null && character.MoodNext != DateTime.MinValue)
                    {
                        session.NextMood = character.MoodNext; // as drawn last time, so a restart doesn't re-roll it
                        session.MoodMin = character.Card.MoodMinHours;
                        session.MoodMax = character.Card.MoodMaxHours;
                    }
                }
                session.Character = character;
                session.User.DisplayName = character.Card.Name;
                session.User.Avatar = character.Avatar;
                // New, just switched on, or a different range: start the clock from now.
                CharacterCard card = character.Card;
                bool scheduled = session.NextProactive != DateTime.MaxValue;
                if (card.ProactiveMessages != scheduled || session.ProactiveMin != card.ProactiveMinMinutes
                    || session.ProactiveMax != card.ProactiveMaxMinutes)
                    ScheduleProactive(session);
                // No saved schedule, or a different range: draw the next mood time again.
                if (session.MoodMin != card.MoodMinHours || session.MoodMax != card.MoodMaxHours)
                {
                    ScheduleMood(session, session.MoodUpdated);
                    if (session.User.Status != null && session.MoodUpdated != DateTime.MinValue)
                    {
                        try
                        {
                            CharacterStore.WriteMood(character.Folder, session.User.Status, session.MoodUpdated, session.NextMood);
                        }
                        catch (Exception)
                        {
                            // only the schedule is lost; it's drawn again next time
                        }
                    }
                }
                return new DirectMessage(session.User, 0, character.Id);
            }
        }

        /// <summary>Tells the service a call with the character started or ended (no proactive messages during one).</summary>
        public void SetInCall(string id, bool inCall)
        {
            lock (_lock)
            {
                Session session;
                if (!_sessions.TryGetValue(id ?? "", out session))
                    return;
                session.InCall = inCall;
                if (!inCall)
                    ScheduleProactive(session); // don't message the moment they hang up
            }
        }

        /// <summary>Starts or stops speaking replies out loud. Replies are only ever spoken on a call.</summary>
        public void SetSpeaker(string id, ICallSpeaker speaker)
        {
            lock (_lock)
            {
                Session session;
                if (_sessions.TryGetValue(id ?? "", out session))
                    session.Speaker = speaker;
            }
        }

        /// <summary>The call just connected: the character says hello rather than leave it silent.</summary>
        public void AnswerCall(string id)
        {
            Session session = Get(id);
            if (session == null)
                return;
            RememberUi();
            Reply(session, _ui, CallGreetingNudge);
        }

        /// <summary>
        /// The user cut the character off: the replies become what was actually heard, so the
        /// character doesn't remember saying the rest. Safe from any thread.
        /// </summary>
        public void Interrupted(string id, IDictionary<string, string> heard)
        {
            Session session = Get(id);
            if (session == null)
                return;
            SynchronizationContext ui = _ui;
            if (ui != null)
                ui.Post(_ => KeepOnlyHeard(session, heard), null);
            else
                KeepOnlyHeard(session, heard);
        }

        private void KeepOnlyHeard(Session session, IDictionary<string, string> heard)
        {
            if (heard == null || heard.Count == 0)
                return;
            var edited = new List<Message>();
            var removed = new List<string>();
            lock (_lock)
            {
                StopReply(session);
                foreach (KeyValuePair<string, string> reply in heard)
                {
                    session.CutOff.Add(reply.Key);
                    int index = session.Items.FindIndex(i => i.Identifier == reply.Key);
                    var old = index < 0 ? null : session.Items[index] as Message;
                    if (old == null)
                        continue;
                    if (reply.Value.Length == 0)
                    {
                        session.Items.RemoveAt(index);
                        removed.Add(reply.Key);
                    }
                    else
                    {
                        Message updated = Copy(old, reply.Value);
                        session.Items[index] = updated;
                        edited.Add(updated);
                    }
                }
            }
            SetTyping(SynchronizationContext.Current, session.User, false);
            foreach (Message message in edited)
                _raiseMessage(new MessageEditedEventArgs(session.Character.Id, message.Identifier, message));
            foreach (string messageId in removed)
                _raiseMessage(new MessageDeletedEventArgs(session.Character.Id, messageId));
        }

        /// <summary>Forgets a removed character: stops any reply and drops its session.</summary>
        public void Remove(string id)
        {
            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session))
                    return;
                StopReply(session);
                _sessions.Remove(id);
            }
            SetTyping(SynchronizationContext.Current, session.User, false);
        }

        public bool Handles(string conversationId)
        {
            lock (_lock)
                return conversationId != null && _sessions.ContainsKey(conversationId);
        }

        public StoredCharacter GetCharacter(string id)
        {
            lock (_lock)
            {
                Session session;
                return _sessions.TryGetValue(id ?? "", out session) ? session.Character : null;
            }
        }

        #endregion

        #region History

        private Session Get(string id)
        {
            Session session;
            lock (_lock)
                _sessions.TryGetValue(id ?? "", out session);
            if (session != null)
                EnsureLoaded(session);
            return session;
        }

        private void EnsureLoaded(Session session)
        {
            lock (_lock)
            {
                if (session.Loaded)
                    return;
                session.Loaded = true;
                DatabaseManager db = Database;
                if (db == null)
                    return;
                try
                {
                    var conversation = new DirectMessage(session.User, 0, session.Character.Id);
                    session.Items.AddRange(db.Messages.Read(conversation, HistoryLoadLimit));
                }
                catch (Exception)
                {
                    // Start with no memory rather than not at all.
                }
            }
        }

        public List<ConversationItem> FetchMessages(string conversationId, Fetch fetch, string afterId)
        {
            Session session = Get(conversationId);
            if (session == null)
                return new List<ConversationItem>();
            lock (_lock)
            {
                if (fetch == Fetch.Newest)
                {
                    // A new, empty chat opens with the character's greeting, if they're set to greet.
                    if (session.Items.Count == 0)
                    {
                        Message greeting = MakeGreeting(session);
                        if (greeting != null)
                            session.Items.Add(greeting);
                    }
                    return session.Items.ToList();
                }
                if (fetch == Fetch.NewestAfterIdentifier || fetch == Fetch.AfterIdentifier)
                {
                    int index = session.Items.FindIndex(i => i.Identifier == afterId);
                    return index < 0 ? new List<ConversationItem>() : session.Items.Skip(index + 1).ToList();
                }
                return new List<ConversationItem>(); // older pages come from the database
            }
        }

        private Message MakeGreeting(Session session)
        {
            CharacterCard card = session.Character.Card;
            if (!card.UseGreeting)
                return null;
            var options = new List<string>();
            if (card.FirstMessage.Trim().Length > 0)
                options.Add(card.FirstMessage);
            options.AddRange(card.AlternateGreetings.Where(g => g.Trim().Length > 0));
            if (options.Count == 0)
                return null;
            string text = PromptBuilder.Expand(options[Rng.Next(options.Count)], PromptBuilder.CharName(card), MyName()).Trim();
            if (card.SuppressActions)
                text = ActionFilter.Strip(text);
            if (text.Length == 0)
                return null;
            return new Message(NewId(session), session.User, DateTime.Now, text);
        }

        public void Clear(string conversationId)
        {
            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(conversationId ?? "", out session))
                    return;
                StopReply(session);
                session.Items.Clear();
                session.UnansweredProactive = 0;
                session.Loaded = true; // the database copy is being cleared too
            }
            SetTyping(SynchronizationContext.Current, session.User, false);
        }

        #endregion

        #region Sending, editing, regenerating

        public bool SendMessage(string conversationId, string text, bool action)
        {
            Session session = Get(conversationId);
            if (session == null || string.IsNullOrEmpty(text))
                return false;

            User me = _me();
            Message message = action
                ? new ActionMessage(NewId(session), me, DateTime.Now, text)
                : new Message(NewId(session), me, DateTime.Now, text);
            RememberUi();
            lock (_lock)
            {
                session.Items.Add(message);
                session.UnansweredProactive = 0;
                ScheduleProactive(session);
            }
            _raiseMessage(new MessageReceivedEventArgs(conversationId, message)); // confirms the "sending" preview

            Reply(session, SynchronizationContext.Current);
            return true;
        }

        /// <summary>
        /// Takes what the user said on a call: the same as typing it, so it's saved and answered
        /// through the normal path. Safe from any thread.
        /// </summary>
        public void SendSpoken(string conversationId, string text)
        {
            SynchronizationContext ui = _ui;
            if (ui != null)
                ui.Post(_ => SendMessage(conversationId, text, false), null);
            else
                SendMessage(conversationId, text, false);
        }

        /// <summary>Throws away the character's latest reply and writes a new one.</summary>
        public bool Regenerate(string conversationId)
        {
            Session session = Get(conversationId);
            if (session == null)
                return false;
            var removed = new List<string>();
            bool somethingToAnswer;
            lock (_lock)
            {
                // A greeting with nothing after it isn't a reply to anything; leave it.
                somethingToAnswer = session.Items.OfType<Message>().Any(m => !IsFromCharacter(session, m));
                if (!somethingToAnswer)
                    return false;
                StopReply(session);
                // Drop the character's messages after the user's last message.
                for (int i = session.Items.Count - 1; i >= 0; i--)
                {
                    var m = session.Items[i] as Message;
                    if (m == null)
                        continue;
                    if (!IsFromCharacter(session, m))
                        break;
                    removed.Add(m.Identifier);
                    session.Items.RemoveAt(i);
                }
            }
            foreach (string id in removed)
                _raiseMessage(new MessageDeletedEventArgs(conversationId, id));
            Reply(session, SynchronizationContext.Current);
            return true;
        }

        /// <summary>True if the message is the character's latest reply (what Regenerate replaces).</summary>
        public bool IsLatestReply(string conversationId, string messageId)
        {
            Session session = Get(conversationId);
            if (session == null)
                return false;
            lock (_lock)
            {
                Message last = session.Items.OfType<Message>().LastOrDefault();
                return last != null && IsFromCharacter(session, last) && last.Identifier == messageId
                       && session.Items.OfType<Message>().Any(m => !IsFromCharacter(session, m)); // not a lone greeting
            }
        }

        public bool Edit(string conversationId, string messageId, string text)
        {
            Session session = Get(conversationId);
            if (session == null || string.IsNullOrEmpty(text))
                return false;
            Message updated;
            lock (_lock)
            {
                int index = session.Items.FindIndex(i => i.Identifier == messageId);
                var old = index < 0 ? null : session.Items[index] as Message;
                if (old == null)
                    return false;
                updated = Copy(old, text);
                session.Items[index] = updated;
            }
            _raiseMessage(new MessageEditedEventArgs(conversationId, messageId, updated));
            return true;
        }

        public bool Delete(string conversationId, string messageId)
        {
            Session session = Get(conversationId);
            if (session == null)
                return false;
            lock (_lock)
            {
                int index = session.Items.FindIndex(i => i.Identifier == messageId);
                if (index < 0)
                    return false;
                session.Items.RemoveAt(index);
            }
            _raiseMessage(new MessageDeletedEventArgs(conversationId, messageId));
            return true;
        }

        private static Message Copy(Message old, string text)
        {
            Message copy = old is ActionMessage
                ? new ActionMessage(old.Identifier, old.Author, old.Time, text)
                : new Message(old.Identifier, old.Author, old.Time, text);
            copy.ConversationId = old.ConversationId;
            return copy;
        }

        #endregion

        #region Replying

        private void StopReply(Session session)
        {
            session.Generation++;
            session.Busy = false;
            if (session.Cts != null)
            {
                session.Cts.Cancel();
                session.Cts = null;
            }
        }

        /// <param name="nudge">
        ///  For a proactive message: an out-of-character note added as the last user turn of the
        ///  request only (never shown or saved). Errors are then not reported.
        /// </param>
        private void Reply(Session session, SynchronizationContext ui, string nudge = null)
        {
            // Before the prompt is built: it has to see what was really said.
            ICallSpeaker speaker = session.Speaker;
            if (speaker != null)
                KeepOnlyHeard(session, speaker.Interrupt());

            CancellationTokenSource cts = new CancellationTokenSource();
            int generation;
            PromptInput input;
            bool proactive = nudge != null;
            bool suppress;
            lock (_lock)
            {
                StopReply(session);
                session.Cts = cts;
                session.Busy = true;
                generation = session.Generation;
                input = Snapshot(session);
                suppress = session.Character.Card.SuppressActions || session.InCall;
                if (proactive)
                    input.Turns.Add(new ChatTurn(false, nudge));
            }
            Action<string> report = text =>
            {
                if (!proactive)
                    Warn(text);
            };
            SetTyping(ui, session.User, true);

            TaskEx.Run(() =>
            {
                Message reply = null;
                bool typing = true;
                try
                {
                    if (!Settings.LlmConfigured)
                    {
                        report("No language model is set up yet, so " + session.User.DisplayName + " can't reply. " +
                             "Choose Tools > Connection Wizard... to set one up.");
                        return;
                    }

                    CharacterTools tools = CharacterTools.For(input.Character);
                    JArray schema = tools == null ? null : tools.Schema;
                    List<LlmChatMessage> request = BuildRequest(input, tools, suppress, cts.Token);
                    LlmEndpoint endpoint = LlmEndpoint.FromSettings();
                    DateTime lastUpdate = DateTime.MinValue;

                    Action<string> onText = null;
                    if (Settings.StreamReplies || speaker != null) // a spoken reply can't wait for the whole text
                    {
                        onText = text =>
                        {
                            if (suppress)
                                text = ActionFilter.Strip(text, true);
                            if (text.Length == 0 || !IsCurrent(session, generation))
                                return;
                            if (typing)
                            {
                                SetTyping(ui, session.User, false);
                                typing = false;
                            }
                            if (reply == null)
                            {
                                reply = new Message(NewId(session), session.User, DateTime.Now, text);
                                if (!Post(session, generation, reply))
                                    return;
                                lastUpdate = DateTime.UtcNow;
                            }
                            else if (DateTime.UtcNow - lastUpdate >= StreamUpdateInterval)
                            {
                                reply = Update(session, reply, text, true) ?? reply;
                                lastUpdate = DateTime.UtcNow;
                            }
                            speaker?.Speak(reply.Identifier, text, false);
                        };
                    }

                    // Every round's text is its own message: what the character says before using a
                    // tool shows up straight away, and what it says after the tool is a follow-up,
                    // like someone sending a second message unprompted.
                    LlmChatResult result;
                    bool postedAny = false, onlyActions = false;
                    int round = 0, maxRounds = Settings.MaxToolRounds;
                    while (true)
                    {
                        JArray roundTools = round < maxRounds ? schema : null;
                        try
                        {
                            result = Ask(endpoint, request, onText, cts.Token, roundTools);
                        }
                        catch (LlmException ex)
                        {
                            if (roundTools == null || round > 0 || cts.IsCancellationRequested
                                || (ex.Kind != LlmErrorKind.ServerError && ex.Kind != LlmErrorKind.NotFound))
                                throw;
                            // The server may not support tools: answer without them.
                            schema = null;
                            result = Ask(endpoint, request, onText, cts.Token, null);
                        }

                        string roundText = result.Content ?? "";
                        if (suppress)
                        {
                            string spoken = ActionFilter.Strip(roundText);
                            onlyActions |= spoken.Length == 0 && roundText.Trim().Length > 0;
                            roundText = spoken;
                        }
                        if (reply != null)
                        {
                            // Finish (and save) this round's streamed message, even if a newer message interrupted it.
                            string finished = roundText.Length > 0 ? roundText : reply.Text;
                            Update(session, reply, finished, false);
                            if (IsCurrent(session, generation))
                                speaker?.Speak(reply.Identifier, finished, true);
                            reply = null; // the next round streams into a new message
                            postedAny = true;
                        }
                        else if (roundText.Length > 0)
                        {
                            var whole = new Message(NewId(session), session.User, DateTime.Now, roundText);
                            if (Post(session, generation, whole))
                            {
                                speaker?.Speak(whole.Identifier, roundText, true);
                                postedAny = true;
                            }
                        }

                        if (roundTools == null || result.ToolCalls.Count == 0 || cts.IsCancellationRequested
                            || !IsCurrent(session, generation))
                            break;

                        request.Add(LlmChatMessage.FromAssistantToolCalls(result.Content, result.ToolCalls));
                        if (!typing)
                        {
                            // Still "typing" while the tool runs and the follow-up is written.
                            SetTyping(ui, session.User, true);
                            typing = true;
                        }
                        foreach (LlmToolCall call in result.ToolCalls)
                        {
                            ToolOutput output = tools.Run(call, cts.Token);
                            request.Add(LlmChatMessage.FromTool(call.Id, output.Text, output.ImageBase64, output.ImageMime));
                        }
                        round++;
                    }

                    if (postedAny && proactive)
                        lock (_lock)
                            session.UnansweredProactive++;
                    if (postedAny || !IsCurrent(session, generation))
                        return; // done, or superseded by a newer message, regenerate or clear
                    report(onlyActions
                        ? session.User.DisplayName + "'s reply was only *actions*, which are hidden (No action text). Try Regenerate Reply."
                        : result.FinishReason == "length"
                        ? session.User.DisplayName + " ran out of reply length before answering. The model may be spending it all on thinking."
                        : "The model sent " + session.User.DisplayName + " an empty reply. Try Regenerate Reply.");
                }
                catch (OperationCanceledException)
                {
                    // superseded while a tool was running
                }
                catch (LlmException ex)
                {
                    if (!IsCurrent(session, generation))
                        return;
                    string what = ex.Kind == LlmErrorKind.Unreachable || ex.Kind == LlmErrorKind.InvalidUrl
                        ? "Couldn't reach your language model. "
                        : "Your language model didn't answer. ";
                    report(session.User.DisplayName + " couldn't reply. " + what + ConnectionTest.Explain(ex) +
                         " You can check the settings in Tools > AI Services.");
                }
                catch (Exception ex)
                {
                    if (IsCurrent(session, generation))
                        report(session.User.DisplayName + " couldn't reply: " + ex.Message);
                }
                finally
                {
                    if (IsCurrent(session, generation))
                    {
                        SetTyping(ui, session.User, false);
                        lock (_lock)
                        {
                            session.Busy = false;
                            ScheduleProactive(session); // the clock starts when they stop writing
                        }
                    }
                }
            });
        }

        private static LlmChatResult Ask(LlmEndpoint endpoint, List<LlmChatMessage> request, Action<string> onText,
            CancellationToken cancel, JArray tools)
        {
            return onText != null
                ? OpenAICompatClient.ChatStream(endpoint, request, onText, cancel, tools: tools)
                : OpenAICompatClient.Chat(endpoint, request, tools: tools);
        }

        /// <summary>What a reply is written from, copied under the lock so the prompt can be built off the UI thread.</summary>
        private sealed class PromptInput
        {
            public StoredCharacter Character;
            public List<ChatTurn> Turns;
            public UserProfile Profile;
        }

        private PromptInput Snapshot(Session session)
        {
            var turns = new List<ChatTurn>();
            foreach (Message m in session.Items.OfType<Message>())
            {
                if (string.IsNullOrEmpty(m.Text))
                    continue;
                bool fromCharacter = IsFromCharacter(session, m);
                string text = m is ActionMessage
                    ? "*" + (fromCharacter ? "{{char}}" : "{{user}}") + " " + m.Text + "*"
                    : m.Text;
                turns.Add(new ChatTurn(fromCharacter, text));
            }

            User me = _me();
            return new PromptInput
            {
                Character = session.Character,
                Turns = turns,
                Profile = new UserProfile
                {
                    Name = MyName(),
                    AboutMe = Settings.ProfileAboutMe,
                    Presence = me == null ? null : DescribePresence(me.ConnectionStatus),
                    Mood = me?.Status
                },
            };
        }

        private static List<LlmChatMessage> BuildRequest(PromptInput input, CharacterTools tools, bool suppressActions,
            CancellationToken cancel)
        {
            List<string> toolContext = null;
            int toolTokens = 0;
            if (tools != null)
            {
                toolContext = tools.ContextInjections(cancel);
                toolTokens = PromptBuilder.EstimateTokens(tools.Schema.ToString(Newtonsoft.Json.Formatting.None));
            }
            return PromptBuilder.Build(input.Character.Card, input.Profile, input.Turns, Settings.LlmContextWindow,
                toolContext, toolTokens, suppressActions);
        }

        private static string DescribePresence(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.Online:
                case PresenceStatus.OnlineMobile: return "Online";
                case PresenceStatus.Away:
                case PresenceStatus.AwayMobile: return "Away";
                case PresenceStatus.DoNotDisturb:
                case PresenceStatus.DoNotDisturbMobile: return "Do Not Disturb";
                case PresenceStatus.Invisible: return "Invisible (appearing offline to others)";
                default: return null;
            }
        }

        private bool IsCurrent(Session session, int generation)
        {
            lock (_lock)
                return session.Generation == generation;
        }

        private bool Post(Session session, int generation, Message message)
        {
            lock (_lock)
            {
                if (session.Generation != generation)
                    return false;
                session.Items.Add(message);
            }
            _raiseMessage(new MessageReceivedEventArgs(session.Character.Id, message));
            return true;
        }

        /// <summary>Replaces a posted message's text. Returns the new message, or null if it's gone (e.g. cleared).</summary>
        private Message Update(Session session, Message message, string text, bool partial)
        {
            Message updated;
            lock (_lock)
            {
                int index = session.Items.FindIndex(i => i.Identifier == message.Identifier);
                if (index < 0 || session.CutOff.Contains(message.Identifier))
                    return null;
                updated = Copy((Message)session.Items[index], text);
                session.Items[index] = updated;
            }
            _raiseMessage(new MessageEditedEventArgs(session.Character.Id, message.Identifier, updated, partial));
            return updated;
        }

        #endregion

        private void Warn(string text)
        {
            _raiseDialog(new DialogRequestEventArgs(DialogType.Warning, text));
        }

        private static bool IsFromCharacter(Session session, Message m)
        {
            return m.Author != null && m.Author.Identifier == session.Character.Id;
        }

        private string MyName()
        {
            User me = _me();
            return me == null ? "User" : me.DisplayName;
        }

        private static string NewId(Session session)
        {
            return session.Character.Id + "/" + Guid.NewGuid().ToString("N");
        }

        private void SetTyping(SynchronizationContext ui, User user, bool typing)
        {
            if (ui == null)
                return;
            ui.Post(_ =>
            {
                if (typing && !_typing.Contains(user))
                    _typing.Add(user);
                else if (!typing)
                    _typing.Remove(user);
            }, null);
        }

        #region Messaging first

        private static void ScheduleProactive(Session session)
        {
            CharacterCard card = session.Character.Card;
            session.ProactiveMin = card.ProactiveMinMinutes;
            session.ProactiveMax = card.ProactiveMaxMinutes;
            if (!card.ProactiveMessages)
            {
                session.NextProactive = DateTime.MaxValue;
                return;
            }
            int min = Math.Max(1, Math.Min(card.ProactiveMinMinutes, card.ProactiveMaxMinutes));
            int max = Math.Max(min, Math.Max(card.ProactiveMinMinutes, card.ProactiveMaxMinutes));
            double minutes;
            lock (Rng)
                minutes = min + Rng.NextDouble() * (max - min);
            session.NextProactive = DateTime.UtcNow.AddMinutes(minutes);
        }

        private void Tick()
        {
            if (Database == null || _me() == null)
                return; // not signed in yet
            try
            {
                CheckMoods();
            }
            catch (Exception)
            {
                // never let the timer thread die
            }
            CheckProactive();
        }

        /// <summary>Runs on a timer: starts a message from every character whose quiet time is up.</summary>
        private void CheckProactive()
        {
            var due = new List<Session>();
            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                if (_disposed)
                    return;
                foreach (Session session in _sessions.Values)
                {
                    if (now < session.NextProactive)
                        continue;
                    if (session.InCall || session.Busy || session.UnansweredProactive >= MaxUnansweredProactive
                        || !Settings.LlmConfigured)
                    {
                        ScheduleProactive(session); // try again after another quiet spell
                        continue;
                    }
                    session.NextProactive = DateTime.MaxValue; // rescheduled when the reply finishes
                    due.Add(session);
                }
            }
            foreach (Session session in due)
            {
                try
                {
                    EnsureLoaded(session);
                    string nudge;
                    lock (_lock)
                    {
                        if (session.InCall || session.Busy || !_sessions.ContainsKey(session.Character.Id))
                        {
                            ScheduleProactive(session);
                            continue;
                        }
                        nudge = ProactiveNudge(session);
                    }
                    Reply(session, _ui, nudge);
                }
                catch (Exception)
                {
                    lock (_lock)
                        ScheduleProactive(session);
                }
            }
        }

        private const string CallGreetingNudge =
            "[Note, not from {{user}}: {{user}} just called {{char}} and {{char}} picked up. Write what {{char}} says " +
            "on answering, in one short spoken line. Write only that line and don't mention this note.]";

        private static string ProactiveNudge(Session session)
        {
            Message last = session.Items.OfType<Message>().LastOrDefault();
            string quiet = last == null ? "You haven't talked with {{user}} yet."
                : "It has been " + DescribeDuration(DateTime.Now - (last.Time.Kind == DateTimeKind.Utc ? last.Time.ToLocalTime() : last.Time)) + " since the last message.";
            return "[Note, not from {{user}}: " + quiet + " {{user}} hasn't written anything new. " +
                   "Write {{char}}'s next message to {{user}} on {{char}}'s own initiative, the way {{char}} would " +
                   "message someone unprompted: follow up on something, share what's on {{char}}'s mind, or start " +
                   "a new topic. Write only the message itself and don't mention this note.]";
        }

        private static string DescribeDuration(TimeSpan span)
        {
            if (span.TotalMinutes < 2)
                return "a minute";
            if (span.TotalHours < 2)
                return (int)span.TotalMinutes + " minutes";
            if (span.TotalDays < 2)
                return (int)span.TotalHours + " hours";
            return (int)span.TotalDays + " days";
        }

        #endregion

        #region Mood messages

        /// <summary>
        ///  If the character updates their mood message and doesn't have one yet, they write one
        ///  now (after being added, or after it's switched on in Edit Character).
        /// </summary>
        public void EnsureMood(string id)
        {
            Session session;
            lock (_lock)
            {
                _sessions.TryGetValue(id ?? "", out session);
                if (session == null || !session.Character.Card.UpdateMood || session.MoodUpdated != DateTime.MinValue)
                    return;
            }
            StartMood(session);
        }

        /// <summary>Runs on a timer: one character whose mood message is old enough writes a new one.</summary>
        private void CheckMoods()
        {
            if (!Settings.LlmConfigured)
                return;
            DateTime now = DateTime.UtcNow;
            Session oldest = null;
            lock (_lock)
            {
                if (_disposed || _sessions.Values.Any(s => s.MoodRunning))
                    return; // one at a time, so a slow local server isn't flooded at sign-in
                foreach (Session session in _sessions.Values)
                {
                    if (session.InCall || session.Busy || now < session.NextMoodTry)
                        continue;
                    // Only if they update it: due, or no mood yet (writing one failed, so try again).
                    if (!session.Character.Card.UpdateMood
                        || (session.MoodUpdated != DateTime.MinValue && now < session.NextMood))
                        continue;
                    if (oldest == null || session.MoodUpdated < oldest.MoodUpdated)
                        oldest = session;
                }
            }
            if (oldest != null)
                StartMood(oldest);
        }

        private void StartMood(Session session)
        {
            if (!Settings.LlmConfigured)
                return;
            EnsureLoaded(session);
            PromptInput input;
            lock (_lock)
            {
                if (session.MoodRunning || _disposed)
                    return;
                session.MoodRunning = true;
                session.NextMoodTry = DateTime.UtcNow + MoodRetryDelay; // in case this fails
                input = Snapshot(session);
                if (input.Turns.Count > MoodHistoryTurns)
                    input.Turns.RemoveRange(0, input.Turns.Count - MoodHistoryTurns);
                input.Turns.Add(new ChatTurn(false, PromptBuilder.MoodNote(session.User.Status)));
            }

            TaskEx.Run(() =>
            {
                try
                {
                    CharacterCard card = input.Character.Card;
                    List<LlmChatMessage> request = PromptBuilder.Build(card, input.Profile, input.Turns,
                        Settings.LlmContextWindow, suppressActions: card.SuppressActions);
                    LlmChatResult result = OpenAICompatClient.Chat(LlmEndpoint.FromSettings(), request);
                    string mood = PromptBuilder.CleanMood(result.Content, card.SuppressActions);
                    if (mood == null)
                        return;
                    DateTime now = DateTime.UtcNow;
                    lock (_lock)
                    {
                        Session current;
                        if (_disposed || !_sessions.TryGetValue(input.Character.Id, out current) || current != session)
                            return; // removed meanwhile
                        session.MoodUpdated = now;
                        ScheduleMood(session, now);
                        CharacterStore.WriteMood(session.Character.Folder, mood, now, session.NextMood);
                    }
                    SetMood(session.User, mood);
                }
                catch (Exception)
                {
                    // Keep the old mood (or none); CheckMoods tries again after MoodRetryDelay.
                }
                finally
                {
                    lock (_lock)
                        session.MoodRunning = false;
                }
            });
        }

        /// <summary>Draws when the next mood message is due: a random MoodMinHours..MoodMaxHours after 'from'.</summary>
        private static void ScheduleMood(Session session, DateTime from)
        {
            CharacterCard card = session.Character.Card;
            session.MoodMin = card.MoodMinHours;
            session.MoodMax = card.MoodMaxHours;
            if (from == DateTime.MinValue)
            {
                session.NextMood = DateTime.MinValue; // no mood yet: due now
                return;
            }
            int min = Math.Max(1, Math.Min(card.MoodMinHours, card.MoodMaxHours));
            int max = Math.Max(min, Math.Max(card.MoodMinHours, card.MoodMaxHours));
            double hours;
            lock (Rng)
                hours = min + Rng.NextDouble() * (max - min);
            session.NextMood = from.AddHours(hours);
        }

        private void SetMood(User user, string mood)
        {
            SynchronizationContext ui = _ui;
            if (ui == null)
                user.Status = mood;
            else
                ui.Post(_ => user.Status = mood, null);
        }

        #endregion

        private bool _disposed;

        public void Dispose()
        {
            _proactiveTimer.Dispose();
            lock (_lock)
            {
                _disposed = true;
                foreach (Session session in _sessions.Values)
                    StopReply(session);
            }
        }
    }
}
