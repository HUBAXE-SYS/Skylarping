/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// echo123, "Echo / Sound Test Service": the built-in test contact, as in Skype.
//  - Call it: after the beep it records up to 10 seconds from the microphone and plays
//    it back, like Skype's echo test (the spoken prompt is shown as a chat message
//    until text-to-speech exists).
//  - Chat with it: the message goes to the configured language model, so a reply
//    proves the AI connection works end to end. Errors come back as Echo's reply.
// Kept separate from the stub so the real character backend can host it too.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Models;
using Skylarping.Preferences;
using Skylarping.Sounds;

namespace Skylarping.Backend
{
    internal sealed class EchoService : IDisposable
    {
        public const string Identifier = "echo123";
        public const string DisplayName = "Echo / Sound Test Service";

        private const int RecordSeconds = 10;
        private const int MaxChatHistory = 20; // LLM turns kept for context
        private const string SystemPrompt =
            "You are \"Echo / Sound Test Service\" (echo123), the built-in test contact in Skylarping, " +
            "a Skype-style chat app whose contacts are AI characters. People message you to check that " +
            "their AI connection works. Reply in one to three short, friendly sentences. If asked, explain " +
            "that calling you tests their microphone and speakers.";

        private readonly Func<User> _me;
        private readonly Action<MessageEventArgs> _raiseMessage;
        private readonly Action<CallEventArgs> _raiseCallState;
        private readonly TypingTracker _typing;

        private readonly object _lock = new object();
        private readonly List<ConversationItem> _items = new List<ConversationItem>();
        private readonly List<LlmChatMessage> _chat = new List<LlmChatMessage>();

        private CancellationTokenSource _callCts;
        private volatile bool _muted;

        public EchoService(Func<User> me, Action<MessageEventArgs> raiseMessage, Action<CallEventArgs> raiseCallState,
            TypingTracker typing)
        {
            _me = me;
            _raiseMessage = raiseMessage;
            _raiseCallState = raiseCallState;
            _typing = typing;
            User = new User(DisplayName, Identifier, Identifier, "", PresenceStatus.Online);
        }

        public User User { get; private set; }

        public DirectMessage CreateContact() { return new DirectMessage(User, 0, Identifier); }

        public static bool Handles(string conversationId) { return conversationId == Identifier; }

        public List<ConversationItem> FetchMessages()
        {
            lock (_lock)
                return _items.ToList();
        }

        #region Chat

