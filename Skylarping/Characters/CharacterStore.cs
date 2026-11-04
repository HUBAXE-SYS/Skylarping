/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Characters are saved per profile, one folder each:
//
//   %AppData%\Skylarping\<profile>\Characters\<id>\
//       card.json    the character card (V2, or V3 if imported as V3); the source of truth
//       avatar.png   contact picture (optional), already square and small
//       memories\   Memory Tools' notes, when the character may use them (see AI/Tools)
//       voice.wav    RESERVED, not written yet: a reference clip for voice cloning, so
//                    calls can speak in the character's voice. When this lands, the editor
//                    gets a "Voice sample" section (import a clip / record one / play) and
//                    the TTS settings in Tools > AI Services get a voice-cloning option.
//                    Keep the clip out of card.json: cards get shared, clips can be large.
//
// The folder name is the character's identifier (contact username), so it never changes
// once a character is added.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Skylarping.Characters
{
    public sealed class StoredCharacter
    {
        public string Id { get; internal set; }
        public CharacterCard Card { get; internal set; }
        /// <summary>Square PNG, or null for the default picture.</summary>
        public byte[] Avatar { get; internal set; }
        /// <summary>The character's folder. Tools keep per-character data under it (memories\).</summary>
        public string Folder { get; internal set; }
    }

    public sealed class CharacterStore
    {
        public const string CardFile = "card.json";
        public const string AvatarFile = "avatar.png";
        public const string VoiceSampleFile = "voice.wav"; // reserved for voice cloning, see above
        public const string IdPrefix = "char_";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public string Folder { get; private set; }

        public CharacterStore(string folder)
        {
            Folder = folder;
        }

        public static bool IsCharacterId(string id)
        {
            return id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        public List<StoredCharacter> LoadAll()
        {
            var list = new List<StoredCharacter>();
            if (!Directory.Exists(Folder))
                return list;
            foreach (string dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    StoredCharacter c = Load(Path.GetFileName(dir));
                    if (c != null)
                        list.Add(c);
                }
                catch (Exception ex)
                {
                    // One damaged character shouldn't hide the rest.
                    Debug.WriteLine("[CharacterStore] Skipping " + dir + ": " + ex.Message);
                }
            }
            return list;
        }

        public StoredCharacter Load(string id)
        {
            string dir = Path.Combine(Folder, id);
            string cardPath = Path.Combine(dir, CardFile);
            if (!File.Exists(cardPath))
                return null;
            string avatarPath = Path.Combine(dir, AvatarFile);
            return new StoredCharacter
            {
                Id = id,
                Folder = dir,
                Card = CharacterCard.FromJson(File.ReadAllText(cardPath, Encoding.UTF8)),
                Avatar = File.Exists(avatarPath) ? File.ReadAllBytes(avatarPath) : null
            };
        }

        /// <summary>Saves a new character and returns it with its new identifier.</summary>
        public StoredCharacter Add(CharacterCard card, byte[] avatar)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));
            string id = NewId(card.Name);
            string dir = Path.Combine(Folder, id);
            Directory.CreateDirectory(dir);
            Save(dir, card, avatar);
            return new StoredCharacter { Id = id, Folder = dir, Card = card, Avatar = avatar };
        }

        /// <summary>Saves changes to an existing character (Edit Character).</summary>
        public StoredCharacter Update(string id, CharacterCard card, byte[] avatar)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));
            string dir = Path.Combine(Folder, id);
            if (!IsCharacterId(id) || !File.Exists(Path.Combine(dir, CardFile)))
                throw new InvalidOperationException("That character doesn't exist any more.");
            Save(dir, card, avatar);
            return new StoredCharacter { Id = id, Folder = dir, Card = card, Avatar = avatar };
        }

        /// <summary>Deletes a character's folder (card, picture and anything else in it).</summary>
        public void Delete(string id)
        {
            if (!IsCharacterId(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
                throw new ArgumentException("Not a character id: " + id);
            string dir = Path.Combine(Folder, id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private static void Save(string dir, CharacterCard card, byte[] avatar)
        {
            WriteAtomic(Path.Combine(dir, CardFile), Utf8NoBom.GetBytes(card.ToJson()));
            string avatarPath = Path.Combine(dir, AvatarFile);
            if (avatar != null && avatar.Length > 0)
                WriteAtomic(avatarPath, avatar);
            else if (File.Exists(avatarPath))
                File.Delete(avatarPath);
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>char_ + a readable slug of the name + a short random suffix, e.g. char_seraphina_3f9a1c.</summary>
        private string NewId(string name)
        {
            var slug = new StringBuilder();
            foreach (char ch in (name ?? "").ToLowerInvariant())
            {
                if (slug.Length >= 24)
                    break;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                    slug.Append(ch);
                else if (slug.Length > 0 && slug[slug.Length - 1] != '_')
                    slug.Append('_');
            }
            string baseId = IdPrefix + (slug.Length > 0 ? slug.ToString().TrimEnd('_') : "character");
            while (true)
            {
                string id = baseId + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (!Directory.Exists(Path.Combine(Folder, id)))
                    return id;
            }
        }
    }
}
