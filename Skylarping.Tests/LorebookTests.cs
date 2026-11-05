/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for lorebooks (character_book): reading/writing, activation and prompt placement.
/*==========================================================*/

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class LorebookTests
    {
        private const string Card = @"{
  ""spec"": ""chara_card_v2"", ""spec_version"": ""2.0"",
  ""data"": {
    ""name"": ""Seraphina"", ""description"": ""Forest guardian."",
    ""character_book"": {
      ""name"": ""Eldergrove"", ""scan_depth"": 2, ""token_budget"": 500, ""recursive_scanning"": false,
      ""extensions"": { ""keepme"": 1 },
      ""entries"": [
        { ""keys"": [""forest"", ""glade""], ""content"": ""The Eldergrove is enchanted."", ""enabled"": true,
          ""insertion_order"": 20, ""comment"": ""Forest"", ""extensions"": { ""probability"": 50 } },
        { ""keys"": [""wolf""], ""content"": ""Wolves guard the north."", ""enabled"": true, ""insertion_order"": 10,
          ""selective"": true, ""secondary_keys"": [""night""], ""position"": ""after_char"" },
        { ""keys"": [], ""content"": ""Magic is real."", ""enabled"": true, ""insertion_order"": 5, ""constant"": true },
        { ""keys"": [""secret""], ""content"": ""Off entry."", ""enabled"": false, ""insertion_order"": 1 },
        { ""keys"": [""Elara""], ""content"": ""Elara is the queen."", ""enabled"": true, ""insertion_order"": 30, ""case_sensitive"": true },
        { ""keys"": [""/dragons?/i""], ""content"": ""Dragons sleep."", ""enabled"": true, ""insertion_order"": 40 }
      ]
    }
  }
}";

        private static List<string> Titles(IEnumerable<LoreEntry> entries) => entries.Select(e => e.Content).ToList();

        [Test]
        public void ReadsBookAndEntries()
        {
            Lorebook book = CharacterCard.FromJson(Card).Lorebook;
            Assert.AreEqual("Eldergrove", book.Name);
            Assert.AreEqual(2, book.ScanDepth);
            Assert.AreEqual(500, book.TokenBudget);
            Assert.AreEqual(6, book.Entries.Count);
            LoreEntry forest = book.Entries[0];
            Assert.AreEqual("Forest", forest.Name);
            CollectionAssert.AreEqual(new[] { "forest", "glade" }, forest.Keys);
            Assert.IsTrue(book.Entries[1].Selective);
            Assert.IsTrue(book.Entries[1].AfterDescription);
            Assert.IsTrue(book.Entries[2].Constant);
            Assert.IsFalse(book.Entries[3].Enabled);
        }

        [Test]
        public void RoundTripKeepsUnknownFields()
        {
            CharacterCard card = CharacterCard.FromJson(Card);
            card.Lorebook.Entries[0].Content = "Edited.";
            card.Lorebook.Entries.Add(new LoreEntry { Keys = new List<string> { "river" }, Content = "A river." });
            JObject book = (JObject)card.ToJObject()["data"]["character_book"];
            Assert.AreEqual(1, (int)book["extensions"]["keepme"]);
            Assert.AreEqual(50, (int)book["entries"][0]["extensions"]["probability"]);
            Assert.AreEqual("Edited.", (string)book["entries"][0]["content"]);
            Assert.AreEqual("after_char", (string)book["entries"][1]["position"]);
            Assert.AreEqual(7, ((JArray)book["entries"]).Count);
            Assert.AreEqual("river", (string)book["entries"][6]["keys"][0]);

            CharacterCard again = CharacterCard.FromJson(card.ToJson());
            Assert.AreEqual(7, again.Lorebook.Entries.Count);
            Assert.IsTrue(again.UseLorebook);
        }

        [Test]
        public void ActivatesByKeywordsWithinScanDepth()
        {
            Lorebook book = CharacterCard.FromJson(Card).Lorebook;
            // Constant entry always; "forest" is 3 messages back, outside scan_depth 2.
            CollectionAssert.AreEqual(new[] { "Magic is real." },
                Titles(book.Activate(new[] { "the FOREST", "hi", "hello" }, 0)));
            CollectionAssert.AreEqual(new[] { "Magic is real.", "The Eldergrove is enchanted." },
                Titles(book.Activate(new[] { "hi", "Let's go to the Glade" }, 0)));
        }

        [Test]
        public void SelectiveCaseAndRegex()
        {
            Lorebook book = CharacterCard.FromJson(Card).Lorebook;
            List<string> got = Titles(book.Activate(new[] { "a wolf appears" }, 0));
            CollectionAssert.DoesNotContain(got, "Wolves guard the north.", "selective needs a secondary key");
            got = Titles(book.Activate(new[] { "a wolf at night" }, 0));
            CollectionAssert.Contains(got, "Wolves guard the north.");

            CollectionAssert.DoesNotContain(Titles(book.Activate(new[] { "elara" }, 0)), "Elara is the queen.");
            CollectionAssert.Contains(Titles(book.Activate(new[] { "Elara" }, 0)), "Elara is the queen.");
            CollectionAssert.Contains(Titles(book.Activate(new[] { "DRAGON!" }, 0)), "Dragons sleep.");
            CollectionAssert.DoesNotContain(Titles(book.Activate(new[] { "secret" }, 0)), "Off entry.");
        }

        [Test]
        public void BudgetDropsLowPriorityFirst()
        {
            var book = new Lorebook();
            book.Entries.Add(new LoreEntry { Keys = new List<string> { "a" }, Content = new string('x', 30), Priority = 1, InsertionOrder = 1 });
            book.Entries.Add(new LoreEntry { Keys = new List<string> { "a" }, Content = new string('y', 30), Priority = 5, InsertionOrder = 2 });
            List<LoreEntry> kept = book.Activate(new[] { "a" }, 10); // each costs 10 tokens
            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual(5, kept[0].Priority);
        }

        [Test]
        public void RecursiveScanning()
        {
            var book = new Lorebook { RecursiveScanning = true };
            book.Entries.Add(new LoreEntry { Keys = new List<string> { "castle" }, Content = "The castle belongs to the king.", InsertionOrder = 1 });
            book.Entries.Add(new LoreEntry { Keys = new List<string> { "king" }, Content = "The king is old.", InsertionOrder = 2 });
            Assert.AreEqual(2, book.Activate(new[] { "the castle" }, 0).Count);
            book.RecursiveScanning = false;
            Assert.AreEqual(1, book.Activate(new[] { "the castle" }, 0).Count);
        }

        [Test]
        public void PromptPlacesLoreAroundDescription()
        {
            CharacterCard card = CharacterCard.FromJson(Card);
            var history = new[] { new ChatTurn(false, "a wolf in the forest at night") };
            string system = PromptBuilder.Build(card, new UserProfile { Name = "Al" }, history, 0)[0].Content;
            int magic = system.IndexOf("Magic is real.");
            int forest = system.IndexOf("The Eldergrove is enchanted.");
            int description = system.IndexOf("Forest guardian.");
            int wolves = system.IndexOf("Wolves guard the north.");
            Assert.IsTrue(magic >= 0 && forest > magic && description > forest && wolves > description, system);

            card.UseLorebook = false;
            StringAssert.DoesNotContain("Magic is real.", PromptBuilder.Build(card, new UserProfile { Name = "Al" }, history, 0)[0].Content);
        }

        [Test]
        public void DecoratorLinesAreNotSent()
        {
            Assert.AreEqual("Real text.\n@@ not at start", PromptBuilder.StripDecorators("@@depth 4\n@@@role system\nReal text.\n@@ not at start"));
            Assert.AreEqual("Plain.", PromptBuilder.StripDecorators("Plain."));
        }

        [Test]
        public void NicknameIsUsedForChar()
        {
            CharacterCard card = CharacterCard.FromJson(
                @"{""spec"":""chara_card_v3"",""spec_version"":""3.0"",""data"":{""name"":""Seraphina Moonwhisper"",""nickname"":""Sera"",""description"":""{{char}} smiles.""}}");
            Assert.AreEqual("Sera", PromptBuilder.CharName(card));
            StringAssert.Contains("Sera smiles.", PromptBuilder.Build(card, new UserProfile { Name = "Al" }, new ChatTurn[0], 0)[0].Content);
        }

        [Test]
        public void BookNameIsSavedAndClearedWithTheCard()
        {
            var card = new CharacterCard { Name = "Sera", Lorebook = new Lorebook { Name = "Forest lore" } };
            CharacterCard saved = CharacterCard.FromJson(card.ToJson());
            Assert.AreEqual("Forest lore", saved.Lorebook.Name);

            saved.Lorebook.Name = "";
            Assert.AreEqual("", CharacterCard.FromJson(saved.ToJson()).Lorebook.Name);
        }
    }
}