        /// <summary>Forgets the chat: shown messages and what the model is sent as context.</summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _items.Clear();
                _chat.Clear();
            }
        }

        public bool SendMessage(string text, bool action)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            // Confirm our own message (the UI swaps its "sending" preview for this).
            User me = _me();
            DateTime now = DateTime.Now;
            Post(action ? new ActionMessage(NewId(), me, now, text) : new Message(NewId(), me, now, text));

            SynchronizationContext ui = SynchronizationContext.Current;
            SetTyping(ui, true);
            TaskEx.Run(() =>
            {
                string reply = Ask(action ? "*" + me.DisplayName + " " + text + "*" : text);
                SetTyping(ui, false);
                Post(new Message(NewId(), User, DateTime.Now, reply));
            });
            return true;
        }

        private string Ask(string text)
        {
            if (!Settings.LlmConfigured)
                return "Hello! I'm the Echo / Sound Test Service. No language model is set up yet, so your characters " +
                       "can't reply. Choose Tools > Connection Wizard... to set one up, then message me again.";

            List<LlmChatMessage> request;
            lock (_lock)
            {
                _chat.Add(LlmChatMessage.FromUser(text));
                while (_chat.Count > MaxChatHistory)
                    _chat.RemoveAt(0);
                request = new List<LlmChatMessage> { LlmChatMessage.FromSystem(SystemPrompt) };
                request.AddRange(_chat);
            }

            try
            {
                LlmChatResult result = OpenAICompatClient.Chat(LlmEndpoint.FromSettings(), request);
                string reply = result.Content;
                if (string.IsNullOrEmpty(reply))
                    reply = result.FinishReason == "length"
                        ? "(The model used up its reply length before answering. It may be spending it all on thinking.)"
                        : "(The model sent an empty reply.)";
                else
                    lock (_lock)
                        _chat.Add(LlmChatMessage.FromAssistant(reply));
                return reply;
            }
            catch (LlmException ex)
            {
                lock (_lock)
                    _chat.RemoveAt(_chat.Count - 1); // don't keep the unanswered turn
                string what = ex.Kind == LlmErrorKind.Unreachable || ex.Kind == LlmErrorKind.InvalidUrl
                    ? "I couldn't reach your language model. "
                    : "Your language model didn't answer. ";
                return what + ConnectionTest.Explain(ex) +
                       " You can check the settings in Tools > AI Services.";
            }
        }

        private void SetTyping(SynchronizationContext ui, bool typing)
        {
            if (ui == null)
                return;
            ui.Post(_ => _typing.Set(Identifier, User, typing), null);
        }

        #endregion

        #region Sound test call

        public async Task<ActiveCall> StartCall()
        {
            EndCall();
            var cts = new CancellationTokenSource();
            _callCts = cts;
            _muted = false;

            // Let it ring briefly, then "pick up".
            try { await TaskEx.Delay(2000, cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (cts.IsCancellationRequested)
                return null;

            var call = new ActiveCall("echo123-" + Guid.NewGuid().ToString("N"), Identifier, new[] { User });
            var thread = new Thread(() => RunSoundTest(cts.Token)) { IsBackground = true, Name = "Echo sound test" };
            thread.Start();
            return call;
        }

        public void EndCall()
        {
            CancellationTokenSource cts = _callCts;
            _callCts = null;
            if (cts != null)
                cts.Cancel();
        }

        public void SetMuted(bool muted) { _muted = muted; }

        private void RunSoundTest(CancellationToken token)
        {
            try
            {
                if (WaveInEvent.DeviceCount == 0)
                {
                    Say("I can't find a microphone on this computer, so there's nothing to record. " +
                        "Plug one in (or check Windows' sound settings) and call me again.");
                    return;
                }

                Say("Hello, welcome to the Skylarping sound test service. After the beep, please record a message " +
                    "of up to " + RecordSeconds + " seconds. Your message will then be played back to you.");
                if (!Wait(token, 2500))
                    return;

                SoundManager.PlaySynchronous("VC_BEEP_1");
                var format = new WaveFormat(16000, 16, 1);
                byte[] recording = Record(format, token);
                if (recording == null)
                    return;
                SoundManager.PlaySynchronous("VC_BEEP_1");
                if (!Wait(token, 500))
                    return;

                if (!Play(recording, format, token))
                    return;

                Say("That was your message. If you heard yourself, your microphone and speakers are working. " +
                    "If you didn't, check the devices in Windows' sound settings.");
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Say("Sorry, the sound test failed: " + ex.Message);
            }
            finally
            {
                // Hang up from our side when done (no-op if the user already did).
                if (!token.IsCancellationRequested)
                {
                    EndCall();
                    _raiseCallState(new CallEventArgs(Identifier, CallState.Ended));
                }
            }
        }

        private byte[] Record(WaveFormat format, CancellationToken token)
        {
            using (var buffer = new MemoryStream())
            using (var input = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100, DeviceNumber = AudioDevices.Microphone })
            {
                var stopped = new ManualResetEvent(false);
                input.DataAvailable += (s, e) =>
                {
                    if (_muted)
                        buffer.Write(new byte[e.BytesRecorded], 0, e.BytesRecorded); // keep timing, record silence
                    else
                        buffer.Write(e.Buffer, 0, e.BytesRecorded);
                };
                input.RecordingStopped += (s, e) => stopped.Set();
                input.StartRecording();
                Wait(token, RecordSeconds * 1000);
                input.StopRecording();
                stopped.WaitOne(2000);
                return token.IsCancellationRequested ? null : buffer.ToArray();
            }
        }

        private static bool Play(byte[] recording, WaveFormat format, CancellationToken token)
        {
            using (var stream = new RawSourceWaveStream(new MemoryStream(recording), format))
            using (var output = AudioDevices.CreateOutput())
            {
                output.Init(stream);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing)
                {
                    if (token.IsCancellationRequested)
                    {
                        output.Stop();
                        return false;
                    }
                    Thread.Sleep(50);
                }
            }
            return !token.IsCancellationRequested;
        }

        /// <summary>Sleeps, returning false early if the call was hung up.</summary>
        private static bool Wait(CancellationToken token, int milliseconds)
        {
            return !token.WaitHandle.WaitOne(milliseconds);
        }

        #endregion

        private void Say(string text)
        {
            Post(new Message(NewId(), User, DateTime.Now, text));
        }

        private void Post(ConversationItem item)
        {
            lock (_lock)
                _items.Add(item);
            _raiseMessage(new MessageReceivedEventArgs(Identifier, item));
        }

        private static string NewId() { return "echo123/" + Guid.NewGuid().ToString("N"); }

        public void Dispose()
        {
            EndCall();
        }
    }
}
