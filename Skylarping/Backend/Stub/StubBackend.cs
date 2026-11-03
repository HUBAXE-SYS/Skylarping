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
using Skylarping.Models;

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

        private SynchronizationContext _uiContext;
        private Conversation _currentConversation;
        private User Me;

        #endregion

        public StubBackend()
        {
            users = new User[]
            {
                new User("Mario", "mario", "012", "It's-a me!", PresenceStatus.Online),
                new User("Luigi", "luigi", "013", "NO", PresenceStatus.DoNotDisturb),
                new User("Peach", "peach", "014", "In the castle", PresenceStatus.Away),
                new User("Bowser", "bowser", "015", "Planning something...", PresenceStatus.Online),
                new User("Yoshi", "yoshi", "016", "Yoshi!", PresenceStatus.Online),
                new User("Toad", "toad", "017", "Welcome!", PresenceStatus.Online),
                new User("Wario", "wario", "018", "Hehehe", PresenceStatus.DoNotDisturb),
                new User("Waluigi", "waluigi", "019", "Wah!", PresenceStatus.Invisible),
                new User("Daisy", "daisy", "020", "Hi!", PresenceStatus.Online),
                new User("Rosalina", "rosalina", "021", "Watching the stars", PresenceStatus.Away),
                new User("Donkey Kong", "dk", "022", "Bananas!", PresenceStatus.Online),
                new User("Koopa", "koopa", "023", "Patrolling", PresenceStatus.Offline),
            };
            _echo = new EchoService(
                () => Me,
                e => MessagesChanged?.Invoke(this, e),
                e => CallStateChanged?.Invoke(this, e),
                TypingUsersList);
        }

        // echo123 is real (it talks to the configured language model); everything else here is canned.
        private readonly EchoService _echo;

        public Task<bool> SignIn(string display_name)
        {
            Me = new User(display_name, display_name, display_name);
            MessagesChanged?.Invoke(
                this,
                new MessageReceivedEventArgs(
                    "13414",
                    new Message("20202", users[0], new DateTime(2025, 4, 30, 8, 14, 0), "Hello")
                )
            );
            return TaskEx.FromResult(true);
        }

        // Also called on logout
        public void Dispose()
        {
            _echo.Dispose();
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
            TypingUsersList.Clear();
            TypingUsersList.Add(new User("Nova", "20202", "20202"));
            TypingUsersList.Add(new User("omega", "20203", "20203"));
            TypingUsersList.Add(new User("patricktbp", "20204", "20204"));
            TypingUsersList.Add(new User("Xaero", "20200", "20200"));
            TypingUsersList.Add(new User("HUBAXE", "20205", "20205"));

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
            DialogRequested?.Invoke(this, new DialogRequestEventArgs(DialogType.Warning, "Message editing is not implemented."));
            return TaskEx.FromResult(false);
        }

        public Task<bool> DeleteMessage(
            string conversationId,
            string messageId
        )
        {
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
            List<ConversationItem> messages = new List<ConversationItem>();

            #region Dummy messages (Imported)

            messages.Add(
                new Message(
                    "20202",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 10, 0),
                    "Hey, I’ve been playing Genshin Impact on the Steam Deck, it works fine."
                )
            );
            messages.Add(
                new Message(
                    "20203",
                    new User("omega", "omega", "omega"),
                    new DateTime(2025, 4, 30, 8, 10, 10),
                    "Oh nice, I’ve heard good things about it."
                )
            );
            messages.Add(
                new Message(
                    "20204",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 10, 20),
                    "Yeah, it’s a really fun game."
                )
            );
            messages.Add(
                new Message(
                    "20205",
                    new User("omega", "omega", "omega"),
                    new DateTime(2025, 4, 30, 8, 10, 30),
                    "Cool, I might try it out sometime."
                )
            );
            messages.Add(
                new Message(
                    "20206",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 10, 40),
                    "It’s pretty enjoyable even without spending money."
                )
            );
            messages.Add(
                new Message(
                    "20207",
                    new User("omega", "omega", "omega"),
                    new DateTime(2025, 4, 30, 8, 10, 50),
                    "That’s good to know."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 11, 0),
                    "I just wanted to share it’s a solid game."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("omega", "omega", "omega"),
                    new DateTime(2025, 4, 30, 8, 11, 10),
                    "Thanks for the info!"
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 11, 20),
                    "Gameplay-wise it’s really engaging and well-designed."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("patricktbp", "patricktbp", "patricktbp"),
                    new DateTime(2025, 4, 30, 8, 12, 40),
                    "Sounds interesting, I’ll check it out."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("patricktbp", "patricktbp", "patricktbp"),
                    new DateTime(2025, 4, 30, 8, 13, 30),
                    "@Amongus do you want to discuss this more in DMs?"
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 14, 0),
                    "Just sharing my experience, I think most people would enjoy it."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("Nova", "Nova", "Nova"),
                    new DateTime(2025, 4, 30, 8, 15, 0),
                    "I think it could be fun to collaborate on the project with this in mind."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("omega", "omega", "omega"),
                    new DateTime(2025, 4, 30, 8, 15, 20),
                    "Yeah, that makes sense. Thanks for sharing."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("patricktbp", "patricktbp", "patricktbp"),
                    new DateTime(2025, 4, 30, 8, 15, 30),
                    "Great, let’s move forward."
                )
            );
            messages.Add(
                new Message(
                    "20202",
                    new User("Amongus", "Amongus", "Amongus"),
                    new DateTime(2025, 4, 30, 8, 15, 40),
                    "Got it, thanks everyone. Also, Genshin impact fuckin sucks ass lol"
                )
            );

            #endregion

            return TaskEx.FromResult(messages);
        }

        public Task<User> GetUserInfo()
        {
            _uiContext = SynchronizationContext.Current;
            Me.Status = "Need an Attorney? Better Call Saul! (505) 503-4455";
            Me.ConnectionStatus = PresenceStatus.Online;
            return TaskEx.FromResult(Me);
        }

        public Task<List<DirectMessage>> FetchContacts()
        {
            List<DirectMessage> contacts = new List<DirectMessage>
            {
                _echo.CreateContact(),
                new DirectMessage(
                    new User("Skylarping user 1", "u1", "u1", "hi skmuuymu", PresenceStatus.Online),
                    10,
                    "u1"
                ),
                new DirectMessage(
                    new User("Skylarping user 2", "u2", "u2", "HELLO", PresenceStatus.Away),
                    0,
                    "u2"
                )
            };
            return TaskEx.FromResult(contacts);
        }

        public Task<List<Conversation>> FetchConversations()
        {
            List<Conversation> conversations = new List<Conversation>();

            int dayOffset = 0;
            foreach (var user in users)
            {
                DateTime messageTime;
                if (dayOffset <= 2)
                {
                    messageTime = DateTime.Now.AddMinutes(-rand.Next(1, 360));
                }
                else if (dayOffset == 3)
                {
                    messageTime = DateTime.Now.AddDays(-1).AddHours(-rand.Next(0, 12));
                }
                else
                {
                    messageTime = DateTime
                        .Now.AddDays(-(dayOffset - 2))
                        .AddHours(-rand.Next(0, 12));
                }
                conversations.Add(
                    new DirectMessage(
                        user,
                        rand.Next(0, 5),
                        rand.Next(100, 5000).ToString(),
                        messageTime
                    )
                );
                dayOffset++;
            }

            conversations.Add(
                new Group(
                    "Giga based coalition",
                    "067",
                    users.Length,
                    users,
                    null,
                    DateTime.Now.AddHours(-1)
                )
            );

            if (presenceTimer == null)
                presenceTimer = new Timer(UpdatePresence, null, 0, 500);

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
            return TaskEx.FromResult(true);
        }

        public Task<bool> SetConnectionStatus(PresenceStatus status)
        {
            Me.ConnectionStatus = status;
            return TaskEx.FromResult(true);
        }

        public int TypingTimeout => 5000;
        public int TypingRepeat => 9000;
        public Task<bool> SetTyping(string idenfitier, bool typing)
        {
            return TaskEx.FromResult(false);
        }

        #region Calls

        private WaveOutEvent _out;

        private WaveOutEvent WaveDispenser()
        {
            return new WaveOutEvent();
        }

        private TaskCompletionSource<bool> _waiter;

        // Call will be picked up as soon as something is returned
        public async Task<ActiveCall> StartCall(string convo_id, bool start_muted)
        {
            if (EchoService.Handles(convo_id))
                return await _echo.StartCall(); // start_muted ignored: a muted sound test records nothing

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

            if (!cont) return null;

            return new ActiveCall(
                "STUBCALL",
                convo_id,
                new User[0]
            );
        }

        public Task<bool> EndCall(ActiveCall call)
        {
            _echo.EndCall(); // also cancels an echo call that's still ringing
            _waiter?.TrySetResult(false);
            _out?.Stop();
            _out?.Dispose();
            _out = null;
            return TaskEx.FromResult(true);
        }

        public async Task<ActiveCall> AnswerCall(string convo_id) => await StartCall(convo_id, true);

        public Task<bool> DeclineCall(string convo_id) => TaskEx.FromResult(false);

        public Task<bool> SetMuted(ActiveCall call, bool muted)
        {
            if (call != null && EchoService.Handles(call.ConversationId))
            {
                _echo.SetMuted(muted);
                return TaskEx.FromResult(true);
            }
            return TaskEx.FromResult(false);
        }

        public event EventHandler<CallEventArgs> IncomingCall;
        public event EventHandler<CallEventArgs> CallStateChanged;
        #endregion

        #region Adding contacts
        // Enter number for the search query for group with that amount of members

        public Task<Metadata[]> FindNewContact(string query)
        {
            if (int.TryParse(query, out var memc))
            {
                var members = new User[memc];
                for (int i = 0; i < memc; i++)
                {
                    members[i] = users[i % users.Length];
                }
                return TaskEx.FromResult(new Metadata[2] {
                    new Group("Mega Based Coalition", "mbc", 0, members),
                    new User(query, query, query)
                });
            }
            return TaskEx.FromResult(new Metadata[1]
            {
                new User(query, query, query)
            });
        }

        public Task<bool> AddContact(Metadata contact, string message)
        {
            if (contact is User user)
                ListChanged?.Invoke(this, new ListItemUpdatedEventArgs(ListType.Contacts, new DirectMessage(user, 0, user.Identifier)));
            else if (contact is Group group)
                ListChanged?.Invoke(this, new ListItemUpdatedEventArgs(ListType.Conversations, group));
            else
                return TaskEx.FromResult(false);
            return TaskEx.FromResult(true);

        }

        #endregion

        #region Stub specific stuff

        private readonly User[] users;

        private Timer presenceTimer;
        private readonly Random rand = new Random();

        private readonly string[] randomTexts = new string[]
        {
            "It's-a me, Mario!",
            "Let's-a go!",
            "Mamma mia!",
            "Here we go!",
            "Just jumped on a Goomba",
            "Looking for Princess Peach",
            "Time to save the kingdom",
            "Collecting coins",
            "Found a Super Mushroom",
            "Jumping through pipes",
            "Watch out for Bowser",
            "Yahoo!",
            "Wahoo!",
            "On my way to the castle",
        };

        private void UpdatePresence(object state)
        {
            foreach (var user in users)
                RandomizeUser(user);
        }

        private void RandomizeUser(User user)
        {
            Array values = Enum.GetValues(typeof(PresenceStatus));
            var newStatus = (PresenceStatus)values.GetValue(rand.Next(values.Length));
            var newText = randomTexts[rand.Next(randomTexts.Length)];

            _uiContext?.Post(
                _ =>
                {
                    user.ConnectionStatus = newStatus;
                    user.Status = newText;
                },
                null
            );
        }

        #endregion
    }
}
