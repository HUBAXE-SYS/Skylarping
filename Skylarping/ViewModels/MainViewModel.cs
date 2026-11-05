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

using Skylarping.Mvvm;
using Microsoft.Win32;
using Skylarping.Databases;
using Skylarping.Emoticons;
using Skylarping.Enumerations;
using Skylarping.Forms;
using Skylarping.Forms.Pages;
using Skylarping.Helpers;
using Skylarping.Preferences;
using Skylarping.Sounds;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Skylarping.Backend;
using Skylarping.Models;

namespace Skylarping.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        #region Shared state

        // this is an OC for now because only one conversation is loaded at any given time, must rework once "Split Window Mode"
        // is added and obviously we'll have multiple conversations loaded at the same time then
        public ObservableCollection<ConversationItem> ActiveConversation { get; }

        // OC's for the two types of lists shown in the UI that can be bound to in WPF
        public ObservableCollection<DirectMessage> ContactList;
        public ObservableCollection<Conversation> ConversationList;

        // Constants
        private const string NOT_IMPLEMENTED = "This feature is not implemented yet.";

        // for the database, TODO change list loading so it attempts to load from DB first
        internal DatabaseManager Database
        {
            get => _database;
        }

        // this is different from ActiveConversation because "Conversation" is not a container of "ConversationItem"
        // even though the naming may imply that
        private Conversation _selectedConversation;
        public Conversation SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                SetProperty(ref _selectedConversation, value);
                MarkRead(value); // opening a conversation reads it
            }
        }

        private bool _isLoadingConversation;
        public bool IsLoadingConversation
        {
            get => _isLoadingConversation;
            set => SetProperty(ref _isLoadingConversation, value);
        }

        private string _typingText = string.Empty;
        public string TypingText
        {
            get => _typingText;
            private set => SetProperty(ref _typingText, value);
        }

        private bool _isTypingVisible;
        public bool IsTypingVisible
        {
            get => _isTypingVisible;
            private set => SetProperty(ref _isTypingVisible, value);
        }

        private bool _isCallActive;
        public bool IsCallActive
        {
            get => _isCallActive;
            set => SetProperty(ref _isCallActive, value);
        }

        public bool IsWindowActive = false;

        /// <summary>The conversation on a call right now (ringing included); null when there's no call.</summary>
        public string CallConversationId;

        #endregion

        #region Events for the View

        public event EventHandler Ready;

        public event EventHandler ConversationLoaded;

        public event EventHandler ConversationOpened;

        public event EventHandler ConversationItemChanged;

        public event EventHandler ConversationChanged;

        public event EventHandler CompactRecentsRefreshRequested;

        public event EventHandler<SignOutRequestedEventArgs> SignOutRequested;

        public event Action<bool> CallActiveChanged;

        public event Action<CallEventArgs> IncomingCallAccepted;

        #endregion

        #region Commands

        public IAsyncRelayCommand<string> SendMessageCommand { get; }
        private bool _isDownloading = false;
        public ICommand OpenImageCommand =>
            new RelayCommand<Attachment[]>(async attachments =>
            {
                if (attachments == null || attachments.Length == 0 || _isDownloading)
                    return;
                _isDownloading = true;
                try
                {
                    string url = attachments[0].Url;
                    string tempPath = Path.Combine(Path.GetTempPath(), $"{Universal.NAME.ToLowerInvariant()}_attachment_temp");
                    byte[] data = await Skylarping.Net.Http.GetBytesAsync(url);
                    File.WriteAllBytes(tempPath, data);
                    string ext = ImageHelper.ResolveExtension(
                        data,
                        attachments[0].Name
                    ); // TODO spin off to helper method
                    string finalPath = tempPath + ext;
                    if (File.Exists(finalPath))
                        File.Delete(finalPath);
                    File.Move(tempPath, finalPath);
                    Universal.OpenUrl(finalPath);
                }
                finally
                {
                    // previously a failed download left this stuck at true, blocking further downloads
                    _isDownloading = false;
                }
            });
        public IAsyncRelayCommand CallCommand { get; }
        public IAsyncRelayCommand<string> SelectConversationCommand { get; }

        #endregion

        #region Private state

        private DatabaseManager _database;
        private NotifyCollectionChangedEventHandler _conversationCollectionHandler;
        private readonly Dictionary<string, Message> _pendingPreviewMessages;
        private bool _synchronizing;
        private bool _typingIndicatorSubscribed;
        private bool _typingActive;
        private Timer _typingTimer;
        private Timer _typingRepeatTimer;

        private const string SKYLARPING_PREFIX = "@skylarping/";
        private const string SKYLARPING_SENDING = SKYLARPING_PREFIX + "sending";

        #endregion

        #region Icon dictionaries

        private static readonly Dictionary<PresenceStatus, int> StatusMap = new Dictionary<
            PresenceStatus,
            int
        >
        {
            { PresenceStatus.Online, 2 },
            { PresenceStatus.OnlineMobile, 2 },
            { PresenceStatus.Away, 3 },
            { PresenceStatus.AwayMobile, 3 },
            { PresenceStatus.DoNotDisturb, 5 },
            { PresenceStatus.DoNotDisturbMobile, 5 },
            { PresenceStatus.Invisible, 19 },
            { PresenceStatus.Blocked, 9 },
            { PresenceStatus.Offline, 14 },
            { PresenceStatus.Unknown, 0 },
        };

        public static int GetIntFromStatus(PresenceStatus status) =>
            StatusMap.TryGetValue(status, out int v) ? v : 0;

        public static PresenceStatus GetStatusFromInt(int value) =>
            StatusMap.FirstOrDefault(x => x.Value == value).Key;

        #endregion

        #region Init

        public MainViewModel()
        {
            Universal.ActiveViewModel = this;

            ActiveConversation = new ObservableCollection<ConversationItem>();

            // just in case something tries to use these lists before they've been populated, don't crash the app with NullReferenceException
            ContactList = new ObservableCollection<DirectMessage>();
            ConversationList = new ObservableCollection<Conversation>();

            _pendingPreviewMessages = new Dictionary<string, Message>();
            _typingActive = false;
            _typingTimer = new Timer(
                _ =>
                {
                    Universal.Backend?.SetTyping(SelectedConversation?.Identifier, false);
                    _typingRepeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _typingActive = false;
                },
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

            _typingRepeatTimer = new Timer(
                _ =>
                {
                    Universal.Backend?.SetTyping(SelectedConversation?.Identifier, true);
                    _typingActive = false;
                },
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

            SendMessageCommand = new AsyncRelayCommand<string>(SendMessage);
        }

        public async Task InitSidebar()
        {
            ConversationList = new ObservableCollection<Conversation>();
            ContactList = new ObservableCollection<DirectMessage>();

            await LoadBackend(Universal.Backend);

            Ready?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Conversation handling

        /// <summary> For external usages </summary>
        public void SelectConversation(Conversation conversation)
        {
            SelectedConversation = conversation;
            ConversationChanged?.Invoke(conversation, EventArgs.Empty);
        }

        public async Task SetConversation()
        {
            if (SelectedConversation == null)
                return;

            ClearActiveConversation();
            ConversationOpened?.Invoke(this, EventArgs.Empty);
            IsLoadingConversation = true;

            List<ConversationItem> cached = _database?.Messages.Read(
                SelectedConversation,
                Settings.MsgLoadCount
            );
            List<ConversationItem> items;

            if (cached != null && cached.Count > 0)
            {
                items = cached;
                IsLoadingConversation = false;
                _ = SyncMessagesInBackground(
                    SelectedConversation,
                    cached[cached.Count - 1].Identifier
                );
            }
            else
            {
                items = await Universal.Backend.FetchMessages(
                    SelectedConversation,
                    Fetch.Newest,
                    Settings.MsgLoadCount,
                    null
                );
                _database?.Messages.Write(items, SelectedConversation);
            }

            if (SelectedConversation == null)
                return;

            if (items != null && items.Count > 0)
            {
                foreach (ConversationItem item in items)
                    ActiveConversation.Add(item);
                TrimActiveConversation();

                // Back-fill PreviousMessageIdentifier
                for (int i = 0; i < ActiveConversation.Count; i++)
                {
                    if (ActiveConversation[i] is Message msg)
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (ActiveConversation[j] is Message prev)
                            {
                                msg.PreviousMessageIdentifier = prev.Author.Identifier;
                                msg.PreviousMessageIsAction = prev is ActionMessage;
                                break;
                            }
                        }
                    }
                }
            }

            SubscribeConversationCollectionChanges();

            IsLoadingConversation = false;
            ConversationLoaded?.Invoke(this, EventArgs.Empty);
        }

        private void SubscribeConversationCollectionChanges()
        {
            if (_conversationCollectionHandler != null)
                ActiveConversation.CollectionChanged -= _conversationCollectionHandler;

            Conversation currentConv = SelectedConversation;

            _conversationCollectionHandler = (s, args) =>
            {
                if (IsLoadingConversation)
                    return;
                if (args.Action != NotifyCollectionChangedAction.Add)
                    return;

                foreach (var addedItem in args.NewItems)
                {
                    if (!(addedItem is Message message))
                        continue;

                    if (
                        message.Author.Identifier == Universal.CurrentUser?.Identifier
                        && message.Identifier != null
                        && !message.Identifier.StartsWith(SKYLARPING_SENDING)
                    )
                    {
                        var match =
                            _pendingPreviewMessages.Values.LastOrDefault(p =>
                                p.Text == message.Text
                            ) ?? _pendingPreviewMessages.Values.LastOrDefault();

                        if (match != null)
                        {
                            _pendingPreviewMessages.Remove(match.Identifier);
                            Application.Current.Dispatcher.BeginInvoke(
                                new Action(() =>
                                {
                                    ActiveConversation.Remove(match);
                                })
                            );
                        }
                    }

                    int idx = ActiveConversation.IndexOf(message);
                    for (int i = idx - 1; i >= 0; i--)
                    {
                        if (
                            ActiveConversation[i] is Message prev
                            && !prev.Identifier.StartsWith(SKYLARPING_SENDING)
                        )
                        {
                            message.PreviousMessageIdentifier = prev.Author.Identifier;
                            message.PreviousMessageIsAction = prev is ActionMessage;
                            break;
                        }
                    }

                    if (
                        message.Author.Identifier != Universal.CurrentUser?.Identifier
                        && IsWindowActive
                        && !_synchronizing
                    )
                    {
                        
                    }
                }

                ConversationItemChanged?.Invoke(this, EventArgs.Empty);
            };

            ActiveConversation.CollectionChanged += _conversationCollectionHandler;
        }

        #region Message and character actions

        /// <summary>Raised when an item in the open conversation changes in place (edits, streaming).</summary>
        public event EventHandler ConversationItemUpdated;

        public bool CanEditMessage(Message message) =>
            message != null && SelectedConversation != null && Settings.ShowEditMessage
            && Universal.Backend?.CanEditMessages(SelectedConversation.Identifier) == true
            && !message.Identifier.StartsWith(SKYLARPING_SENDING);

        public bool CanRegenerate(Message message) =>
            message != null && SelectedConversation != null && Settings.ShowRegenerate
            && Universal.Backend?.CanRegenerate(SelectedConversation.Identifier, message.Identifier) == true;

        public void CopyMessage(Message message)
        {
            if (!string.IsNullOrEmpty(message?.Text))
            {
                try { Clipboard.SetText(message.Text); }
                catch { } // clipboard busy
            }
        }

        public void EditMessage(Message message)
        {
            if (!CanEditMessage(message))
                return;
            string conversationId = SelectedConversation.Identifier;
            string text = EditMessageText.Show(message.Text);
            if (text == null || text == message.Text)
                return;
            _ = Universal.Backend.EditMessage(conversationId, message.Identifier, text);
        }

        public void RemoveMessage(Message message)
        {
            if (!CanEditMessage(message))
                return;
            _ = Universal.Backend.DeleteMessage(SelectedConversation.Identifier, message.Identifier);
        }

        public void RegenerateReply(Message message)
        {
            if (!CanRegenerate(message))
                return;
            _ = Universal.Backend.RegenerateReply(SelectedConversation.Identifier);
        }

        public bool IsCharacter(Conversation conversation) =>
            conversation != null && Universal.Backend?.GetCharacter(conversation.Identifier) != null;

        public void ShowEditCharacterWindow(Conversation conversation)
        {
            var character = conversation == null ? null : Universal.Backend?.GetCharacter(conversation.Identifier);
            if (character != null)
                new AddContact(character).ShowWindow();
        }

        /// <summary>Asks, then removes a character contact along with its chat history.</summary>
        public void ConfirmRemoveContact(Conversation conversation)
        {
            if (!IsCharacter(conversation))
                return;
            var dialog = new Dialog(
                type: WindowBase.IconType.Question,
                content: "Remove " + conversation.DisplayName + " from your contacts? Their character and your chat " +
                         "history with them will be deleted. This can't be undone.",
                header: "Remove contact",
                brText: Universal.Lang["sF_CONFIRM_NO_BTN"],
                blEnabled: true,
                blText: Universal.Lang["sF_CONFIRM_YES"]
            );
            dialog.BRAction = () => dialog.Close();
            dialog.BLAction = () =>
            {
                dialog.Close();
                _ = RemoveContact(conversation);
            };
            dialog.ShowDialog();
        }

        private async Task RemoveContact(Conversation conversation)
        {
            bool wasOpen = SelectedConversation?.Identifier == conversation.Identifier;
            try
            {
                MarkRead(conversation);
                _database?.Messages.Clear(conversation);
                if (!await Universal.Backend.RemoveCharacter(conversation.Identifier))
                    return;
            }
            catch (Exception ex)
            {
                Universal.ShowMessage("The contact couldn't be removed: " + ex.Message, null, WindowBase.IconType.Error);
                return;
            }
            if (wasOpen)
            {
                // Don't leave the removed character's chat open; go to the first remaining contact (Echo).
                Conversation next = ContactList.FirstOrDefault(c => c.Identifier != conversation.Identifier);
                if (next != null)
                {
                    SelectConversation(next);
                    await SetConversation();
                }
                else
                {
                    ClearActiveConversation();
                }
            }
        }

        /// <summary>Shows a character's new name and picture everywhere after Edit Character.</summary>
        public void RefreshContact(DirectMessage updated)
        {
            if (updated == null)
                return;
            foreach (Conversation c in ContactList.Cast<Conversation>().Concat(ConversationList)
                                                  .Where(c => c.Identifier == updated.Identifier))
            {
                c.DisplayName = updated.DisplayName;
                c.Avatar = updated.Avatar;
            }
            if (_database != null)
            {
                _database.Contacts.Write(new[] { updated });
                _database.Conversations.Write(new[] { updated });
            }
        }

        #endregion

        /// <summary>
        ///  Asks, then deletes a conversation's chat history: the saved messages and the
        ///  backend's copy (what the character remembers). The contact stays.
        /// </summary>
        public void ConfirmClearHistory(Conversation conversation)
        {
            if (conversation == null)
                return;
            var dialog = new Dialog(
                type: WindowBase.IconType.Question,
                content: "Clear your chat history with " + conversation.DisplayName + "? " +
                         "All messages will be deleted, and they will forget the conversation. This can't be undone.",
                header: "Clear chat history",
                brText: Universal.Lang["sF_CONFIRM_NO_BTN"],
                blEnabled: true,
                blText: Universal.Lang["sF_CONFIRM_YES"]
            );
            dialog.BRAction = () => dialog.Close();
            dialog.BLAction = () =>
            {
                dialog.Close();
                _ = ClearHistory(conversation);
            };
            dialog.ShowDialog();
        }

        private async Task ClearHistory(Conversation conversation)
        {
            try
            {
                if (Universal.Backend != null)
                    await Universal.Backend.ClearConversation(conversation.Identifier);
                _database?.Messages.Clear(conversation);
                MarkRead(conversation); // nothing left to read
            }
            catch (Exception ex)
            {
                Universal.ShowMessage("The chat history couldn't be cleared: " + ex.Message, null, WindowBase.IconType.Error);
                return;
            }
            if (SelectedConversation?.Identifier == conversation.Identifier)
            {
                _pendingPreviewMessages.Clear();
                ActiveConversation.Clear();
            }
        }

        public void ClearActiveConversation()
        {
            _pendingPreviewMessages.Clear();
            Universal.Backend?.TypingUsersList?.Clear();

            if (_conversationCollectionHandler != null)
                ActiveConversation.CollectionChanged -= _conversationCollectionHandler;

            ActiveConversation.Clear();
            _conversationCollectionHandler = null;
        }

        private async Task SyncMessagesInBackground(Conversation conversation, string afterId)
        {
            List<ConversationItem> items = await Universal.Backend.FetchMessages(
                conversation,
                Fetch.NewestAfterIdentifier,
                Settings.MsgLoadCount,
                afterId
            );

            if (items == null || items.Count == 0)
                return;
            _database?.Messages.Write(items, conversation);

            if (SelectedConversation != conversation)
                return;

            _synchronizing = true;
            foreach (ConversationItem item in items)
                ActiveConversation.Add(item);
            TrimActiveConversation();
            _synchronizing = false;
        }

        /// <summary>
        /// Drops the oldest items once the open conversation exceeds Settings.MaxConversationItems
        /// (default 250). The chat list isn't virtualized, so every message is a live UI element;
        /// this also bounds how much history is around for an AI conversation's context.
        /// </summary>
        private void TrimActiveConversation()
        {
            int max = Math.Max(1, Settings.MaxConversationItems);
            if (ActiveConversation.Count <= max)
                return;

            while (ActiveConversation.Count > max)
                ActiveConversation.RemoveAt(0);

            // The new first message no longer has a predecessor on screen, so it should show its author.
            Message first = ActiveConversation.OfType<Message>().FirstOrDefault();
            if (first != null)
            {
                first.PreviousMessageIdentifier = null;
                first.PreviousMessageIsAction = false;
            }
        }

        #endregion

        #region Incoming item handler

        public void HandleIncoming(MessageEventArgs e)
        {
            if (e is MessageReceivedEventArgs eR)
            {
                Conversation conversation = FindConversation(eR.ConversationId);
                if (conversation != null)
                    _database?.Messages.WriteRow(eR.Item, conversation);

                bool isOpen = SelectedConversation?.Identifier == eR.ConversationId;
                if (isOpen)
                {
                    ActiveConversation.Add(eR.Item);
                    TrimActiveConversation();
                }
                if (eR.Item is Message message)
                {
                    UpdateRecentsListOnNewMessage(e.ConversationId, message.Time);
                    if (message.Author?.Identifier == Universal.CurrentUser?.Identifier) return;

                    // On a call with them: this is what they're saying, so it isn't news.
                    if (eR.ConversationId == CallConversationId) return;

                    // "Focused" = the conversation is open and the window is in front: you're
                    // already looking at it, so no sound unless the user asked for one.
                    bool focused = isOpen && IsWindowActive;
                    if (!focused)
                        MarkUnread(eR.ConversationId);
                    bool sound = !focused || Settings.SoundInFocusedConversation;
                    bool notify = (Settings.NotificationTrigger & NotificationTriggerType.ALL) != 0
                        || ((Settings.NotificationTrigger & NotificationTriggerType.DM) != 0 && !focused);

                    if (notify)
                        new Notification(eR, playSound: sound); // plays the sound itself (unless DND / disabled)
                    else if (focused && sound)
                        SoundManager.Play("IM");
                }
            }
            else if (e is MessageDeletedEventArgs eD)
            {
                Conversation conversation = FindConversation(e.ConversationId);
                if (conversation != null)
                    _database?.Messages.DeleteRow(conversation, eD.DeletedItemId);
                if (SelectedConversation?.Identifier == e.ConversationId)
                {
                    var item = ActiveConversation.FirstOrDefault(x => x.Identifier == eD.DeletedItemId);
                    if (item != null)
                        ActiveConversation.Remove(item);
                }
            }
            else if (e is MessageEditedEventArgs eE)
            {
                // Partial edits are a reply still being streamed: shown, saved once it's finished.
                Conversation conversation = FindConversation(e.ConversationId);
                if (conversation != null && !eE.IsPartial && eE.NewItem != null)
                    _database?.Messages.WriteRow(eE.NewItem, conversation);

                if (SelectedConversation?.Identifier == e.ConversationId && eE.NewItem != null)
                {
                    int index = -1;
                    for (int i = ActiveConversation.Count - 1; i >= 0; i--)
                        if (ActiveConversation[i].Identifier == eE.OldItemId)
                        {
                            index = i;
                            break;
                        }
                    if (index != -1)
                    {
                        // Keep the grouping info so the message doesn't re-show its author.
                        if (eE.NewItem is Message updated && ActiveConversation[index] is Message old)
                        {
                            updated.PreviousMessageIdentifier = old.PreviousMessageIdentifier;
                            updated.PreviousMessageIsAction = old.PreviousMessageIsAction;
                        }
                        ActiveConversation[index] = eE.NewItem;
                        ConversationItemUpdated?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        #region Unread messages

        // Messages that arrive while their conversation isn't open in the active window are
        // unread: the contact gets a numbered badge and a bold name (Recents and Contacts share
        // the object), and the tray icon becomes the chat bubble until everything is read.

        private void MarkUnread(string conversationId)
        {
            Conversation conversation = FindConversation(conversationId);
            if (conversation == null)
                return;
            conversation.UnreadCount++;
            SaveUnread(conversation);
            RefreshUnreadTray();
        }

        /// <summary>Clears a conversation's unread count (it was opened, or the window came back with it open).</summary>
        public void MarkRead(Conversation conversation)
        {
            if (conversation == null || conversation.UnreadCount == 0)
                return;
            conversation.UnreadCount = 0;
            SaveUnread(conversation);
            RefreshUnreadTray();
        }

        // Saved with the conversation (Skype's unconsumed_normal_messages), so unread messages
        // are still marked after a restart.
        private void SaveUnread(Conversation conversation)
        {
            try
            {
                _database?.Conversations.WriteUnread(conversation.Identifier, conversation.UnreadCount);
            }
            catch (Exception)
            {
                // Only the badge after a restart is lost.
            }
        }

        /// <summary>Call when the main window is activated.</summary>
        public void OnWindowActivated()
        {
            MarkRead(SelectedConversation);
        }

        public void RefreshUnreadTray()
        {
            bool any = ContactList.Any(c => c.UnreadCount > 0) || ConversationList.Any(c => c.UnreadCount > 0);
            Native.Windows.Tray.SetUnread(any); // the tray shows it only while signed in
        }

        #endregion

        /// <summary>A conversation by id, from Recents or the contact list.</summary>
        private Conversation FindConversation(string identifier)
        {
            return ConversationList.FirstOrDefault(c => c.Identifier == identifier)
                ?? (Conversation)ContactList.FirstOrDefault(c => c.Identifier == identifier);
        }

        private void UpdateRecentsListOnNewMessage(
            string conversationId,
            DateTime messageTimestamp
        )
        {
            var conversation = ConversationList.FirstOrDefault(c =>
                c.Identifier == conversationId
            );
            if (conversation == null)
            {
                // First message with this contact: it joins Recents (the same object as in
                // Contacts, so selection, presence and avatar stay in sync).
                conversation = ContactList.FirstOrDefault(c => c.Identifier == conversationId);
                if (conversation == null)
                    return;
                ConversationList.Add(conversation);
            }

            conversation.LastMessageTime = messageTimestamp;
            CompactRecentsRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Message sending

        public async Task SendMessage(string text)
        {
            if (string.IsNullOrEmpty(text) || SelectedConversation == null)
                return;

            StopTyping();

            string tempId = SKYLARPING_SENDING + "/" + Guid.NewGuid().ToString();

            bool action = text.StartsWith("/me ");
            if (action)
                text = text.Substring(4);

            Message preview;
            if (action)
                preview = new ActionMessage(
                    tempId,
                    Universal.CurrentUser,
                    DateTime.Now,
                    text,
                    null,
                    null
                );
            else
                preview = new Message(
                    tempId,
                    Universal.CurrentUser,
                    DateTime.Now,
                    text,
                    null,
                    null
                );

            _pendingPreviewMessages[tempId] = preview;
            ActiveConversation.Add(preview);
            TrimActiveConversation();

            bool sent = false;
            try
            {
                SoundManager.Play("IM_SENT");
                sent = await Universal.Backend.SendMessage(SelectedConversation.Identifier, text, null, null, action);
            }
            catch { }

            if (!sent)
            {
                if (_pendingPreviewMessages.TryGetValue(tempId, out var pending))
                {
                    _pendingPreviewMessages.Remove(tempId);
                    _ = Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            ActiveConversation.Remove(pending);
                        })
                    );
                }
                Universal.ShowMessage("Error sending message.");
            }
        }

        #endregion

        #region Sidebar tab data helpers

        // TODO: Do this via data binding! These helpers are temporary.

        public IList<object> GetConversationList() // this is not async because the conversation list is never lazy-loaded
        {
            return CompactRecentsHelper
                .GroupByDate(ConversationList)
                .Cast<object>()
                .ToList();
        }

        #endregion

        #region Sign out

        public class SignOutRequestedEventArgs : EventArgs
        {
            public bool switchuser { get; }

            public SignOutRequestedEventArgs(bool switchuser)
            {
                this.switchuser = switchuser;
            }
        }

        public void InitiateSignOut(bool switchuser = false)
        {
            // Signing out forgets the profile name so the next start shows the login form;
            // switching user keeps it so the form is prefilled.
            if (!switchuser)
            {
                Settings.ProfileName = string.Empty;
                Settings.Save();
            }
            SoundManager.Play("LOGOUT");
            Universal.SignedIn = false;
            Native.Windows.Tray.SetUnread(false);
            SignOutRequested?.Invoke(this, new SignOutRequestedEventArgs(switchuser));
        }
        #endregion

        public void ShowAddContactWindow()
        {
            new AddContact().ShowWindow();
        }

        public void ShowCallPhones()
        {
            SoundManager.Play("CALL_ERROR1");
            Universal.ShowMessage(NOT_IMPLEMENTED);
        }

        public void InformDND()
        {
            if (Settings.InformDND != true)
                Application.Current.Dispatcher.Invoke(() =>
                    new Dialog(
                        WindowBase.IconType.Information,
                        Universal.Lang["sINFORM_DND"],
                        Universal.Lang["sINFORM_DND_CAP"],
                        Universal.Lang["sINFORM_DND_TITLE"],
                        brText: "OK",
                        cbEnabled: true,
                        onClosing: (s, e) =>
                        {
                            if (((Dialog)s).CheckBox.IsChecked == true)
                            {
                                Settings.InformDND = true;
                                Settings.Save();
                            }
                        }
                    ).ShowDialog()
                );
        }

        public async Task SendFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = Universal.Lang.Format("sF_MULTICHAT_SENDFILE_MULTI_CAPTION", SelectedConversation.DisplayName),
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;
                string fileName = Path.GetFileName(filePath);

                byte[] data = File.ReadAllBytes(filePath);
                Attachment file = new Attachment(data, fileName);
                await Universal.Backend.SendMessage(SelectedConversation.Identifier, null, file);
            }
        }

        public DateTime lastTypingActivity = DateTime.MinValue;

        public void SubscribeTypingIndicator()
        {
            if (_typingIndicatorSubscribed)
                return;
            _typingIndicatorSubscribed = true;
            Universal.Backend.TypingUsersList.CollectionChanged += (s, e) => RefreshTypingState();

            _ = TypingLoop();
        }

        private async Task TypingLoop()
        {
            while (true)
            {
                await TaskEx.Delay(500); // TODO: I don't think this was how it works...
                if ((DateTime.UtcNow - lastTypingActivity).TotalMilliseconds < 500)
                    StartTyping();
            }
        }

        private void RefreshTypingState()
        {
            IChatBackend backend = Universal.Backend;
            if (backend == null)
                return;
            ObservableCollection<User> typing = backend.TypingUsersList;
            int count = typing.Count;
            if (count <= 0)
            {
                Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        IsTypingVisible = false;
                        TypingText = string.Empty;
                    })
                );
                return;
            }
            string text;
            switch (count)
            {
                // TODO: language
                case 1:
                    text = $"{typing[0].DisplayName} is typing...";
                    break;
                case 2:
                    text = $"{typing[0].DisplayName} and {typing[1].DisplayName} are typing...";
                    break;
                case 3:
                    text =
                        $"{typing[0].DisplayName}, {typing[1].DisplayName}, and {typing[2].DisplayName} are typing...";
                    break;
                default:
                    text = "Multiple people are typing...";
                    break;
            }
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    TypingText = text;
                    IsTypingVisible = true;
                })
            );
        }

        public void StopTyping()
        {
            _typingTimer.Change(0, Timeout.Infinite);
            Universal.Backend?.SetTyping(SelectedConversation?.Identifier, false);
        }

        public void StartTyping()
        {
            IChatBackend backend = Universal.Backend;
            if (backend == null)
                return;
            _typingTimer.Change(backend.TypingTimeout, Timeout.Infinite);
            if (!_typingActive)
            {
                backend.SetTyping(SelectedConversation?.Identifier, true);
                _typingRepeatTimer.Change(backend.TypingRepeat, backend.TypingRepeat);
                _typingActive = true;
            }
        }

        private async Task LoadBackend(IChatBackend p)
        {
            p.ListChanged += (o, e) =>
            {
                if (e is ListItemUpdatedEventArgs ubot)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        switch (ubot.List)
                        {
                            case ListType.Contacts:
                                var added = ubot.Item as DirectMessage;
                                ContactList.Add(added);
                                if (added != null && _database != null)
                                {
                                    _database.Contacts.Write(new[] { added });
                                    _database.Conversations.Write(new[] { added });
                                }
                                break;
                            case ListType.Conversations:
                                ConversationList.Add(ubot.Item as Conversation);
                                break;
                        }
                    }));
                }
                else if (e is ListItemRemovedEventArgs rbot)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        switch (rbot.List)
                        {
                            case ListType.Contacts:
                                var toRemove = ContactList.FirstOrDefault(c => c.Identifier == rbot.Identifier);
                                if (toRemove != null)
                                    ContactList.Remove(toRemove);
                                break;
                            case ListType.Conversations:
                                var toRemoveConv = ConversationList.FirstOrDefault(c => c.Identifier == rbot.Identifier);
                                if (toRemoveConv != null)
                                    ConversationList.Remove(toRemoveConv);
                                break;
                        }
                        RefreshUnreadTray(); // a removed contact's unread messages are gone too
                    }));
                }
            };
            var u = await p.GetUserInfo();
            if (string.IsNullOrEmpty(u?.Identifier))
                throw new InvalidOperationException("The chat backend did not return a valid user object to initialize the database.");
            Universal.CurrentUser = u;
            _database = new DatabaseManager(u);
            _database.Accounts.Write(u);
            // Keep the Skype-format Accounts row's picture in step (the backend's avatar.png is the source of truth).
            u.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(User.Avatar) || _database == null) return;
                try { _database.Accounts.Write(u); }
                catch (Exception ex) { Debug.WriteLine("Couldn't save the picture to the database: " + ex); }
            };
            p.AttachDatabase(_database);

            var convs = await p.FetchConversations();
            foreach (var conv in convs)
                ConversationList.Add(conv);
            _database.Conversations.Write(convs);

            var conts = await p.FetchContacts();
            foreach (var cont in conts)
                ContactList.Add(cont);
            _database.Contacts.Write(conts);
            // Messages are saved per conversation row; contacts need one even before they appear in Recents.
            _database.Conversations.Write(conts);

            // Recents: every contact with saved history, by its newest message.
            Dictionary<string, DateTime> lastMessages = _database.Messages.LastMessageTimes();
            Dictionary<string, int> unread = _database.Conversations.UnreadCounts();
            foreach (var cont in conts)
            {
                if (!lastMessages.TryGetValue(cont.Identifier, out DateTime last))
                    continue;
                if (unread.TryGetValue(cont.Identifier, out int count))
                    cont.UnreadCount = count; // still unread from last time
                cont.LastMessageTime = last;
                if (!ConversationList.Any(c => c.Identifier == cont.Identifier))
                    ConversationList.Add(cont);
            }
            RefreshUnreadTray();

            p.IncomingCall += (sender, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IncomingCall ic = new IncomingCall(e);
                    EventHandler handler = null;
                    handler = (s, args) =>
                    {
                        ic.Answered -= handler;
                        IncomingCallAccepted?.Invoke(e);
                    };
                    ic.Answered += handler;
                    ic.Show();
                });
            };
        }

        public bool CheckCallEligibility(Conversation c)
        {
            bool e = c is DirectMessage || c is Group;

            if (!e)
                Universal.ShowMessage("The conversation you are trying to call is of an ineligible type.", "Cannot start call", WindowBase.IconType.GroupCall);
            return e;
        }

        public IEnumerable<(string key, string filename)> GetUniqueEmojiList()
        {
            return EmojiDictionary
                .Map.GroupBy(kvp => kvp.Value)
                .Select(g => g.First())
                .Select(kvp => (kvp.Key, kvp.Value));
        }

        public PresenceStatus GetConnectionStatusFromName(string menuItemName)
        {
            PresenceStatus status;
            switch (menuItemName)
            {
                case "online":
                    status = PresenceStatus.Online;
                    break;
                case "offline":
                    status = PresenceStatus.Offline;
                    break;
                case "invisible":
                    status = PresenceStatus.Invisible;
                    break;
                case "away":
                    status = PresenceStatus.Away;
                    break;
                case "dnd":
                    status = PresenceStatus.DoNotDisturb;
                    break;
                case "call_forwarding":
                    Universal.NotImplemented(
                        Universal.Lang["sF_OPTIONS_PAGE_FORWARDINGANDVOICEMAIL"]
                    );
                    status = PresenceStatus.Unknown;
                    break;
                default:
                    status = PresenceStatus.Unknown;
                    break;
            }
            return status;
        }
    }
}
