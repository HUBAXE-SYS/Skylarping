/*==========================================================*/
// Copyright © The Skymu Team and other contributors.
// For any inquiries or concerns, email contact@skymu.app.
/*==========================================================*/
// Modification or redistribution of this code is governed
// by the terms set out in the project license agreement.
// If you do not comply with those terms, you may not
// modify or distribute any original code from the project.
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

// Fake chat backend with canned data, for working on the UI without a real backend.
// Type "Call me!" without the quotes to get called by the active conversation

using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Skylarping.Audio;
using Skylarping.Characters;
using Skylarping.Databases;
using Skylarping.Models;
using Skylarping.Preferences;

namespace Skylarping.Backend
{
    public class StubBackend : IChatBackend
    {
        #region Variables

        public event EventHandler<DialogRequestEventArgs> DialogRequested;
        public event EventHandler<MessageEventArgs> MessagesChanged;
        public event EventHandler<ListChangedEventArgs> ListChanged;
        public ObservableCollection<User> TypingUsersList { get; private set; } =
            new ObservableCollection<User>();

        private Conversation _currentConversation;
        private User Me;
        private CharacterStore _characters;
        private GroupStore _groups;
        private string _profileFolder;

        // The user's own picture, next to the database in the profile folder.
        private const string AvatarFile = "avatar.png";
        private string AvatarPath => Path.Combine(_profileFolder, AvatarFile);

        #endregion

        public StubBackend()
        {
            _echo = new EchoService(
                () => Me,
                e => MessagesChanged?.Invoke(this, e),
                e => CallStateChanged?.Invoke(this, e),
                TypingUsersList);
            _chat = new CharacterChatService(
                () => Me,
                e => MessagesChanged?.Invoke(this, e),
                e => DialogRequested?.Invoke(this, e),
                TypingUsersList);
        }

        // Characters are real: saved in CharacterStore, chatting through CharacterChatService.
        private readonly CharacterChatService _chat;

        // echo123 is real (it talks to the configured language model); everything else here is canned.
        private readonly EchoService _echo;

        public Task<bool> SignIn(string display_name)
        {
            Me = new User(display_name, display_name, display_name);
            // Same profile folder as the database (see DatabaseManager).
            _profileFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Universal.NAME,
                DatabaseManager.SanitizeFolderName(Me.Identifier));
            _characters = new CharacterStore(Path.Combine(_profileFolder, "Characters"));
            _groups = new GroupStore(Path.Combine(_profileFolder, "Groups"));
            return TaskEx.FromResult(true);
        }

        // Also called on logout
        public void Dispose()
        {
            _echo.Dispose();
            EndCharacterCall();
            _chat.Dispose();
            _out?.Stop();
            _out?.Dispose();
            _out = null;
        }

        public Task<bool> SendMessage(
            string identifier,
            string text,
            Attachment attachment,
            string parent_message_identifier,
            bool action
        )
        {
            if (EchoService.Handles(identifier))
            {
                if (text == null)
                    DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Echo can't receive files."));
                return TaskEx.FromResult(_echo.SendMessage(text, action));
            }
            if (_chat.Handles(identifier))
            {
                if (text == null)
                {
                    DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Characters can't receive files yet."));
                    return TaskEx.FromResult(false);
                }
                return TaskEx.FromResult(_chat.SendMessage(identifier, text, action));
            }

            // Invoke a call
            if (text == "Call me!")
            {
                User user;
                if (_currentConversation is DirectMessage dm)
                    user = dm.Partner;
                else if (_currentConversation is Group group)
                    user = group.Members[0];
                else
                    return TaskEx.FromResult(false);
                IncomingCall?.Invoke(this, new CallEventArgs(new DirectMessage(user, 0, "TotallyRandomIncomingCall"), CallState.Ringing));
                return TaskEx.FromResult(true);
            }
            if (text != null)
            {
                if (attachment != null)
                    DialogRequested?.Invoke(
                        this,
                        new DialogRequestEventArgs(DialogType.Warning, (action ? "Action message" : "Message") + " with text and attachment sent.")
                    );
                else
                    DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Text-only " + (action ? "action" : "") + " message sent."));
            }
            else
                DialogRequested?.Invoke(
                    this,
                    new DialogRequestEventArgs(DialogType.Warning, "Attachment-only message sent.")
                );
            if (parent_message_identifier != null)
                DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Message references a parent."));
            TaskEx.Run(async () =>
            {
                await TaskEx.Delay(3000);
                // Make the UI recognize that the message was sent, adding the timestamp and removing the Spinner (loading wheel)
                MessagesChanged?.Invoke(this, new MessageReceivedEventArgs(identifier,
                    action
                    ? new ActionMessage(identifier, Me, DateTimeOffset.UtcNow.DateTime, text)
                    : new Message(identifier, Me, DateTimeOffset.UtcNow.DateTime, text))
                );
            });

            return TaskEx.FromResult(true);
        }

