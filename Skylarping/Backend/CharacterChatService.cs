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
//    search and memory while writing a reply. Tool use is never shown: no messages, nothing
//    saved, just a longer "typing". Only the text of the model's last round is the reply;
//    text written before a tool call ("let me check...") shows while streaming and is
//    replaced, or kept if the last round says nothing. After Settings.MaxToolRounds (AI Services > Tools) the model is
//    asked once more without tools so it has to answer. A server that rejects the tools
//    parameter (e.g. llama.cpp without --jinja) gets the request again without them.
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
    internal sealed class CharacterChatService : IDisposable
    {
        private const int HistoryLoadLimit = 2000;          // the prompt builder trims to the context window
        private static readonly TimeSpan StreamUpdateInterval = TimeSpan.FromMilliseconds(150);

        private sealed class Session
        {
            public StoredCharacter Character;
            public User User;
            public readonly List<ConversationItem> Items = new List<ConversationItem>();
            public bool Loaded;
            public int Generation;                  // bumped to make a running reply stale
            public CancellationTokenSource Cts;
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
        }

        /// <summary>The signed-in profile's database (attached after sign-in). Chat memory is read from it.</summary>
        public DatabaseManager Database { get; set; }

        #region Characters

        /// <summary>Adds (or refreshes) a character and returns its contact.</summary>
        public DirectMessage Register(StoredCharacter character)
        {
            lock (_lock)
            {
                Session session;
                if (!_sessions.TryGetValue(character.Id, out session))
                {
                    session = new Session
                    {
                        User = new User(character.Card.Name, character.Id, character.Id, null, PresenceStatus.Online, character.Avatar)
                    };
                    _sessions[character.Id] = session;
                }
                session.Character = character;
                session.User.DisplayName = character.Card.Name;
                session.User.Avatar = character.Avatar;
                return new DirectMessage(session.User, 0, character.Id);
            }
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
            lock (_lock)
                session.Items.Add(message);
            _raiseMessage(new MessageReceivedEventArgs(conversationId, message)); // confirms the "sending" preview

            Reply(session, SynchronizationContext.Current);
            return true;
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
            if (session.Cts != null)
            {
                session.Cts.Cancel();
                session.Cts = null;
            }
        }

        private void Reply(Session session, SynchronizationContext ui)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            int generation;
            PromptInput input;
            lock (_lock)
            {
                StopReply(session);
                session.Cts = cts;
                generation = session.Generation;
                input = Snapshot(session);
            }
            SetTyping(ui, session.User, true);

            TaskEx.Run(() =>
            {
                Message reply = null;
                bool typing = true;
                try
                {
                    if (!Settings.LlmConfigured)
                    {
                        Warn("No language model is set up yet, so " + session.User.DisplayName + " can't reply. " +
                             "Choose Tools > Connection Wizard... to set one up.");
                        return;
                    }

                    CharacterTools tools = CharacterTools.For(input.Character);
                    JArray schema = tools == null ? null : tools.Schema;
                    List<LlmChatMessage> request = BuildRequest(input, tools, cts.Token);
                    LlmEndpoint endpoint = LlmEndpoint.FromSettings();
                    DateTime lastUpdate = DateTime.MinValue;

                    Action<string> onText = null;
                    if (Settings.StreamReplies)
                    {
                        onText = text =>
                        {
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
                        };
                    }

                    LlmChatResult result;
                    string earlierText = null;  // text from rounds that ended in tool calls
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

                        if (roundTools == null || result.ToolCalls.Count == 0 || cts.IsCancellationRequested
                            || !IsCurrent(session, generation))
                            break;

                        if (!string.IsNullOrEmpty(result.Content))
                            earlierText = result.Content;
                        request.Add(LlmChatMessage.FromAssistantToolCalls(result.Content, result.ToolCalls));
                        if (!typing && reply == null)
                        {
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

                    string final = result.Content ?? "";
                    if (final.Length == 0 && earlierText != null)
                        final = earlierText;
                    if (reply != null)
                    {
                        // Finish (and save) the streamed message, even if a newer message interrupted it.
                        Update(session, reply, final.Length > 0 ? final : reply.Text, false);
                        return;
                    }
                    if (!IsCurrent(session, generation))
                        return; // superseded by a newer message, regenerate or clear
                    if (final.Length == 0)
                    {
                        Warn(result.FinishReason == "length"
                            ? session.User.DisplayName + " ran out of reply length before answering. The model may be spending it all on thinking."
                            : "The model sent " + session.User.DisplayName + " an empty reply. Try Regenerate Reply.");
                        return;
                    }
                    Post(session, generation, new Message(NewId(session), session.User, DateTime.Now, final));
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
                    Warn(session.User.DisplayName + " couldn't reply. " + what + ConnectionTest.Explain(ex) +
                         " You can check the settings in Tools > AI Services.");
                }
                catch (Exception ex)
                {
                    if (IsCurrent(session, generation))
                        Warn(session.User.DisplayName + " couldn't reply: " + ex.Message);
                }
                finally
                {
                    if (IsCurrent(session, generation))
                        SetTyping(ui, session.User, false);
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

        private static List<LlmChatMessage> BuildRequest(PromptInput input, CharacterTools tools, CancellationToken cancel)
        {
            List<string> toolContext = null;
            int toolTokens = 0;
            if (tools != null)
            {
                toolContext = tools.ContextInjections(cancel);
                toolTokens = PromptBuilder.EstimateTokens(tools.Schema.ToString(Newtonsoft.Json.Formatting.None));
            }
            return PromptBuilder.Build(input.Character.Card, input.Profile, input.Turns, Settings.LlmContextWindow,
                toolContext, toolTokens);
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
                if (index < 0)
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

        public void Dispose()
        {
            lock (_lock)
                foreach (Session session in _sessions.Values)
                    StopReply(session);
        }
    }
}
