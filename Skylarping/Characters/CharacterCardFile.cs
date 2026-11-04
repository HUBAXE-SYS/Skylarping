/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Reads character card files:
//  - .json: the card JSON as-is.
//  - .png:  the card JSON lives in a text chunk, base64 of the UTF-8 JSON. V2 uses the
//           keyword "chara", V3 uses "ccv3"; when both are present V3 wins (per the V3
//           spec). The PNG image itself is the character's picture.
// Not supported yet: .charx (a zip with card.json + assets; .NET 4.0 has no zip reader)
// and the discouraged "chara-ext-asset_" PNG asset chunks.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Skylarping.Characters
{
    public sealed class LoadedCard
    {
        public CharacterCard Card { get; internal set; }
        /// <summary>The card's own picture (the PNG file itself), or null for a JSON card.</summary>
        public byte[] Image { get; internal set; }
    }

    public static class CharacterCardFile
    {
        public const string OpenFilter =
            "Character cards (*.png;*.json)|*.png;*.json|PNG character cards (*.png)|*.png|JSON character cards (*.json)|*.json|All files (*.*)|*.*";

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static LoadedCard Load(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                throw new CharacterCardException("The file couldn't be opened: " + ex.Message, ex);
            }
            return Load(bytes, Path.GetExtension(path));
        }

        public static LoadedCard Load(byte[] bytes, string extension = null)
        {
            if (IsPng(bytes))
                return new LoadedCard { Card = CharacterCard.FromJson(ReadPngCardJson(bytes)), Image = bytes };

            if (string.Equals(extension, ".charx", StringComparison.OrdinalIgnoreCase) || (bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K'))
                throw new CharacterCardException("CHARX cards aren't supported yet. Export the card as PNG or JSON instead.");
            if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
                throw new CharacterCardException("WebP cards aren't supported. Export the card as PNG or JSON instead.");

            return new LoadedCard { Card = CharacterCard.FromJson(DecodeText(bytes)) };
        }

        public static bool IsPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < PngSignature.Length)
                return false;
            for (int i = 0; i < PngSignature.Length; i++)
                if (bytes[i] != PngSignature[i])
                    return false;
            return true;
        }

        /// <summary>Returns the card JSON embedded in a PNG card.</summary>
        public static string ReadPngCardJson(byte[] png)
        {
            Dictionary<string, string> chunks = ReadPngText(png);
            string value;
            if (!chunks.TryGetValue("ccv3", out value) && !chunks.TryGetValue("chara", out value))
                throw new CharacterCardException("This picture doesn't contain a character card. " +
                                                 "To use it as the character's picture, choose Change Picture instead.");

            value = value.Trim();
            if (value.StartsWith("{"))
                return value; // a few tools store raw JSON instead of base64
            try
            {
                return DecodeText(Convert.FromBase64String(value));
            }
            catch (FormatException ex)
            {
                throw new CharacterCardException("The character card inside this picture is damaged.", ex);
            }
        }

        /// <summary>
        ///  Reads the tEXt, zTXt and iTXt chunks of a PNG (keyword -> text).
        ///  The first chunk with a given keyword wins.
        /// </summary>
        public static Dictionary<string, string> ReadPngText(byte[] png)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int pos = PngSignature.Length;
            while (pos + 8 <= png.Length)
            {
                long length = ((long)png[pos] << 24) | ((long)png[pos + 1] << 16) | ((long)png[pos + 2] << 8) | png[pos + 3];
                string type = Encoding.ASCII.GetString(png, pos + 4, 4);
                int dataStart = pos + 8;
                if (length < 0 || dataStart + length > png.Length)
                    break; // truncated file: keep what we have
                int len = (int)length;

                try
                {
                    string keyword, text;
                    if (type == "tEXt" && TryReadTEXt(png, dataStart, len, out keyword, out text) ||
                        type == "zTXt" && TryReadZTXt(png, dataStart, len, out keyword, out text) ||
                        type == "iTXt" && TryReadITXt(png, dataStart, len, out keyword, out text))
                    {
                        if (!result.ContainsKey(keyword))
                            result[keyword] = text;
                    }
                }
                catch (Exception)
                {
                    // A broken text chunk shouldn't stop us finding the card in another one.
                }

                if (type == "IEND")
                    break;
                pos = dataStart + len + 4; // skip data + CRC
            }
            return result;
        }

        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static int IndexOfZero(byte[] b, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (b[i] == 0)
                    return i;
            return -1;
        }

        // tEXt: keyword \0 text (Latin-1; base64 card data is plain ASCII anyway)
        private static bool TryReadTEXt(byte[] b, int start, int len, out string keyword, out string text)
        {
            keyword = text = null;
            int end = start + len;
            int z = IndexOfZero(b, start, end);
            if (z < 0)
                return false;
            keyword = Latin1.GetString(b, start, z - start);
            text = Latin1.GetString(b, z + 1, end - z - 1);
            return true;
        }

        // zTXt: keyword \0 method(0) zlib-data
        private static bool TryReadZTXt(byte[] b, int start, int len, out string keyword, out string text)
        {
            keyword = text = null;
            int end = start + len;
            int z = IndexOfZero(b, start, end);
            if (z < 0 || z + 2 > end)
                return false;
            keyword = Latin1.GetString(b, start, z - start);
            text = Latin1.GetString(Inflate(b, z + 2, end - z - 2));
            return true;
        }

        // iTXt: keyword \0 flag method language \0 translated-keyword \0 text (UTF-8, zlib if flag = 1)
        private static bool TryReadITXt(byte[] b, int start, int len, out string keyword, out string text)
        {
            keyword = text = null;
            int end = start + len;
            int z = IndexOfZero(b, start, end);
            if (z < 0 || z + 3 > end)
                return false;
            keyword = Latin1.GetString(b, start, z - start);
            bool compressed = b[z + 1] == 1;
            int lang = IndexOfZero(b, z + 3, end);
            if (lang < 0)
                return false;
            int translated = IndexOfZero(b, lang + 1, end);
            if (translated < 0)
                return false;
            int textStart = translated + 1;
            byte[] raw = compressed ? Inflate(b, textStart, end - textStart) : Slice(b, textStart, end - textStart);
            text = Encoding.UTF8.GetString(raw);
            return true;
        }

        private static byte[] Slice(byte[] b, int start, int len)
        {
            var r = new byte[len];
            Buffer.BlockCopy(b, start, r, 0, len);
            return r;
        }

        // zlib stream = 2-byte header + raw deflate + adler32; DeflateStream wants the raw part.
        private static byte[] Inflate(byte[] b, int start, int len)
        {
            if (len < 2)
                return new byte[0];
            using (var input = new MemoryStream(b, start + 2, len - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);
                return output.ToArray();
            }
        }

        /// <summary>Decodes UTF-8 (with or without BOM); falls back to UTF-16 if there's a UTF-16 BOM.</summary>
        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
