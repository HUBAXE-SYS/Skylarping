/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Group chats on disk: <profile>\Groups\<id>\group.json holds the name, the members and the
// settings, with the chat's macro variables beside it. The messages are in the database, like
// every chat's.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Skylarping.Characters
{
    public sealed class StoredGroup
    {
        public const int DefaultQuietMinMinutes = 10;
        public const int DefaultQuietMaxMinutes = 30;
        public const int DefaultRepeats = 3;
        public const int DefaultDelayMinSeconds = 15;
        public const int DefaultDelayMaxSeconds = 60;
        public const int DefaultCallPauseMinSeconds = 1;
        public const int DefaultCallPauseMaxSeconds = 3;
        public const int DefaultCallQuietMinSeconds = 20;
        public const int DefaultCallQuietMaxSeconds = 45;

        public string Id { get; internal set; }
        public string Folder { get; internal set; }
        public string Name { get; set; } = "";
        public List<string> MemberIds { get; set; } = new List<string>();

        /// <summary>Whether the characters talk on their own when the chat goes quiet.</summary>
        public bool Unprompted { get; set; } = true;
        public int QuietMinMinutes { get; set; } = DefaultQuietMinMinutes;
        public int QuietMaxMinutes { get; set; } = DefaultQuietMaxMinutes;
        /// <summary>How many quiet spells they talk through before waiting for the user. 0 = no limit.</summary>
        public int Repeats { get; set; } = DefaultRepeats;
        /// <summary>The wait before a reply nobody asked for: one in the unprompted loop, or to a character.</summary>
        public int DelayMinSeconds { get; set; } = DefaultDelayMinSeconds;
        public int DelayMaxSeconds { get; set; } = DefaultDelayMaxSeconds;
        /// <summary>How many replies characters can set off by naming each other in one cycle. 0 = no limit.</summary>
        public int ChainLimit { get; set; }

        /// <summary>On a call: the silence between speakers.</summary>
        public int CallPauseMinSeconds { get; set; } = DefaultCallPauseMinSeconds;
        public int CallPauseMaxSeconds { get; set; } = DefaultCallPauseMaxSeconds;
        /// <summary>On a call: the quiet before they talk on their own.</summary>
        public int CallQuietMinSeconds { get; set; } = DefaultCallQuietMinSeconds;
        public int CallQuietMaxSeconds { get; set; } = DefaultCallQuietMaxSeconds;

        public StoredGroup Copy()
        {
            var copy = (StoredGroup)MemberwiseClone();
            copy.MemberIds = MemberIds.ToList();
            return copy;
        }

        internal JObject ToJObject()
        {
            return new JObject
            {
                ["name"] = Name,
                ["members"] = new JArray(MemberIds.Cast<object>().ToArray()),
                ["unprompted"] = Unprompted,
                ["quiet_min_minutes"] = QuietMinMinutes,
                ["quiet_max_minutes"] = QuietMaxMinutes,
                ["repeats"] = Repeats,
                ["delay_min_seconds"] = DelayMinSeconds,
                ["delay_max_seconds"] = DelayMaxSeconds,
                ["chain_limit"] = ChainLimit,
                ["call_pause_min_seconds"] = CallPauseMinSeconds,
                ["call_pause_max_seconds"] = CallPauseMaxSeconds,
                ["call_quiet_min_seconds"] = CallQuietMinSeconds,
                ["call_quiet_max_seconds"] = CallQuietMaxSeconds,
            };
        }

        internal static StoredGroup FromJObject(JObject o, string id, string folder)
        {
            var defaults = new StoredGroup();
            return new StoredGroup
            {
                Id = id,
                Folder = folder,
                Name = (string)o["name"] ?? "",
                MemberIds = (o["members"] as JArray ?? new JArray()).Select(m => (string)m).Where(m => m != null).ToList(),
                Unprompted = (bool?)o["unprompted"] ?? defaults.Unprompted,
                QuietMinMinutes = (int?)o["quiet_min_minutes"] ?? defaults.QuietMinMinutes,
                QuietMaxMinutes = (int?)o["quiet_max_minutes"] ?? defaults.QuietMaxMinutes,
                Repeats = (int?)o["repeats"] ?? defaults.Repeats,
                DelayMinSeconds = (int?)o["delay_min_seconds"] ?? defaults.DelayMinSeconds,
                DelayMaxSeconds = (int?)o["delay_max_seconds"] ?? defaults.DelayMaxSeconds,
                ChainLimit = (int?)o["chain_limit"] ?? defaults.ChainLimit,
                CallPauseMinSeconds = (int?)o["call_pause_min_seconds"] ?? defaults.CallPauseMinSeconds,
                CallPauseMaxSeconds = (int?)o["call_pause_max_seconds"] ?? defaults.CallPauseMaxSeconds,
                CallQuietMinSeconds = (int?)o["call_quiet_min_seconds"] ?? defaults.CallQuietMinSeconds,
                CallQuietMaxSeconds = (int?)o["call_quiet_max_seconds"] ?? defaults.CallQuietMaxSeconds,
            };
        }
    }

    public sealed class GroupStore
    {
        public const string IdPrefix = "group_";
        private const string GroupFile = "group.json";

        public string Folder { get; private set; }

        public GroupStore(string folder)
        {
            Folder = folder;
        }

        public static bool IsGroupId(string id)
        {
            return id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        public List<StoredGroup> LoadAll()
        {
            var groups = new List<StoredGroup>();
            if (!Directory.Exists(Folder))
                return groups;
            foreach (string dir in Directory.GetDirectories(Folder))
            {
                try
                {
                    string id = Path.GetFileName(dir);
                    string path = Path.Combine(dir, GroupFile);
                    if (IsGroupId(id) && File.Exists(path))
                        groups.Add(StoredGroup.FromJObject(JObject.Parse(File.ReadAllText(path, Encoding.UTF8)), id, dir));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                    || ex is JsonException || ex is FormatException || ex is ArgumentException)
                {
                    // A damaged group shouldn't take the others down with it.
                }
            }
            return groups;
        }

        /// <summary>Saves a new group under a new id.</summary>
        public StoredGroup Add(StoredGroup group)
        {
            group.Id = IdPrefix + Guid.NewGuid().ToString("N");
            group.Folder = Path.Combine(Folder, group.Id);
            Directory.CreateDirectory(group.Folder);
            Save(group);
            return group;
        }

        public static void Save(StoredGroup group)
        {
            if (group.Folder == null)
                return;
            Directory.CreateDirectory(group.Folder);
            CharacterStore.WriteAtomic(Path.Combine(group.Folder, GroupFile),
                new UTF8Encoding(false).GetBytes(group.ToJObject().ToString()));
        }

        public void Delete(string id)
        {
            if (!IsGroupId(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
                throw new ArgumentException("Not a group id: " + id);
            string dir = Path.Combine(Folder, id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
