/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// What the character says on a call: reply text -> pieces -> text-to-speech -> speaker.
// Pieces are generated one at a time, ahead of what's playing. It also tracks how far each
// reply was heard, so an interrupted one can be cut back to what was actually said.
/*==========================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Skylarping.AI;
using Skylarping.Audio;
using Skylarping.Preferences;

namespace Skylarping.Backend
{
    /// <summary>The voice a character speaks in on a call.</summary>
    public sealed class CallVoice
    {
        /// <summary>A WAV clip to clone, or null.</summary>
        public byte[] Sample;
    }

    public interface ICallSpeaker
    {
        /// <summary>Takes a reply as it's written. Any thread.</summary>
        /// <param name="text">The whole reply so far.</param>
        /// <param name="final">The reply is complete.</param>
        void Speak(string messageId, string text, bool final);

        /// <summary>
        /// Stops speaking at once. Returns what was heard of each reply that wasn't finished,
        /// by message id; an empty string means nothing was.
        /// </summary>
        IDictionary<string, string> Interrupt();
    }

    public sealed class CallSpeech : ICallSpeaker, IDisposable
    {
        private const int PlayedPiecesKept = 6;
        private const int EchoPercent = 70;
        private static readonly Regex Words = new Regex(@"[\w']+", RegexOptions.Compiled);

        private sealed class Reply
        {
            public string Id;
            public readonly SpeechChunker Chunker = new SpeechChunker();
            public string Text = "";
            public bool Final;
            public int Queued, Started;
            public int HeardEnd;
        }

        private sealed class Piece
        {
            public Reply Reply;
            public SpeechChunk Chunk;
            public int Generation;
        }

        private readonly object _lock = new object();
        private readonly List<Reply> _replies = new List<Reply>();
        private readonly HashSet<string> _interrupted = new HashSet<string>(); // their writer may not know yet
        private readonly BlockingCollection<Piece> _pieces = new BlockingCollection<Piece>();
        private readonly CancellationTokenSource _hangUp = new CancellationTokenSource();
        private readonly LlmEndpoint _endpoint = LlmEndpoint.TextToSpeechFromSettings();
        private readonly int _speed = Settings.TtsSpeedPercent;
        private readonly string _sample;
        private readonly SpeechPlayer _player;
        private readonly Func<string, byte[]> _synthesize;
        private readonly Action<string> _onProblem;
        private readonly Queue<string> _played = new Queue<string>(); // the latest pieces, for IsEcho
        private int _generation; // bumped to make everything not yet played stale
        private int _waiting;    // pieces not yet handed to the player

        /// <param name="onEchoFrame">Called with what's being played, for the echo canceller.</param>
        /// <param name="onProblem">Called when speech fails. Not on the UI thread.</param>
        public CallSpeech(CallVoice voice, Action<short[]> onEchoFrame, Action<string> onProblem)
            : this(voice, null, onEchoFrame, onProblem)
        {
        }

        /// <param name="synthesize">Turns text into a WAV file; null asks the server in the settings.</param>
        public CallSpeech(CallVoice voice, Func<string, byte[]> synthesize, Action<short[]> onEchoFrame, Action<string> onProblem)
        {
            _sample = voice == null || voice.Sample == null ? null : Convert.ToBase64String(voice.Sample);
            _synthesize = synthesize ?? (text => OpenAICompatClient.Synthesize(_endpoint, text, _speed, _sample));
            _onProblem = onProblem;
            _player = new SpeechPlayer(onEchoFrame);
            _player.ClipStarted += OnClipStarted;
        }

        public SpeechPlayer Player
        {
            get { return _player; }
        }

        /// <summary>Whether the character is speaking or about to, so the microphone may pick it up.</summary>
        public bool Busy
        {
            get
            {
                lock (_lock)
                    if (_waiting > 0)
                        return true;
                return _player.Busy;
            }
        }

        public void Start()
        {
            _player.Start();
            StartGenerating();
        }

        public void StartGenerating()
        {
            new Thread(GeneratePieces) { IsBackground = true, Name = "Call speech" }.Start();
        }

        public void Speak(string messageId, string text, bool final)
        {
            lock (_lock)
            {
                if (_interrupted.Contains(messageId))
                    return;
                Reply reply = _replies.Find(r => r.Id == messageId);
                if (reply == null)
                {
                    reply = new Reply { Id = messageId };
                    _replies.Add(reply);
                }
                reply.Text = text ?? "";
                reply.Final |= final;
                foreach (SpeechChunk chunk in reply.Chunker.Update(reply.Text, final, _waiting == 0))
                {
                    reply.Queued++;
                    _waiting++;
                    _pieces.Add(new Piece { Reply = reply, Chunk = chunk, Generation = _generation });
                }
                RemoveIfFinished(reply);
            }
        }

        public IDictionary<string, string> Interrupt()
        {
            var heard = new Dictionary<string, string>();
            lock (_lock)
            {
                _generation++;
                _waiting = 0;
                foreach (Reply reply in _replies)
                {
                    heard[reply.Id] = reply.Text.Substring(0, Math.Min(reply.HeardEnd, reply.Text.Length)).Trim();
                    _interrupted.Add(reply.Id);
                }
                _replies.Clear();
                _player.Clear();
            }
            return heard;
        }

        private void GeneratePieces()
        {
            bool reported = false; // one popup per outage, not one for every sentence during it
            try
            {
                foreach (Piece piece in _pieces.GetConsumingEnumerable(_hangUp.Token))
                {
                    if (IsStale(piece))
                        continue;

                    short[] samples = null;
                    try
                    {
                        samples = SpeechAudio.Decode(_synthesize(piece.Chunk.Text));
                        reported = false;
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is LlmException || ex is FormatException || ex is IOException
                            || ex is InvalidOperationException || ex is ArgumentException))
                            throw;
                        if (!reported && !_hangUp.IsCancellationRequested)
                            _onProblem("Couldn't speak the character's reply. " +
                                (ex is LlmException ? ConnectionTest.Explain((LlmException)ex) : ex.Message) +
                                " You can check the settings in Tools > AI Services.");
                        reported = true;
                    }

                    lock (_lock)
                    {
                        if (piece.Generation != _generation)
                            continue;
                        _waiting--;
                        if (samples != null)
                            _player.Enqueue(samples, piece);
                        else
                            MarkStarted(piece); // nothing to hear, but it didn't get cut short either
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Hung up.
            }
        }

        private bool IsStale(Piece piece)
        {
            lock (_lock)
                return piece.Generation != _generation;
        }

        private void OnClipStarted(object tag)
        {
            var piece = (Piece)tag;
            lock (_lock)
                if (piece.Generation == _generation)
                    MarkStarted(piece);
        }

        private void MarkStarted(Piece piece)
        {
            piece.Reply.Started++;
            piece.Reply.HeardEnd = piece.Chunk.End;
            _played.Enqueue(piece.Chunk.Text);
            if (_played.Count > PlayedPiecesKept)
                _played.Dequeue();
            RemoveIfFinished(piece.Reply);
        }

        /// <summary>Whether a transcript is the character's own voice coming back through the microphone.</summary>
        public bool IsEcho(string transcript)
        {
            string played;
            lock (_lock)
                played = string.Join(" ", _played.ToArray());
            return Repeats(played, transcript);
        }

        /// <summary>Whether most of the words in <paramref name="transcript"/> come from <paramref name="spoken"/>.</summary>
        public static bool Repeats(string spoken, string transcript)
        {
            var known = new HashSet<string>();
            foreach (Match word in Words.Matches((spoken ?? "").ToLowerInvariant()))
                known.Add(word.Value);

            int total = 0, matched = 0;
            foreach (Match word in Words.Matches((transcript ?? "").ToLowerInvariant()))
            {
                total++;
                if (known.Contains(word.Value))
                    matched++;
            }
            return total > 0 && matched * 100 >= total * EchoPercent;
        }

        // Once its last piece has started there's nothing left to cut.
        private void RemoveIfFinished(Reply reply)
        {
            if (reply.Final && reply.Started >= reply.Queued)
                _replies.Remove(reply);
        }

        public void Dispose()
        {
            _hangUp.Cancel();
            _player.Dispose();
        }
    }
}
