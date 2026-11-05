/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for SillyTavern character card reading/writing, PNG card chunks,
// avatar normalization and the per-profile character store.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class CharacterCardTests
    {
        private const string V2Json = @"{
  ""spec"": ""chara_card_v2"", ""spec_version"": ""2.0"",
  ""name"": ""Old Name"",
  ""data"": {
    ""name"": ""Seraphina"",
    ""description"": ""A guardian of the forest.\r\nShe has pink hair."",
    ""personality"": ""Kind, protective"",
    ""scenario"": ""{{user}} wakes up in a glade."",
    ""first_mes"": ""*She smiles.* Hello, {{user}}."",
    ""mes_example"": ""<START>\n{{user}}: Hi\n{{char}}: Hello!"",
    ""creator_notes"": ""notes"",
    ""system_prompt"": """",
    ""post_history_instructions"": """",
    ""alternate_greetings"": [""Hey"", ""Yo""],
    ""tags"": [""fantasy"", ""female""],
    ""creator"": ""someone"",
    ""character_version"": ""1.2"",
    ""character_book"": { ""entries"": [ { ""keys"": [""forest""], ""content"": ""Big trees."" } ] },
    ""extensions"": { ""talkativeness"": ""0.5"", ""depth_prompt"": { ""prompt"": ""x"", ""depth"": 4 } }
  }
}";

        [Test]
        public void ReadsV2Card()
        {
            CharacterCard card = CharacterCard.FromJson(V2Json);
            Assert.AreEqual(CardSpec.V2, card.SourceSpec);
            Assert.AreEqual("Seraphina", card.Name); // data wins over the top-level mirror
            Assert.AreEqual("A guardian of the forest.\nShe has pink hair.", card.Description);
            Assert.AreEqual("Kind, protective", card.Personality);
            Assert.AreEqual("<START>\n{{user}}: Hi\n{{char}}: Hello!", card.MessageExamples);
            Assert.AreEqual("*She smiles.* Hello, {{user}}.", card.FirstMessage);
            CollectionAssert.AreEqual(new[] { "fantasy", "female" }, card.Tags);
            CollectionAssert.AreEqual(new[] { "Hey", "Yo" }, card.AlternateGreetings);
            Assert.IsTrue(card.HasLorebook);
        }

        [Test]
        public void ReadsV1Card()
        {
            CharacterCard card = CharacterCard.FromJson(
                @"{""name"":""Bob"",""description"":""desc"",""personality"":""grumpy"",""scenario"":"""",""first_mes"":""Hi"",""mes_example"":""<START>""}");
            Assert.AreEqual(CardSpec.V1, card.SourceSpec);
            Assert.AreEqual("Bob", card.Name);
            Assert.AreEqual("grumpy", card.Personality);
        }

        [Test]
        public void ReadsPygmalionEraNames()
        {
            CharacterCard card = CharacterCard.FromJson(
                @"{""char_name"":""Ann"",""char_persona"":""persona"",""char_greeting"":""Hey"",""example_dialogue"":""ex"",""world_scenario"":""sc""}");
            Assert.AreEqual("Ann", card.Name);
            Assert.AreEqual("persona", card.Description);
            Assert.AreEqual("Hey", card.FirstMessage);
            Assert.AreEqual("ex", card.MessageExamples);
            Assert.AreEqual("sc", card.Scenario);
        }

        [Test]
        public void RejectsNonCards()
        {
            Assert.Throws<CharacterCardException>(() => CharacterCard.FromJson("not json"));
            Assert.Throws<CharacterCardException>(() => CharacterCard.FromJson("[1,2]"));
            Assert.Throws<CharacterCardException>(() => CharacterCard.FromJson(@"{""foo"":1}"));
        }

        [Test]
        public void RoundTripKeepsUnknownFieldsAndEdits()
        {
            CharacterCard card = CharacterCard.FromJson(V2Json);
            card.Personality = "Edited\r\nline";
            JObject json = card.ToJObject();

            Assert.AreEqual("chara_card_v2", (string)json["spec"]);
            Assert.AreEqual("Seraphina", (string)json["name"]);
            JObject data = (JObject)json["data"];
            Assert.AreEqual("Edited\nline", (string)data["personality"]);
            Assert.AreEqual("Big trees.", (string)data["character_book"]["entries"][0]["content"]);
            Assert.AreEqual(4, (int)data["extensions"]["depth_prompt"]["depth"]);

            CharacterCard again = CharacterCard.FromJson(card.ToJson());
            Assert.AreEqual("Edited\nline", again.Personality);
            CollectionAssert.AreEqual(card.AlternateGreetings, again.AlternateGreetings);
        }

        [Test]
        public void V3StaysV3()
        {
            CharacterCard card = CharacterCard.FromJson(
                @"{""spec"":""chara_card_v3"",""spec_version"":""3.0"",""data"":{""name"":""Vee"",""description"":""d"",""nickname"":""V"",""assets"":[{""type"":""icon"",""uri"":""ccdefault:"",""name"":""main"",""ext"":""png""}]}}");
            Assert.AreEqual(CardSpec.V3, card.SourceSpec);
            JObject json = card.ToJObject();
            Assert.AreEqual("chara_card_v3", (string)json["spec"]);
            Assert.AreEqual("3.0", (string)json["spec_version"]);
            Assert.AreEqual("V", (string)json["data"]["nickname"]);
            Assert.AreEqual("ccdefault:", (string)json["data"]["assets"][0]["uri"]);
            Assert.IsInstanceOf<JArray>(json["data"]["group_only_greetings"]);
        }

        [Test]
        public void GreetingIsOffByDefaultAndRoundTrips()
        {
            CharacterCard card = CharacterCard.FromJson(V2Json);
            Assert.IsFalse(card.UseGreeting, "imported cards don't greet unless turned on");
            Assert.IsFalse(card.UseScenario, "imported cards don't use their scenario unless turned on");

            card.UseGreeting = true;
            JObject json = card.ToJObject();
            Assert.AreEqual(true, (bool)json["data"]["extensions"]["skylarping"]["use_greeting"]);
            Assert.AreEqual(false, (bool)json["data"]["extensions"]["skylarping"]["use_scenario"]);
            Assert.AreEqual("{{user}} wakes up in a glade.", (string)json["data"]["scenario"], "scenario text kept while off");
            Assert.AreEqual("0.5", (string)json["data"]["extensions"]["talkativeness"], "other extensions kept");

            CharacterCard again = CharacterCard.FromJson(card.ToJson());
            Assert.IsTrue(again.UseGreeting);
            Assert.IsFalse(again.UseScenario);
            again.UseScenario = true;
            Assert.IsTrue(CharacterCard.FromJson(again.ToJson()).UseScenario);
            Assert.AreEqual("*She smiles.* Hello, {{user}}.", again.FirstMessage);
        }

        [Test]
        public void ProactiveMessagesAreOffByDefaultAndRoundTrip()
        {
            CharacterCard card = CharacterCard.FromJson(V2Json);
            Assert.IsFalse(card.ProactiveMessages, "imported cards don't message first unless turned on");
            Assert.AreEqual(CharacterCard.DefaultProactiveMinMinutes, card.ProactiveMinMinutes);
            Assert.AreEqual(CharacterCard.DefaultProactiveMaxMinutes, card.ProactiveMaxMinutes);
            Assert.AreEqual(3, card.ProactiveMaxUnanswered);

            card.ProactiveMessages = true;
            card.ProactiveMinMinutes = 5;
            card.ProactiveMaxMinutes = 15;
            card.ProactiveMaxUnanswered = 6;
            JObject json = card.ToJObject();
            Assert.AreEqual(true, (bool)json["data"]["extensions"]["skylarping"]["proactive_messages"]);
            Assert.AreEqual(5, (int)json["data"]["extensions"]["skylarping"]["proactive_min_minutes"]);

            CharacterCard again = CharacterCard.FromJson(card.ToJson());
            Assert.IsTrue(again.ProactiveMessages);
            Assert.AreEqual(5, again.ProactiveMinMinutes);
            Assert.AreEqual(15, again.ProactiveMaxMinutes);
            Assert.AreEqual(6, again.ProactiveMaxUnanswered);

            json["data"]["extensions"]["skylarping"]["proactive_min_minutes"] = 0; // invalid: back to the default
            Assert.AreEqual(CharacterCard.DefaultProactiveMinMinutes, CharacterCard.FromJObject(json).ProactiveMinMinutes);
            json["data"]["extensions"]["skylarping"]["proactive_max_unanswered"] = 0;
            Assert.AreEqual(CharacterCard.DefaultProactiveMaxUnanswered, CharacterCard.FromJObject(json).ProactiveMaxUnanswered);
        }

        [Test]
        public void SuppressActionsIsOffByDefaultAndRoundTrips()
        {
            CharacterCard card = CharacterCard.FromJson(V2Json);
            Assert.IsFalse(card.SuppressActions);

            card.SuppressActions = true;
            Assert.AreEqual(true, (bool)card.ToJObject()["data"]["extensions"]["skylarping"]["suppress_actions"]);
            Assert.IsTrue(CharacterCard.FromJson(card.ToJson()).SuppressActions);
        }

        [Test]
        public void NewCardIsWrittenAsV2()
        {
            var card = new CharacterCard { Name = "Fresh", Description = "d" };
            JObject json = card.ToJObject();
            Assert.AreEqual("chara_card_v2", (string)json["spec"]);
            Assert.AreEqual("Fresh", (string)json["data"]["name"]);
            Assert.IsInstanceOf<JObject>(json["data"]["extensions"]);
        }

        #region PNG

        [Test]
        public void ReadsPngCardFromCharaChunk()
        {
            byte[] png = AddTextChunk(MakePng(40, 60), "chara", Base64(V2Json));
            LoadedCard loaded = CharacterCardFile.Load(png, ".png");
            Assert.AreEqual("Seraphina", loaded.Card.Name);
            Assert.AreSame(png, loaded.Image);
        }

        [Test]
        public void PrefersCcv3OverChara()
        {
            byte[] png = MakePng(8, 8);
            png = AddTextChunk(png, "chara", Base64(@"{""name"":""Two"",""description"":""x""}"));
            png = AddTextChunk(png, "ccv3", Base64(@"{""spec"":""chara_card_v3"",""spec_version"":""3.0"",""data"":{""name"":""Three"",""description"":""x""}}"));
            Assert.AreEqual("Three", CharacterCardFile.Load(png, ".png").Card.Name);
        }

        [Test]
        public void ReadsCompressedITXtChunk()
        {
            byte[] text = Encoding.UTF8.GetBytes(Base64(@"{""name"":""Zed"",""description"":""ü""}"));
            var data = new List<byte>(Encoding.ASCII.GetBytes("chara"));
            data.Add(0); data.Add(1); data.Add(0); // keyword end, compressed, method 0
            data.Add(0); data.Add(0);             // empty language, empty translated keyword
            data.AddRange(Zlib(text));
            byte[] png = AddChunk(MakePng(8, 8), "iTXt", data.ToArray());
            CharacterCard card = CharacterCardFile.Load(png, ".png").Card;
            Assert.AreEqual("Zed", card.Name);
            Assert.AreEqual("ü", card.Description);
        }

        [Test]
        public void PlainPictureIsNotACard()
        {
            var ex = Assert.Throws<CharacterCardException>(() => CharacterCardFile.Load(MakePng(8, 8), ".png"));
            StringAssert.Contains("Change Picture", ex.Message);
        }

        [Test]
        public void JsonWithBomLoads()
        {
            byte[] bytes = new UTF8Encoding(true).GetPreamble();
            byte[] json = Encoding.UTF8.GetBytes(V2Json);
            byte[] all = new byte[bytes.Length + json.Length];
            bytes.CopyTo(all, 0);
            json.CopyTo(all, bytes.Length);
            Assert.AreEqual("Seraphina", CharacterCardFile.Load(all, ".json").Card.Name);
        }

        [Test]
        public void AvatarIsSquareAndSmall()
        {
            byte[] avatar = CharacterAvatar.Normalize(AddTextChunk(MakePng(400, 600), "chara", Base64(V2Json)));
            BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(avatar), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.AreEqual(CharacterAvatar.Size, frame.PixelWidth);
            Assert.AreEqual(CharacterAvatar.Size, frame.PixelHeight);
            Assert.IsFalse(CharacterCardFile.ReadPngText(avatar).ContainsKey("chara"), "card data should be stripped");

            byte[] tiny = CharacterAvatar.Normalize(MakePng(30, 20));
            frame = BitmapDecoder.Create(new MemoryStream(tiny), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.AreEqual(20, frame.PixelWidth);
            Assert.AreEqual(20, frame.PixelHeight);
        }

        [Test]
        public void AvatarRejectsGarbage()
        {
            Assert.Throws<CharacterCardException>(() => CharacterAvatar.Normalize(new byte[] { 1, 2, 3 }));
        }

        #endregion

        [Test]
        public void StoreAddsAndLoads()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SkylarpingTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CharacterStore(folder);
                CharacterCard card = CharacterCard.FromJson(V2Json);
                byte[] avatar = CharacterAvatar.Normalize(MakePng(10, 10));
                StoredCharacter added = store.Add(card, avatar);
                store.Add(new CharacterCard { Name = "  !!  " }, null);

                StringAssert.StartsWith("char_seraphina_", added.Id);
                Assert.IsTrue(CharacterStore.IsCharacterId(added.Id));

                List<StoredCharacter> all = new CharacterStore(folder).LoadAll();
                Assert.AreEqual(2, all.Count);
                StoredCharacter loaded = all.Find(c => c.Id == added.Id);
                Assert.AreEqual("Seraphina", loaded.Card.Name);
                Assert.IsTrue(loaded.Card.HasLorebook);
                CollectionAssert.AreEqual(avatar, loaded.Avatar);
                StoredCharacter blank = all.Find(c => c.Id != added.Id);
                StringAssert.StartsWith("char_character_", blank.Id);
                Assert.IsNull(blank.Avatar);

                // Delete removes only that character, and refuses non-character ids.
                store.Delete(blank.Id);
                Assert.AreEqual(1, new CharacterStore(folder).LoadAll().Count);
                Assert.Throws<ArgumentException>(() => store.Delete("..\\x"));
                Assert.Throws<ArgumentException>(() => store.Delete("char_..\\.."));

                // A broken folder is skipped, not fatal.
                Directory.CreateDirectory(Path.Combine(folder, "char_broken"));
                File.WriteAllText(Path.Combine(folder, "char_broken", CharacterStore.CardFile), "{oops");
                Assert.AreEqual(1, new CharacterStore(folder).LoadAll().Count);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Test]
        public void StoreKeepsMoodSeparateFromCard()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SkylarpingTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CharacterStore(folder);
                StoredCharacter added = store.Add(new CharacterCard { Name = "Mia" }, null);
                Assert.IsNull(store.Load(added.Id).Mood);

                var when = new DateTime(2026, 11, 4, 18, 30, 0, DateTimeKind.Utc);
                CharacterStore.WriteMood(added.Folder, "Baking at 2am", when, when.AddHours(5));
                StoredCharacter loaded = store.Load(added.Id);
                Assert.AreEqual("Baking at 2am", loaded.Mood);
                Assert.AreEqual(when, loaded.MoodUpdated);
                Assert.AreEqual(DateTimeKind.Utc, loaded.MoodUpdated.Kind);
                Assert.AreEqual(when.AddHours(5), loaded.MoodNext);

                // Editing the character keeps the mood; it isn't part of the card.
                store.Update(added.Id, new CharacterCard { Name = "Mia 2" }, null);
                Assert.AreEqual("Baking at 2am", store.Load(added.Id).Mood);
                StringAssert.DoesNotContain("Baking", File.ReadAllText(Path.Combine(added.Folder, CharacterStore.CardFile)));

                // A damaged status file means no mood, not a lost character.
                File.WriteAllText(Path.Combine(added.Folder, CharacterStore.StatusFile), "{oops");
                Assert.IsNull(store.Load(added.Id).Mood);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Test]
        public void MoodScheduleIsPerCharacter()
        {
            CharacterCard imported = CharacterCard.FromJson(V2Json);
            Assert.IsTrue(imported.UpdateMood);
            Assert.AreEqual(CharacterCard.DefaultMoodMinHours, imported.MoodMinHours);
            Assert.AreEqual(CharacterCard.DefaultMoodMaxHours, imported.MoodMaxHours);

            CharacterCard saved = CharacterCard.FromJson(
                new CharacterCard { Name = "A", UpdateMood = false, MoodMinHours = 2, MoodMaxHours = 12 }.ToJson());
            Assert.IsFalse(saved.UpdateMood);
            Assert.AreEqual(2, saved.MoodMinHours);
            Assert.AreEqual(12, saved.MoodMaxHours);

            // Early builds' single mood_hours (0 = never) still reads.
            JObject legacy = JObject.Parse(new CharacterCard { Name = "A" }.ToJson());
            JObject ext = (JObject)legacy.SelectToken("data.extensions.skylarping");
            ext.Remove("update_mood"); ext.Remove("mood_min_hours"); ext.Remove("mood_max_hours");
            ext["mood_hours"] = 3;
            CharacterCard old = CharacterCard.FromJson(legacy.ToString());
            Assert.IsTrue(old.UpdateMood);
            Assert.AreEqual(3, old.MoodMinHours);
            Assert.AreEqual(3, old.MoodMaxHours);
            Assert.IsNull(JObject.Parse(old.ToJson()).SelectToken("data.extensions.skylarping.mood_hours"));
            ext["mood_hours"] = 0;
            Assert.IsFalse(CharacterCard.FromJson(legacy.ToString()).UpdateMood);
        }

        [Test]
        public void CleanMoodTakesOneTidyLine()
        {
            Assert.AreEqual("Baking at 2am", PromptBuilder.CleanMood("\n  \"Baking at 2am\"  \nsecond line", false));
            Assert.AreEqual("off to the lake", PromptBuilder.CleanMood("Mood: “off to the lake”", false));
            Assert.AreEqual("sleepy", PromptBuilder.CleanMood("*yawns* sleepy", true));
            Assert.IsNull(PromptBuilder.CleanMood("*yawns*", true));
            Assert.IsNull(PromptBuilder.CleanMood("   ", false));
            Assert.IsNull(PromptBuilder.CleanMood(null, false));
            string longMood = PromptBuilder.CleanMood(string.Join(" ", new string[40].Select(_ => "word")), false);
            Assert.LessOrEqual(longMood.Length, PromptBuilder.MaxMoodLength);
            StringAssert.EndsWith("...", longMood);
        }

        #region Helpers

        private static string Base64(string json) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)); }

        private static byte[] MakePng(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)(i * 7);
            BitmapSource bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        private static byte[] AddTextChunk(byte[] png, string keyword, string text)
        {
            var data = new List<byte>(Encoding.ASCII.GetBytes(keyword)) { 0 };
            data.AddRange(Encoding.ASCII.GetBytes(text));
            return AddChunk(png, "tEXt", data.ToArray());
        }

        /// <summary>Inserts a chunk just before IEND (the last 12 bytes).</summary>
        private static byte[] AddChunk(byte[] png, string type, byte[] data)
        {
            var chunk = new List<byte>();
            chunk.Add((byte)(data.Length >> 24)); chunk.Add((byte)(data.Length >> 16));
            chunk.Add((byte)(data.Length >> 8)); chunk.Add((byte)data.Length);
            var typeAndData = new List<byte>(Encoding.ASCII.GetBytes(type));
            typeAndData.AddRange(data);
            chunk.AddRange(typeAndData);
            uint crc = Crc32(typeAndData.ToArray());
            chunk.Add((byte)(crc >> 24)); chunk.Add((byte)(crc >> 16)); chunk.Add((byte)(crc >> 8)); chunk.Add((byte)crc);

            var result = new List<byte>(png.Length + chunk.Count);
            for (int i = 0; i < png.Length - 12; i++) result.Add(png[i]);
            result.AddRange(chunk);
            for (int i = png.Length - 12; i < png.Length; i++) result.Add(png[i]);
            return result.ToArray();
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }

        private static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionMode.Compress, true))
                    deflate.Write(data, 0, data.Length);
                // adler32 not needed by our reader
                return ms.ToArray();
            }
        }

        #endregion
    }
}
