/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skylarping.Audio
{
    /// <summary>16-bit mono PCM helpers: the format calls use from the microphone to the STT server.</summary>
    public static class WavFile
    {
        public const int HeaderBytes = 44;

        /// <summary>Joins sample frames into little-endian PCM bytes.</summary>
        public static byte[] ToPcm(IList<short[]> frames)
        {
            int samples = 0;
            foreach (short[] frame in frames)
                samples += frame.Length;

            var pcm = new byte[samples * 2];
            int offset = 0;
            foreach (short[] frame in frames)
            {
                Buffer.BlockCopy(frame, 0, pcm, offset, frame.Length * 2);
                offset += frame.Length * 2;
            }
            return pcm;
        }

        /// <summary>Wraps PCM bytes in a WAV container.</summary>
        public static byte[] FromPcm(byte[] pcm, int sampleRate)
        {
            const short channels = 1, bitsPerSample = 16;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            using (var stream = new MemoryStream(HeaderBytes + pcm.Length))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(HeaderBytes - 8 + pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);                       // size of the fmt chunk
                writer.Write((short)1);                 // PCM
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * blockAlign);  // bytes per second
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);
                writer.Write(pcm);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
