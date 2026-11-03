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
using Skymu.Models;

namespace Skymu.Backend
{
    /// <summary>
    ///  Event arguments covering all types of dialogs the backend can request.
    /// </summary>
    public class DialogRequestEventArgs : EventArgs
    {
        public DialogType Type { get; }
        public string Message { get; }
        public string CopyToClipboardText { get; } // text to copy to clipboard.
        public Func<bool, object> Action { get; }

        public DialogRequestEventArgs(DialogType type, string message)
        {
            Type = type;
            Message = message;
        }

        public DialogRequestEventArgs(DialogType type, string message, string copy_to_clipboard_text)
        {
            Type = type;
            Message = message;
            CopyToClipboardText = copy_to_clipboard_text;
        }

        public DialogRequestEventArgs(DialogType type, string message, Func<bool, object> action)
        {
            Type = type;
            Message = message;
            Action = action;
        }
    }

    /// <summary>
    ///  Abstract event arguments for lists. Do not use these directly when raising ListChanged, use the event arguments that inherit from this base class.
    /// </summary>
    public abstract class ListChangedEventArgs : EventArgs
    {
        public ListType List { get; }

        public ListChangedEventArgs(ListType list)
        {
            List = list;
        }
    }

    /// <summary>
    ///  Used when a list item is positively updated (created or edited, doesn't matter).
    /// </summary>
    public class ListItemUpdatedEventArgs : ListChangedEventArgs
    {
        public Metadata Item { get; }

        public ListItemUpdatedEventArgs(ListType list, Metadata item) : base (list)
        {
            Item = item;
        }
    }

    /// <summary>
    ///  Used when a list item is removed.
    /// </summary>
    public class ListItemRemovedEventArgs : ListChangedEventArgs
    {
        public string Identifier { get; }

        public ListItemRemovedEventArgs(ListType list, string identifier) : base(list)
        {
            Identifier = identifier;
        }
    }

    /// <summary>
    ///  Abstract event arguments for instant messages. Do not use these directly when raising MessagesChanged, use the event arguments that inherit from this base class.
    /// </summary>
    public abstract class MessageEventArgs : EventArgs
    {
        public string ConversationId { get; }

        public MessageEventArgs(string conversation_id)
        {
            ConversationId = conversation_id;
        }
    }

    /// <summary>
    ///  Event arguments used to raise MessagesChanged when an instant message is recieved.
    /// </summary>
    public class MessageReceivedEventArgs : MessageEventArgs
    {
        public ConversationItem Item { get; }

        public MessageReceivedEventArgs(string conversation_id, ConversationItem item)
            : base(conversation_id)
        {
            Item = item;
        }
    }

    /// <summary>
    ///  Event arguments used to raise MessagesChanged when an instant message is edited (modified).
    /// </summary>
    public class MessageEditedEventArgs : MessageEventArgs
    {
        public string OldItemId { get; }
        public ConversationItem NewItem { get; }

        public MessageEditedEventArgs(
            string conversation_id,
            string old_item_id,
            ConversationItem new_item
        )
            : base(conversation_id)
        {
            OldItemId = old_item_id;
            NewItem = new_item;
        }
    }

    /// <summary>
    ///  Event arguments used to raise MessagesChanged when an instant message is deleted.
    /// </summary>
    public class MessageDeletedEventArgs : MessageEventArgs
    {
        public string DeletedItemId { get; }

        public MessageDeletedEventArgs(string conversation_id, string deleted_item_id)
            : base(conversation_id)
        {
            DeletedItemId = deleted_item_id;
        }
    }

    /// <summary>
    ///  Event arguments that signal call state changes. Used for IncomingCall and CallStateChanged.
    /// </summary>
    public class CallEventArgs : EventArgs
    {
        public string ConversationId { get; }
        public CallState State { get; }
        public string FailReason { get; }
        public Conversation Caller { get; }

        public CallEventArgs(string convo_id, CallState state)
        {
            ConversationId = convo_id;
            State = state;
        }

        public CallEventArgs(string convo_id, CallState state, string fail_reason)
        {
            ConversationId = convo_id;
            State = state;
            FailReason = fail_reason;
        }

        public CallEventArgs(Conversation caller, CallState state)
        {
            ConversationId = caller.Identifier;
            State = state;
            Caller = caller;
        }
    }
}
