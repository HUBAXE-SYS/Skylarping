/*==========================================================*/
// Yggdrasil API copyright © OmegaAOL 2025-2026.
// For any inquiries, email hackersword666@gmail.com.
/*==========================================================*/
// License: https://www.apache.org/licenses/LICENSE-2.0
// SPDX-License-Identifier: Apache-2.0
// Modified by the Skylarping project: moved into the Skymu
// codebase, renamed and adapted (see git history).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Skylarping.Characters;
using Skylarping.Databases;
using Skylarping.Models;

namespace Skylarping.Backend
{
    /// <summary>
    ///  The chat backend the UI talks to. There is exactly one active backend at a time
    ///  (Universal.Backend); it is built into the app, not loaded as a plugin.
    /// </summary>
    public interface IChatBackend : IDisposable
    {
        /// <summary>
        ///  Raised when the backend wants to show a dialog to the user.
        /// </summary>
        event EventHandler<DialogRequestEventArgs> DialogRequested;
        /// <summary>
        ///  Raised when a message is received, edited, or deleted.
        /// </summary>
        event EventHandler<MessageEventArgs> MessagesChanged;
        /// <summary>
        ///  Raised when an entry is added, modified, or removed from the contact or conversation list.
        /// </summary>
        event EventHandler<ListChangedEventArgs> ListChanged;
        /// <summary>
        ///  Raised when there is an incoming call.
        /// </summary>
        event EventHandler<CallEventArgs> IncomingCall;
        /// <summary>
        ///  Raised when the call state changes (i.e. remote user declined, accepted, or hung up).
        /// </summary>
        event EventHandler<CallEventArgs> CallStateChanged;

        /// <summary>
        ///  Timeout for typing status, in milliseconds.
        /// </summary>
        int TypingTimeout { get; }
        /// <summary>
        ///  Repeat interval for typing status, in milliseconds.
        /// </summary>
        int TypingRepeat { get; }
        /// <summary>
        ///  Configurations for clickable items (e.g. user mentions) in messages.
        /// </summary>
        ClickableConfiguration[] ClickableConfigurations { get; }
        /// <summary>
        ///  Users currently typing in the active conversation.
        /// </summary>
        ObservableCollection<User> TypingUsersList { get; }

        /// <summary>
        ///  Signs in to the local profile with the given display name. Returns true on success.
        /// </summary>
        Task<bool> SignIn(string display_name);
        /// <summary>
        ///  Returns a User object with information about the signed-in user.
        /// </summary>
        Task<User> GetUserInfo();
        /// <summary>
        ///  Fetches the contact list.
        /// </summary>
        Task<List<DirectMessage>> FetchContacts();
        /// <summary>
        ///  Fetches the conversation list (direct messages and groups).
        /// </summary>
        Task<List<Conversation>> FetchConversations();
        /// <summary>
        ///  Fetches messages from a conversation. Multiple fetch types supported.
        /// </summary>
        Task<List<ConversationItem>> FetchMessages(
            Conversation conversation,
            Fetch fetch_type = Fetch.Newest,
            int message_count = 50,
            string identifier = null
        );
        /// <summary>
        ///  Sends a message. Returns true on success.
        /// </summary>
        Task<bool> SendMessage(string conversation_id, string text = null, Attachment attachment = null, string parent_message_id = null, bool action = false);
        /// <summary>
        ///  Edits a message. Returns true on success.
        /// </summary>
        Task<bool> EditMessage(string conversation_id, string message_id, string new_text);
        /// <summary>
        ///  Deletes a message. Returns true on success.
        /// </summary>
        Task<bool> DeleteMessage(string conversation_id, string message_id);
        /// <summary>
        ///  Sets your presence: online, away, do not disturb, etc.
        ///  remember = false for automatic changes (auto-away) that must not be restored on next sign-in.
        /// </summary>
        Task<bool> SetConnectionStatus(PresenceStatus status, bool remember = true);
        /// <summary>
        ///  Sets your mood (text status), i.e. "Feeling good!".
        /// </summary>
        Task<bool> SetMood(string status);
        /// <summary>
        ///  Sets your profile picture: a square PNG from CharacterAvatar.Normalize, or null to remove it.
        /// </summary>
        Task<bool> SetAvatar(byte[] avatar);
        /// <summary>
        ///  Sets your typing status in a conversation.
        /// </summary>
        Task<bool> SetTyping(string identifier, bool typing);
        /// <summary>
        ///  Forgets a conversation's history on the backend side (e.g. what a character
        ///  remembers of the chat). The app clears its own saved copy separately.
        /// </summary>
        Task<bool> ClearConversation(string conversation_id);

        /// <summary>
        ///  Adds a character (created or imported in the Add Contact window) as a new contact,
        ///  saving it with the profile and raising ListChanged. Returns the new contact.
        ///  avatar is a square PNG from CharacterAvatar.Normalize, or null for the default picture.
        /// </summary>
        Task<DirectMessage> AddCharacter(CharacterCard card, byte[] avatar);
        /// <summary>
        ///  The saved character behind a contact, for Edit Character. Null if the contact isn't a character.
        /// </summary>
        StoredCharacter GetCharacter(string identifier);
        /// <summary>
        ///  Saves an edited character. Returns its refreshed contact (new name/picture), or null if it's gone.
        /// </summary>
        Task<DirectMessage> UpdateCharacter(string identifier, CharacterCard card, byte[] avatar);
        /// <summary>
        ///  Deletes a character (its card and picture) and raises ListChanged to remove the contact.
        ///  The app deletes the saved chat history separately.
        /// </summary>
        Task<bool> RemoveCharacter(string identifier);

        /// <summary>
        ///  Gives the backend the signed-in profile's database, which holds each character's chat
        ///  memory. Called once after sign-in, before any conversation is opened.
        /// </summary>
        void AttachDatabase(DatabaseManager database);
        /// <summary>
        ///  True if messages in this conversation can be edited and removed (character chats).
        /// </summary>
        bool CanEditMessages(string conversation_id);
        /// <summary>
        ///  True if this message is the latest reply, which Regenerate would replace.
        /// </summary>
        bool CanRegenerate(string conversation_id, string message_id);
        /// <summary>
        ///  Throws away the latest reply in the conversation and asks for a new one.
        /// </summary>
        Task<bool> RegenerateReply(string conversation_id);

        /// <summary>
        ///  Starts a voice call in the specified conversation.
        /// </summary>
        Task<ActiveCall> StartCall(string convo_id, bool start_muted);
        /// <summary>
        ///  Answers an incoming call from the specified conversation.
        /// </summary>
        Task<ActiveCall> AnswerCall(string convo_id);
        /// <summary>
        ///  Declines an incoming call from the specified conversation.
        /// </summary>
        Task<bool> DeclineCall(string convo_id);
        /// <summary>
        ///  Ends an ongoing call.
        /// </summary>
        Task<bool> EndCall(ActiveCall call);
        /// <summary>
        ///  Signals that you're muted or unmuted.
        /// </summary>
        Task<bool> SetMuted(ActiveCall call, bool muted);
    }
}