        public Task<bool> EditMessage(
            string conversationId,
            string messageId,
            string newText
        )
        {
            if (_chat.Handles(conversationId))
                return TaskEx.FromResult(_chat.Edit(conversationId, messageId, newText));
            DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Message editing is not implemented."));
            return TaskEx.FromResult(false);
        }

        public Task<bool> DeleteMessage(
            string conversationId,
            string messageId
        )
        {
            if (_chat.Handles(conversationId))
                return TaskEx.FromResult(_chat.Delete(conversationId, messageId));
            DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Message deletion is not implemented."));
            return TaskEx.FromResult(false);
        }

        public Task<List<ConversationItem>> FetchMessages(
            Conversation conversation,
            Fetch fetch_type,
            int message_count,
            string identifier
        )
        { // THIS IS STUB CODE. THIS IS NOT A REPLICATION OF HOW THE INTERFACE IS SUPPOSED TO WORK.
            _currentConversation = conversation;
            TypingUsersList.Clear();
            if (EchoService.Handles(conversation?.Identifier))
                return TaskEx.FromResult(_echo.FetchMessages());
            if (_chat.Handles(conversation?.Identifier))
                return TaskEx.FromResult(_chat.FetchMessages(conversation.Identifier, fetch_type, identifier));
            return TaskEx.FromResult(new List<ConversationItem>());
        }

        public Task<User> GetUserInfo()
        {
            string mood = Preferences.Settings.ProfileMood;
            Me.Status = string.IsNullOrEmpty(mood) ? null : mood;
            Me.ConnectionStatus = RestorablePresence(Preferences.Settings.ProfilePresence);
            try
            {
                Me.Avatar = File.Exists(AvatarPath) ? File.ReadAllBytes(AvatarPath) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Couldn't read the profile picture: " + ex);
                Me.Avatar = null;
            }
            return TaskEx.FromResult(Me);
        }

        public Task<List<DirectMessage>> FetchContacts()
        {
            List<DirectMessage> contacts = new List<DirectMessage>
            {
                _echo.CreateContact()
            };
            foreach (StoredCharacter character in _characters.LoadAll())
                contacts.Add(_chat.Register(character));
            return TaskEx.FromResult(contacts);
        }

        public Task<List<Conversation>> FetchConversations()
        {
            // Groups need their members, and contacts come after conversations.
            foreach (StoredCharacter character in _characters.LoadAll())
                _chat.Register(character);
            var conversations = new List<Conversation>();
            foreach (StoredGroup group in _groups.LoadAll())
                conversations.Add(_chat.RegisterGroup(group));
            _chat.RefreshSummaries();
            return TaskEx.FromResult(conversations);
        }

        public ClickableConfiguration[] ClickableConfigurations
        {
            get
            {
                return new ClickableConfiguration[]
                {
                    new ClickableConfiguration(ClickableItemType.User, "<@!", ">"),
                    new ClickableConfiguration(ClickableItemType.User, "<@", ">"),
                };
            }
        }

        public Task<bool> SetMood(string status)
        {
            Me.Status = string.IsNullOrEmpty(status) ? null : status;
            Preferences.Settings.ProfileMood = status ?? string.Empty;
            return TaskEx.FromResult(true);
        }

        public Task<bool> SetAvatar(byte[] avatar)
        {
            try
            {
                Directory.CreateDirectory(_profileFolder);
                if (avatar != null && avatar.Length > 0)
                    CharacterStore.WriteAtomic(AvatarPath, avatar);
                else if (File.Exists(AvatarPath))
                    File.Delete(AvatarPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Couldn't save the profile picture: " + ex);
                return TaskEx.FromResult(false);
            }
            Me.Avatar = avatar != null && avatar.Length > 0 ? avatar : null;
            return TaskEx.FromResult(true);
        }

        public Task<bool> SetConnectionStatus(PresenceStatus status, bool remember = true)
        {
            Me.ConnectionStatus = status;
            if (remember && RestorablePresence(status) == status)
                Preferences.Settings.ProfilePresence = status;
            return TaskEx.FromResult(true);
        }

        // Only the presences a user can pick from the status menu are saved and restored;
        // anything else (e.g. Offline from a sign-out) falls back to Online.
        private static PresenceStatus RestorablePresence(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.Online:
                case PresenceStatus.Away:
                case PresenceStatus.DoNotDisturb:
                case PresenceStatus.Invisible:
                    return status;
                default:
                    return PresenceStatus.Online;
            }
        }

