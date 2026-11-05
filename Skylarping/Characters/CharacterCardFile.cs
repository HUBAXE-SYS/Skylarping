/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Reads and writes character card files:
//  - .json:  the card JSON as-is.
//  - .png:   the card JSON lives in a text chunk, base64 of the UTF-8 JSON. V2 uses the
//            keyword "chara", V3 uses "ccv3"; when both are present V3 wins (per the V3
//            spec). The PNG image itself is the character's picture. We write both, so
//            tools that only know V2 can read our cards.
//  - .charx: a zip with card.json at its root and the assets beside it (read only). The
//            picture is the asset of type icon named main.
// Not supported: WebP cards (.NET 4.0 can't read WebP, on XP least of all) and the
// discouraged "chara-ext-asset_" PNG asset chunks. Other CHARX assets aren't kept.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Skylarping.Characters
{
    public sealed class LoadedCard
    {
        public CharacterCard Card { get; internal set; }
        /// <summary>The card's own picture (the PNG file, or a CHARX icon), or null if it has none.</summary>
        public byte[] Image { get; internal set; }
    }

    public static class CharacterCardFile
    {
        public const string OpenFilter =
            "Character cards (*.png;*.json;*.charx)|*.png;*.json;*.charx|PNG character cards (*.png)|*.png|JSON character cards (*.json)|*.json|CHARX character cards (*.charx)|*.charx|All files (*.*)|*.*";

        public const string SaveFilter = "PNG character card (*.png)|*.png|JSON character card (*.json)|*.json";

        private const string EmbeddedUri = "embeded://"; // the V3 spec's own spelling
        private const string EmbeddedUriFixed = "embedded://";
        private const string DefaultAssetUri = "ccdefault:";

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
                return LoadCharx(bytes);
            if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
                throw new CharacterCardException("WebP cards aren't supported. Export the card as PNG or JSON instead.");

            return new LoadedCard { Card = CharacterCard.FromJson(DecodeText(bytes)) };
        }

        private static LoadedCard LoadCharx(byte[] bytes)
        {
            try
            {
                var zip = new ZipReader(bytes);
                if (!zip.Contains("card.json"))
                    throw new CharacterCardException("This CHARX file doesn't contain a character card.");
                string json = DecodeText(zip.Read("card.json"));
                CharacterCard card = CharacterCard.FromJson(json);
                return new LoadedCard { Card = card, Image = ReadMainIcon(zip, card) };
            }
            catch (InvalidDataException ex)
            {
                throw new CharacterCardException("This CHARX file can't be read: " + ex.Message, ex);
            }
        }

        // The picture is a bonus: a card that can't find it still imports.
        private static byte[] ReadMainIcon(ZipReader zip, CharacterCard card)
        {
            var assets = card.ToJObject()["data"]["assets"] as JArray;
            if (assets == null)
                return null;
            var icons = assets.OfType<JObject>().Where(a => a["type"]?.ToString() == "icon").ToList();
            JObject main = icons.FirstOrDefault(a => a["name"]?.ToString() == "main") ?? icons.FirstOrDefault();
            string uri = main?["uri"]?.ToString() ?? "";
            string path = EmbeddedPath(uri);
            try
            {
                if (path != null)
                    return zip.Read(path);
                int comma = uri.IndexOf(',');
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0
                    && uri.Substring(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    return Convert.FromBase64String(uri.Substring(comma + 1));
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is InvalidDataException || ex is FormatException)
            {
                // No picture, then.
            }
            return null;
        }

        private static string EmbeddedPath(string uri)
        {
            foreach (string prefix in new[] { EmbeddedUri, EmbeddedUriFixed })
                if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return uri.Substring(prefix.Length);
            return null;
        }

        #region Export

        /// <summary>Writes a card to a .json file, or otherwise to a .png showing <paramref name="picture"/>.</summary>
        public static void Save(CharacterCard card, byte[] picture, string path)
        {
            byte[] bytes = string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
                ? new UTF8Encoding(false).GetBytes(ToJson(card))
                : ToPng(card, picture);
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>The card as a .json file holds it, in the spec it came in.</summary>
        public static string ToJson(CharacterCard card)
        {
            return Export(card, card.SourceSpec).ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>A PNG card: the picture, with the card in it as both V3 and V2. Any card already in it is replaced.</summary>
        public static byte[] ToPng(CharacterCard card, byte[] picture)
        {
            if (!IsPng(picture))
                throw new CharacterCardException("The character's picture isn't a PNG.");

            using (var output = new MemoryStream())
            {
                output.Write(PngSignature, 0, PngSignature.Length);
                int pos = PngSignature.Length;
                while (true)
                {
                    if (pos + 12 > picture.Length)
                        throw new CharacterCardException("The character's picture is damaged.");
                    long length = ((long)picture[pos] << 24) | ((long)picture[pos + 1] << 16) | ((long)picture[pos + 2] << 8) | picture[pos + 3];
                    if (pos + 12 + length > picture.Length)
                        throw new CharacterCardException("The character's picture is damaged.");
                    string type = Encoding.ASCII.GetString(picture, pos + 4, 4);

                    if (type == "IEND")
                    {
                        WriteTextChunk(output, "chara", Encode(Export(card, CardSpec.V2)));
                        WriteTextChunk(output, "ccv3", Encode(Export(card, CardSpec.V3)));
                    }
                    if (!IsCardChunk(picture, pos + 8, (int)length, type))
                        output.Write(picture, pos, 12 + (int)length);
                    if (type == "IEND")
                        return output.ToArray();
                    pos += 12 + (int)length;
                }
            }
        }

        // Embedded assets aren't kept when a CHARX is imported, so a saved card mustn't point at them.
        // The default icon means the PNG itself, or the app's own picture for a JSON card.
        private static JObject Export(CharacterCard card, CardSpec spec)
        {
            JObject json = card.ToJObject(spec);
            var assets = json["data"]["assets"] as JArray;
            if (assets == null)
                return json;
            foreach (JObject asset in assets.OfType<JObject>().ToList())
                if (EmbeddedPath(asset["uri"]?.ToString() ?? "") != null)
                    asset.Remove();
            if (!assets.OfType<JObject>().Any(a => a["type"]?.ToString() == "icon" && a["name"]?.ToString() == "main"))
                assets.Add(new JObject { ["type"] = "icon", ["uri"] = DefaultAssetUri, ["name"] = "main", ["ext"] = "png" });
            return json;
        }

        private static string Encode(JObject card)
        {
            return Convert.ToBase64String(new UTF8Encoding(false).GetBytes(card.ToString(Newtonsoft.Json.Formatting.None)));
        }

        private static bool IsCardChunk(byte[] png, int start, int length, string type)
        {
            if (type != "tEXt" && type != "zTXt" && type != "iTXt")
                return false;
            int zero = IndexOfZero(png, start, start + length);
            if (zero < 0)
                return false;
            string keyword = Latin1.GetString(png, start, zero - start);
            return keyword == "chara" || keyword == "ccv3";
        }

        private static void WriteTextChunk(Stream output, string keyword, string text)
        {
            byte[] data = Latin1.GetBytes(keyword + "\0" + text);
            byte[] typed = new byte[data.Length + 4];
            Encoding.ASCII.GetBytes("tEXt", 0, 4, typed, 0);
            Buffer.BlockCopy(data, 0, typed, 4, data.Length);

            WriteBigEndian(output, (uint)data.Length);
            output.Write(typed, 0, typed.Length);
            WriteBigEndian(output, Crc32(typed));
        }

        private static void WriteBigEndian(Stream output, uint value)
        {
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }

        /// <summary>The checksum PNG chunks end with.</summary>
        public static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }

        /// <summary>A character's name as a file name.</summary>
        public static string FileNameFor(CharacterCard card)
        {
            string name = string.Concat((card.Name ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
            return name.Length == 0 ? "Character" : name;
        }

        #endregion

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
