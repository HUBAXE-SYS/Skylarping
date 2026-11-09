/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// The echo canceller's inputs: the microphone and the echo reference have to see the same
// conversion, and the pairing between them has to survive what the devices do.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Skylarping.Audio;

namespace Skylarping.Tests
{
    [TestFixture]
    public class EchoPathTests
    {
        private static short[] Sine(int rate, double hz, int count, double amplitude = 10000)
        {
            var samples = new short[count];
            for (int i = 0; i < count; i++)
                samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate));
            return samples;
        }

        private static double Rms(IEnumerable<short> samples)
        {
            double sum = 0;
            int count = 0;
            foreach (short s in samples)
            {
                sum += (double)s * s;
                count++;
            }
            return Math.Sqrt(sum / Math.Max(1, count));
        }

        private static short[] ConvertAll(RateConverter converter, short[] input, int chunk)
        {
            var output = new List<short>();
            short[] buffer = null;
            for (int at = 0; at < input.Length; at += chunk)
            {
                int count = Math.Min(chunk, input.Length - at);
                var piece = new short[count];
                Array.Copy(input, at, piece, 0, count);
                int produced = converter.Convert(piece, count, ref buffer);
                for (int i = 0; i < produced; i++)
                    output.Add(buffer[i]);
            }
            return output.ToArray();
        }

        [Test]
        public void Converter_KeepsSpeechAndRemovesWhatCantExistAt16kHz()
        {
            short[] voice = ConvertAll(new RateConverter(48000, 16000), Sine(48000, 1000, 48000), 4800);
            short[] hiss = ConvertAll(new RateConverter(48000, 16000), Sine(48000, 12000, 48000), 4800);

            Assert.That(voice.Length, Is.EqualTo(16000).Within(100), "three samples in, one out");
            Assert.That(Rms(voice.Skip(1000)), Is.EqualTo(10000 / Math.Sqrt(2)).Within(300), "1 kHz passes as it was");
            Assert.That(Rms(hiss.Skip(1000)), Is.LessThan(200), "12 kHz can't be in a 16 kHz stream, so it mustn't be");
        }

        [Test]
        public void Converter_TakesAnyRatio()
        {
            short[] voice = ConvertAll(new RateConverter(44100, 16000), Sine(44100, 1000, 44100), 4410);

            Assert.That(voice.Length, Is.EqualTo(16000).Within(100));
            Assert.That(Rms(voice.Skip(1000)), Is.EqualTo(10000 / Math.Sqrt(2)).Within(300));
        }

        [Test]
        public void Converter_SameRateIsUntouched()
        {
            short[] input = Sine(16000, 1000, 3200);

            short[] output = ConvertAll(new RateConverter(16000, 16000), input, 320);

            CollectionAssert.AreEqual(input, output);
        }

        [Test]
        public void Reference_IsWhatTheMicrophonePathMakesOfTheSameSound()
        {
            // Everything the device is given goes through the same converter as the microphone.
            // So converting the device's bytes the microphone's way must give the reference frames.
            var reference = new List<short[]>();
            var random = new Random(7);
            short[] clip = new short[24000 * 2]; // two seconds of noise at the rate speech comes in
            for (int i = 0; i < clip.Length; i++)
                clip[i] = (short)random.Next(-8000, 8000);

            using (var player = new SpeechPlayer(frame => reference.Add((short[])frame.Clone()), 48000))
            {
                player.Enqueue(clip, "clip");
                player.EnqueueSilence(100);
                player.Enqueue(clip, "again");

                var device = new List<short>();
                var bytes = new byte[48000 * 2 / 10]; // 100 ms buffers, as waveOut takes them
                for (int i = 0; i < 45; i++)
                {
                    player.Read(bytes, 0, bytes.Length);
                    var samples = new short[bytes.Length / 2];
                    Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                    device.AddRange(samples);
                }

                var microphone = new List<short[]>();
                var slicer = new FrameSlicer(320);
                short[] heard = ConvertAll(new RateConverter(48000, 16000), device.ToArray(), 4800);
                slicer.Push(heard, heard.Length, microphone.Add);

                Assert.That(reference.Count, Is.GreaterThan(100));
                Assert.That(microphone.Count, Is.EqualTo(reference.Count));
                for (int i = 0; i < reference.Count; i++)
                    CollectionAssert.AreEqual(reference[i], microphone[i], "frame " + i);
                Assert.That(reference.Any(f => f.Any(s => s != 0)), "the clip was in there");
            }
        }

        [Test]
        public void Player_ConvertsClipsToTheDeviceRate()
        {
            using (var player = new SpeechPlayer(frame => { }, 48000))
            {
                player.Enqueue(Enumerable.Repeat((short)1000, 2400).ToArray(), "100 ms"); // at 24 kHz
                var bytes = new byte[48000 * 2 * 120 / 1000]; // 120 ms at 48 kHz
                player.Read(bytes, 0, bytes.Length);
                var samples = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

                Assert.That(samples.Skip(200).Take(4500).All(s => Math.Abs(s - 1000) < 20), "the clip fills the same time at twice the samples");
                Assert.That(samples.Skip(4850).All(s => s == 0), "and then it's over");
            }
        }

        private static EchoQueue SteadyQueue(int lead, int windows)
        {
            // Speakers start with the device full (lead frames ahead), then one frame in, one out.
            var queue = new EchoQueue(320);
            for (int i = 0; i < lead; i++)
                queue.Push(new short[320]);
            for (int i = 0; i < windows * EchoQueue.WindowFrames; i++)
            {
                queue.Next();
                queue.Push(new short[320]);
            }
            return queue;
        }

        [Test]
        public void Queue_IsSilentUntilTheSpeakersStart_ThenHoldsItsDepth()
        {
            var queue = new EchoQueue(320);
            Assert.That(queue.Next(), Is.Null, "nothing to cancel yet");

            queue = SteadyQueue(15, 3);

            Assert.That(queue.Target, Is.EqualTo(15), "the depth as each microphone frame arrives");
            Assert.That(queue.Dropped, Is.EqualTo(0));
            Assert.That(queue.Padded, Is.EqualTo(0));
        }

        [Test]
        public void Queue_DropsWhatTheMicrophoneLost()
        {
            EchoQueue queue = SteadyQueue(15, 3);
            int target = queue.Target;

            for (int i = 0; i < 5; i++) // a 100 ms waveIn buffer never arrives: pushes without takes
                queue.Push(new short[320]);
            for (int i = 0; i < EchoQueue.WindowFrames; i++)
            {
                queue.Next();
                queue.Push(new short[320]);
            }

            Assert.That(queue.Dropped, Is.EqualTo(5));
            Assert.That(queue.Depth, Is.EqualTo(target), "back where it was");

            for (int i = 0; i < EchoQueue.WindowFrames; i++)
            {
                queue.Next();
                queue.Push(new short[320]);
            }
            Assert.That(queue.Dropped, Is.EqualTo(5), "and it stays put");
            Assert.That(queue.Padded, Is.EqualTo(0));
        }

        [Test]
        public void Queue_PadsWhatTheSpeakersFellBehindBy()
        {
            EchoQueue queue = SteadyQueue(15, 3);
            int target = queue.Target;

            for (int i = 0; i < 6; i++) // the device ran dry for 120 ms: takes without pushes
                queue.Next();
            for (int i = 0; i < EchoQueue.WindowFrames; i++)
            {
                queue.Next();
                queue.Push(new short[320]);
            }
            Assert.That(queue.Padded, Is.EqualTo(6));
            Assert.That(queue.Depth, Is.EqualTo(target), "six microphone frames got silence, leaving six real frames in the queue");

            for (int i = 0; i < EchoQueue.WindowFrames; i++)
            {
                queue.Next();
                queue.Push(new short[320]);
            }
            Assert.That(queue.Padded, Is.EqualTo(6), "and it stays put");
            Assert.That(queue.Dropped, Is.EqualTo(0));
        }

        [Test]
        public void Queue_IgnoresTheDeviceBufferSawtooth()
        {
            // waveOut is refilled a buffer at a time: five frames land at once every 100 ms.
            var queue = new EchoQueue(320);
            for (int i = 0; i < 15; i++)
                queue.Push(new short[320]);
            for (int second = 0; second < 5; second++)
                for (int buffer = 0; buffer < 10; buffer++)
                {
                    for (int i = 0; i < 5; i++)
                        queue.Next();
                    for (int i = 0; i < 5; i++)
                        queue.Push(new short[320]);
                }

            Assert.That(queue.Dropped, Is.EqualTo(0));
            Assert.That(queue.Padded, Is.EqualTo(0));
        }

        [Test]
        public void Queue_NeverRunsDryOnceStarted()
        {
            EchoQueue queue = SteadyQueue(2, 1);
            for (int i = 0; i < 10; i++)
                Assert.That(queue.Next(), Is.Not.Null, "silence rather than nothing, so the canceller keeps its place");
        }

        [TestCase(48000, 300, 1000, 700, 300, Description = "a second handed over, 700 ms played: 300 ms left")]
        [TestCase(48000, 300, 1000, 2000, 0, Description = "the clock says it's all gone")]
        [TestCase(48000, 300, 1000, 0, 300, Description = "nothing played yet, but it can't hold more than its buffers")]
        [TestCase(16000, 150, 100, 20, 80)]
        public void Cut_UnplayedComesFromTheClockWithinTheDeviceBuffer(int rate, int latency, int handedMs, int elapsedMs, int expectedMs)
        {
            long handed = (long)handedMs * rate / 1000 * 2;

            long unplayed = SpeechPlayer.UnplayedBytes(handed, elapsedMs, rate, latency);

            Assert.That(unplayed, Is.EqualTo((long)expectedMs * rate / 1000 * 2));
        }

        [Test]
        public void Rates_PreferTheSettingThen48Then44_AndBothDevicesMustAgree()
        {
            Func<int, bool> any = rate => true;
            Func<int, bool> only44 = rate => rate == 44100;
            Func<int, bool> none = rate => false;

            AudioDevices.CallRates rates = AudioDevices.ChooseCallRates(0, any, any);
            Assert.That(rates.Microphone, Is.EqualTo(48000));
            Assert.That(rates.Speakers, Is.EqualTo(48000));

            rates = AudioDevices.ChooseCallRates(44100, any, any);
            Assert.That(rates.Speakers, Is.EqualTo(44100), "the setting wins");

            rates = AudioDevices.ChooseCallRates(0, any, only44);
            Assert.That(rates.Microphone, Is.EqualTo(44100), "the microphone follows the speakers down");
            Assert.That(rates.Matched);

            rates = AudioDevices.ChooseCallRates(0, none, any);
            Assert.That(rates.Microphone, Is.EqualTo(MicrophoneCapture.SampleRate), "each at its own rate, as before");
            Assert.That(rates.Speakers, Is.EqualTo(SpeechAudio.SampleRate));
            Assert.That(rates.Matched, Is.False);
        }

        [Test]
        public void Rates_TheRealDevicesAnswerWithoutOpening()
        {
            Assume.That(MicrophoneCapture.Available, "needs a microphone");

            AudioDevices.CallRates rates = AudioDevices.ChooseCallRates();

            Assert.That(rates.Microphone, Is.GreaterThan(0));
            Assert.That(rates.Speakers, Is.GreaterThan(0));
        }
    }
}