        public int TypingTimeout => 5000;
        public int TypingRepeat => 9000;
        public Task<bool> SetTyping(string idenfitier, bool typing)
        {
            return TaskEx.FromResult(false);
        }

        public Task<bool> ClearConversation(string conversation_id)
        {
            if (EchoService.Handles(conversation_id))
                _echo.ClearHistory();
            else
                _chat.Clear(conversation_id);
            return TaskEx.FromResult(true);
        }

        public void AttachDatabase(DatabaseManager database)
        {
            _chat.Database = database;
        }

        public bool CanEditMessages(string conversation_id) => _chat.Handles(conversation_id);

        public bool CanRegenerate(string conversation_id, string message_id) =>
            _chat.Handles(conversation_id) && _chat.IsLatestReply(conversation_id, message_id);

        public Task<bool> RegenerateReply(string conversation_id)
        {
            return TaskEx.FromResult(_chat.Handles(conversation_id) && _chat.Regenerate(conversation_id));
        }

        #region Calls

        private WaveOutEvent _out;

        private WaveOutEvent WaveDispenser()
        {
            return AudioDevices.CreateOutput();
        }

        private TaskCompletionSource<bool> _waiter;
        private string _callConversation; // the character on the phone, if any (they don't message first during a call)

        private const int CharacterRingMilliseconds = 2000;
        private CancellationTokenSource _characterRing;
        private VoiceCall _voice;

        private void SetCallConversation(string convo_id)
        {
            if (_callConversation != null)
                _chat.SetInCall(_callConversation, false);
            _callConversation = convo_id;
            if (convo_id != null)
                _chat.SetInCall(convo_id, true);
        }

        // Call will be picked up as soon as something is returned
        public async Task<ActiveCall> StartCall(string convo_id, bool start_muted)
        {
            if (EchoService.Handles(convo_id))
                return await _echo.StartCall(); // start_muted ignored: a muted sound test records nothing
            if (_chat.GetGroup(convo_id) != null)
                return FailCall("Group calls aren't available yet.");
            if (_chat.Handles(convo_id))
                return await StartCharacterCall(convo_id, start_muted);

            _out?.Dispose();

            // Fake call: ring for a few seconds, then "connect" to silence.
            _out = WaveDispenser();

            var silence = new SilenceProvider(
                new WaveFormat(48000, 16, 2));

            _out.Init(silence);
            _out.Play();

            _waiter =
                new TaskCompletionSource<bool>();

            Thread thread = new Thread(_ =>
            {
                Thread.Sleep(3200);
                _waiter?.TrySetResult(true);
            });

            thread.Start();

            bool cont = await _waiter.Task;

            if (!cont)
                return null;

            return new ActiveCall(
                "STUBCALL",
                convo_id,
                new User[0]
            );
        }

