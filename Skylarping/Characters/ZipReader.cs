/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Reads files out of a zip, for .charx cards. .NET 4.0 has no zip reader, but it does have
// DeflateStream, which leaves only the zip's directory to read here. Stored and deflated files
// only: no encryption, no Zip64 (a card doesn't need 4 GB).
//
// Offsets are measured from the end of the file, so a zip with something in front of it (some
// tools append the zip to a JPEG) reads the same.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Skylarping.Characters
{
    public sealed class ZipReader
    {
        private const uint EndOfDirectorySignature = 0x06054B50;
        private const uint DirectoryEntrySignature = 0x02014B50;
        private const uint LocalHeaderSignature = 0x04034B50;
        private const int EndOfDirectorySize = 22;
        private const int MaxCommentSize = 65535;
        private const int DirectoryEntrySize = 46;
        private const int LocalHeaderSize = 30;
        private const uint Zip64Marker = 0xFFFFFFFF;
        private const int Stored = 0, Deflated = 8;

        /// <summary>The largest file taken out of a zip, so a tiny file can't unpack to fill the memory.</summary>
        public const int MaxFileSize = 64 * 1024 * 1024;

        private sealed class Entry
        {
            public int Method;
            public bool Encrypted;
            public long CompressedSize, Size, HeaderOffset;
        }

        private readonly byte[] _zip;
        private readonly long _shift; // how far in the zip itself starts
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <exception cref="InvalidDataException">It isn't a zip, or it's damaged.</exception>
        public ZipReader(byte[] zip)
        {
            _zip = zip;
            int end = FindEndOfDirectory();
            long directorySize = UInt32At(end + 12), directoryOffset = UInt32At(end + 16);
            if (directorySize == Zip64Marker || directoryOffset == Zip64Marker)
                throw new InvalidDataException("Zip64 isn't supported.");
            _shift = end - directorySize - directoryOffset;
            if (_shift < 0)
                throw new InvalidDataException("The zip's directory is damaged.");

            int count = UInt16At(end + 10);
            long pos = directoryOffset + _shift;
            for (int i = 0; i < count; i++)
            {
                Need(pos, DirectoryEntrySize);
                if (UInt32At((int)pos) != DirectoryEntrySignature)
                    throw new InvalidDataException("The zip's directory is damaged.");
                int at = (int)pos;
                int nameLength = UInt16At(at + 28), extraLength = UInt16At(at + 30), commentLength = UInt16At(at + 32);
                Need(pos + DirectoryEntrySize, nameLength);
                string name = Encoding.UTF8.GetString(_zip, at + DirectoryEntrySize, nameLength);
                _entries[Normalize(name)] = new Entry
                {
                    Encrypted = (UInt16At(at + 8) & 1) != 0,
                    Method = UInt16At(at + 10),
                    CompressedSize = UInt32At(at + 20),
                    Size = UInt32At(at + 24),
                    HeaderOffset = UInt32At(at + 42)
                };
                pos += DirectoryEntrySize + nameLength + extraLength + commentLength;
            }
        }

        public IEnumerable<string> Names
        {
            get { return _entries.Keys; }
        }

        public bool Contains(string name)
        {
            return _entries.ContainsKey(Normalize(name));
        }

        /// <summary>A file's contents. Names ignore case, and "./" or a leading "/".</summary>
        /// <exception cref="FileNotFoundException">There's no such file.</exception>
        /// <exception cref="InvalidDataException">It's damaged, or can't be read.</exception>
        public byte[] Read(string name)
        {
            Entry entry;
            if (!_entries.TryGetValue(Normalize(name), out entry))
                throw new FileNotFoundException("Not in the zip.", name);
            if (entry.Encrypted)
                throw new InvalidDataException("The zip is encrypted.");
            if (entry.Size > MaxFileSize || entry.CompressedSize > MaxFileSize)
                throw new InvalidDataException("A file in the zip is too large.");

            long header = entry.HeaderOffset + _shift;
            Need(header, LocalHeaderSize);
            if (UInt32At((int)header) != LocalHeaderSignature)
                throw new InvalidDataException("The zip is damaged.");
            long data = header + LocalHeaderSize + UInt16At((int)header + 26) + UInt16At((int)header + 28);
            Need(data, entry.CompressedSize);

            if (entry.Method == Stored)
            {
                var stored = new byte[entry.CompressedSize];
                Buffer.BlockCopy(_zip, (int)data, stored, 0, stored.Length);
                return stored;
            }
            if (entry.Method != Deflated)
                throw new InvalidDataException("The zip uses a compression method that isn't supported.");

            using (var input = new MemoryStream(_zip, (int)data, (int)entry.CompressedSize))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > MaxFileSize)
                        throw new InvalidDataException("A file in the zip is too large.");
                }
                return output.ToArray();
            }
        }

        private static string Normalize(string name)
        {
            name = name.Replace('\\', '/');
            while (name.StartsWith("./", StringComparison.Ordinal))
                name = name.Substring(2);
            return name.TrimStart('/');
        }

        private int FindEndOfDirectory()
        {
            int last = _zip.Length - EndOfDirectorySize;
            int first = Math.Max(0, last - MaxCommentSize);
            for (int i = last; i >= first; i--)
                if (_zip[i] == 0x50 && _zip[i + 1] == 0x4B && UInt32At(i) == EndOfDirectorySignature)
                    return i;
            throw new InvalidDataException("It isn't a zip file.");
        }

        private void Need(long start, long length)
        {
            if (start < 0 || length < 0 || start + length > _zip.Length)
                throw new InvalidDataException("The zip is damaged.");
        }

        private int UInt16At(int at)
        {
            return _zip[at] | (_zip[at + 1] << 8);
        }

        private uint UInt32At(int at)
        {
            return (uint)(_zip[at] | (_zip[at + 1] << 8) | (_zip[at + 2] << 16)) | ((uint)_zip[at + 3] << 24);
        }
    }
}
