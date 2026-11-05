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
using System.ComponentModel;

namespace Skylarping.Models
{
    public abstract class Metadata : INotifyPropertyChanged
    {
        private string _displayName;
        private string _description;
        private byte[] _avatar;

        public string Identifier { get; set; }

        public string DisplayName
        {
            get => _displayName;
            set => Set(ref _displayName, value, nameof(DisplayName));
        }

        public byte[] Avatar
        {
            get => _avatar;
            set => Set(ref _avatar, value, nameof(Avatar));
        }

        public string Description
        {
            get => _description;
            set => Set(ref _description, value, nameof(Description));
        }

        protected Metadata(string displayName, string identifier, byte[] avatar = null, string description = null)
        {
            _displayName = displayName;
            Identifier = identifier;
            _avatar = avatar;
            _description = description;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, string name)
        {
            if (Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public abstract class Participant : Metadata
    {
        protected Participant(string displayName, string identifier, byte[] avatar = null)
            : base(displayName, identifier, avatar) { }
    }

    public class User : Participant
    {
        private string _status;
        private string _username;
        private PresenceStatus _presence_status;

        public string Status
        {
            get => _status;
            set => Set(ref _status, value, nameof(Status));
        }

        public string Username
        {
            get => _username;
            set => Set(ref _username, value, nameof(Username));
        }

        public PresenceStatus ConnectionStatus
        {
            get => _presence_status;
            set => Set(ref _presence_status, value, nameof(ConnectionStatus));
        }

        public User(
            string display_name,
            string username,
            string identifier,
            string status = null,
            PresenceStatus presence_status = PresenceStatus.Offline,
            byte[] avatar = null
        )
            : base(display_name, identifier, avatar)
        {
            _username = username;
            _status = status;
            _presence_status = presence_status;
        }
    }

    public abstract class Conversation : Metadata
    {
        private int _unreadCount;
        private DateTime _lastMessageTime;

        public int UnreadCount
        {
            get => _unreadCount;
            set => Set(ref _unreadCount, value, nameof(UnreadCount));
        }

        public DateTime LastMessageTime
        {
            get => _lastMessageTime;
            set => Set(ref _lastMessageTime, value, nameof(LastMessageTime));
        }

        protected Conversation(
            string display_name,
            string identifier,
            int unread_count,
            byte[] profile_picture = null,
            DateTime? last_message_time = null,
            string description = null
        )
            : base(display_name, identifier, profile_picture, description)
        {
            _unreadCount = unread_count;
            _lastMessageTime = last_message_time ?? DateTime.Now;
        }
    }

    public class DirectMessage : Conversation
    {
        public User Partner { get; }

        public DirectMessage(
            User partner,
            int unread_count,
            string identifier,
            DateTime? last_message_time = null
        )
            : base(
                partner.DisplayName,
                identifier,
                unread_count,
                partner.Avatar,
                last_message_time
            )
        {
            Partner = partner;
        }
    }

    public class Group : Conversation
    {
        private User[] _members;

        public User[] Members
        {
            get => _members;
            set => Set(ref _members, value, nameof(Members));
        }

        public Group(
            string name,
            string identifier,
            int unread_count,
            User[] members,
            byte[] profile_picture = null,
            DateTime? last_message_time = null
        )
            : base(name, identifier, unread_count, profile_picture, last_message_time)
        {
            _members = members;
        }
    }

    public class Attachment
    {
        public string Name { get; set; }
        public byte[] File { get; set; }
        public string Url { get; set; }
        public AttachmentType Type { get; set; }

        public Attachment(byte[] file, string name)
        {
            File = file;
            Name = name;
            Type = AttachmentType.File;
        }

        public Attachment(byte[] file, string name, string url, AttachmentType type)
        {
            File = file;
            Name = name;
            Url = url;
            Type = type;
        }

        public Attachment(string location_url, string name)
        {
            Url = location_url;
            Name = name;
        }
    }

    public abstract class ConversationItem
    {
        public DateTime Time { get; set; } // Time when the item was sent. If your server API returns send_started and send_completed (for example) use send_completed.
        public string Identifier { get; set; } // Unique identifier for the item
        public string ConversationId { get; set; } // Identifier of the conversation that the item is in
    }

    public class Message : ConversationItem
    {
        public User Author { get; set; } // Who sent the message
        public string Text { get; set; } // Message body
        public Attachment[] Attachments { get; set; } // Media or files attached to the message
        public Message ParentMessage { get; set; } // Parent message, if applicable (e.g. this message is a reply to another message)
        public bool IsForwarded { get; set; }
        public MentionType MentionType { get; set; }

        public string PreviousMessageIdentifier { get; set; } // TODO: TO BE REMOVED!!
        public bool PreviousMessageIsAction { get; set; } // TODO: REMOVE THIS TOO!!!

        public Message(
            string identifier,
            User author,
            DateTime time,
            string text = null,
            Attachment[] attachments = null,
            Message parent_message = null,
            bool is_forwarded = false,
            MentionType mention_type = MentionType.None
        )
        {
            Identifier = identifier;
            Author = author;
            Text = text;
            Time = time;
            Attachments = attachments;
            ParentMessage = parent_message;
            IsForwarded = is_forwarded;
            MentionType = mention_type;
        }
    }

    public class ActionMessage : Message
    {
        public ActionMessage(
            string identifier,
            User sender,
            DateTime time,
            string text = null,
            Attachment[] attachments = null,
            Message parent_message = null,
            bool is_forwarded = false
        )
            : base(identifier, sender, time, text, attachments, parent_message, is_forwarded) { }
    }

    /// <summary>Someone joined or left a group chat.</summary>
    public class GroupNotice : ConversationItem
    {
        public User Who { get; set; }
        public bool Joined { get; set; }

        public string Text => (Who?.DisplayName ?? "Someone") + (Joined ? " joined the group." : " left the group.");

        public GroupNotice(string identifier, User who, bool joined, DateTime time)
        {
            Identifier = identifier;
            Who = who;
            Joined = joined;
            Time = time;
        }
    }

    public class CallStartedNotice : ConversationItem
    {
        public User StartedBy { get; set; }

        public CallStartedNotice(User started_by, DateTime time)
        {
            StartedBy = started_by;
            Time = time;
        }
    }

    public class CallEndedNotice : ConversationItem
    {
        public User StartedBy { get; set; }
        public TimeSpan Duration { get; set; } // Length of call

        public CallEndedNotice(
            User started_by,
            TimeSpan duration,
            DateTime time
        ) // time here is when the "Call ended" notification was sent, not when call started
        {
            StartedBy = started_by;
            Duration = duration;
            Time = time;
        }
    }

    public class ClickableConfiguration
    {
        public string DelimiterLeft { get; set; } // left delimiter for clickable item, e.g. '<@', '@'.
        public string DelimiterRight { get; set; } // right delimiter for clickable item, e.g. '>'. Space means left-only delimitation in practice.
        public ClickableItemType Type { get; set; } // items that are clickable within the clickability delimiter range

        public ClickableConfiguration(
            ClickableItemType type,
            string delimiter_left,
            string delimiter_right
        )
        {
            DelimiterLeft = delimiter_left;
            DelimiterRight = delimiter_right;
            Type = type;
        }
    }

    public class ActiveCall
    {
        public string CallId { get; }
        public string ConversationId { get; }
        public CallState State { get; set; }
        public DateTime StartedAt { get; }
        public User[] Participants { get; set; }

        public ActiveCall(
            string call_id,
            string conversation_id,
            User[] participants
        )
        {
            CallId = call_id;
            ConversationId = conversation_id;
            StartedAt = DateTime.UtcNow;
            Participants = participants;
            State = CallState.Ringing;
        }
    }
}
