/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Who is typing, and where. Characters write in the background in any chat, so everyone typing
// is remembered by conversation, and the list the UI shows only holds the open conversation's.
// Everything here runs on the UI thread.
/*==========================================================*/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Skylarping.Models;

namespace Skylarping.Backend
{
    public sealed class TypingTracker
    {
        private readonly ObservableCollection<User> _visible;
        private readonly List<KeyValuePair<string, User>> _typing = new List<KeyValuePair<string, User>>();
        private string _open;

        public TypingTracker(ObservableCollection<User> visible)
        {
            _visible = visible;
        }

        public void Set(string conversationId, User user, bool typing)
        {
            var entry = new KeyValuePair<string, User>(conversationId, user);
            if (typing)
            {
                if (!_typing.Contains(entry))
                    _typing.Add(entry);
                if (conversationId == _open && !_visible.Contains(user))
                    _visible.Add(user);
            }
            else
            {
                _typing.Remove(entry);
                if (conversationId == _open)
                    _visible.Remove(user);
            }
        }

        /// <summary>Everyone in a conversation stops typing.</summary>
        public void Clear(string conversationId)
        {
            _typing.RemoveAll(entry => entry.Key == conversationId);
            if (conversationId == _open)
                _visible.Clear();
        }

        /// <summary>Shows the typing in this conversation, which may have started while it was closed.</summary>
        public void Open(string conversationId)
        {
            _open = conversationId;
            _visible.Clear();
            foreach (KeyValuePair<string, User> entry in _typing)
                if (entry.Key == conversationId)
                    _visible.Add(entry.Value);
        }
    }
}