        private async Task<ActiveCall> StartCharacterCall(string convo_id, bool start_muted)
        {
            if (!Settings.SttConfigured)
                return FailCall("Calls need a speech-to-text server. Set one up in Tools > AI Services.");

            EndCharacterCall();
            var ring = new CancellationTokenSource();
            _characterRing = ring;
            SetCallConversation(convo_id);

            try { await TaskEx.Delay(CharacterRingMilliseconds, ring.Token); }
            catch (OperationCanceledException) { return null; }
            if (ring.IsCancellationRequested)
                return null;

            VoiceCall voice = null;
            try
            {
                voice = new VoiceCall(start_muted, CharacterVoice(convo_id),
                    text => _chat.SendSpoken(convo_id, text),
                    problem => DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, problem)),
                    heard => _chat.Interrupted(convo_id, heard));
                voice.Start();
            }
            catch (Exception ex)
            {
                voice?.Dispose();
                SetCallConversation(null);
                return FailCall("The call couldn't use your microphone: " + ex.Message);
            }

            _voice = voice;
            if (voice.Speaker != null)
            {
                _chat.SetSpeaker(convo_id, voice.Speaker);
                _chat.AnswerCall(convo_id);
            }
            return new ActiveCall("voice-" + Guid.NewGuid().ToString("N"), convo_id, new User[0]);
        }

        // Null while no text-to-speech is set up: the call still hears you, and replies stay in the chat.
        private CallVoice CharacterVoice(string convo_id)
        {
            if (!Settings.TtsConfigured)
                return null;
            StoredCharacter character = _chat.GetCharacter(convo_id);
            return new CallVoice
            {
                Sample = character == null ? null : CharacterStore.ReadVoiceSample(character.Folder),
            };
        }

        private ActiveCall FailCall(string problem)
        {
            DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, problem));
            return null;
        }

        private void EndCharacterCall()
        {
            _characterRing?.Cancel();
            _characterRing = null;
            _chat.SetSpeaker(_callConversation, null);
            _voice?.Dispose();
            _voice = null;
            SetCallConversation(null);
        }

        public Task<bool> EndCall(ActiveCall call)
        {
            _echo.EndCall(); // also cancels an echo call that's still ringing
            EndCharacterCall();
            _waiter?.TrySetResult(false);
            _out?.Stop();
            _out?.Dispose();
            _out = null;
            return TaskEx.FromResult(true);
        }

        public async Task<ActiveCall> AnswerCall(string convo_id) => await StartCall(convo_id, false);

        public Task<bool> DeclineCall(string convo_id) => TaskEx.FromResult(false);

        public Task<bool> SetMuted(ActiveCall call, bool muted)
        {
            if (call != null && EchoService.Handles(call.ConversationId))
            {
                _echo.SetMuted(muted);
                return TaskEx.FromResult(true);
            }
            if (_voice != null)
            {
                _voice.Muted = muted;
                return TaskEx.FromResult(true);
            }
            return TaskEx.FromResult(false);
        }

        public event EventHandler<CallEventArgs> IncomingCall;
        public event EventHandler<CallEventArgs> CallStateChanged;
        #endregion

        #region Adding contacts

        public Task<DirectMessage> AddCharacter(CharacterCard card, byte[] avatar)
        {
            StoredCharacter character = _characters.Add(card, avatar);
            DirectMessage contact = _chat.Register(character);
            ListChanged?.Invoke(this, new ListItemUpdatedEventArgs(ListType.Contacts, contact));
            _chat.EnsureMood(character.Id); // if they update their mood, they write one (in the background; none if it fails)
            _chat.RefreshSummaries();
            return TaskEx.FromResult(contact);
        }

        public StoredCharacter GetCharacter(string identifier) => _chat.GetCharacter(identifier);

        public Task<bool> RemoveCharacter(string identifier)
        {
            if (_chat.GetCharacter(identifier) == null)
                return TaskEx.FromResult(false);
            _chat.Remove(identifier);
            _characters.Delete(identifier);
            ListChanged?.Invoke(this, new ListItemRemovedEventArgs(ListType.Contacts, identifier));
            ListChanged?.Invoke(this, new ListItemRemovedEventArgs(ListType.Conversations, identifier));
            return TaskEx.FromResult(true);
        }

        public byte[] GetVoiceSample(string identifier)
        {
            StoredCharacter character = _chat.GetCharacter(identifier);
            return character == null ? null : CharacterStore.ReadVoiceSample(character.Folder);
        }

        public void SetVoiceSample(string identifier, byte[] sample)
        {
            StoredCharacter character = _chat.GetCharacter(identifier);
            if (character != null)
                CharacterStore.WriteVoiceSample(character.Folder, sample);
        }

        public Task<DirectMessage> UpdateCharacter(string identifier, CharacterCard card, byte[] avatar)
        {
            if (_chat.GetCharacter(identifier) == null)
                return TaskEx.FromResult<DirectMessage>(null);
            StoredCharacter character = _characters.Update(identifier, card, avatar);
            DirectMessage contact = _chat.Register(character);
            _chat.EnsureMood(identifier); // just switched on, with no mood yet
            _chat.RefreshSummaries();
            return TaskEx.FromResult(contact);
        }

        public Task<Group> CreateGroup(StoredGroup group)
        {
            Group conversation = _chat.RegisterGroup(_groups.Add(group));
            ListChanged?.Invoke(this, new ListItemUpdatedEventArgs(ListType.Conversations, conversation));
            _chat.RefreshSummaries();
            return TaskEx.FromResult(conversation);
        }

        public StoredGroup GetGroup(string identifier) => _chat.GetGroup(identifier);

        public Task<Group> UpdateGroup(StoredGroup group)
        {
            Group conversation = _chat.UpdateGroup(group);
            _chat.RefreshSummaries();
            return TaskEx.FromResult(conversation);
        }

        public Task<bool> RemoveGroup(string identifier)
        {
            if (_chat.GetGroup(identifier) == null)
                return TaskEx.FromResult(false);
            _chat.RemoveGroup(identifier);
            _groups.Delete(identifier);
            ListChanged?.Invoke(this, new ListItemRemovedEventArgs(ListType.Conversations, identifier));
            return TaskEx.FromResult(true);
        }

        #endregion
    }
}
