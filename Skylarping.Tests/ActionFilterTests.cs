/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for No action text: cutting *actions* out of character replies and the prompt.
/*==========================================================*/

using System.Collections.Generic;
using NUnit.Framework;
using Skylarping.AI;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class ActionFilterTests
    {
        [TestCase("*waves* Hi there!", "Hi there!")]
        [TestCase("Hi *waves*!", "Hi!")]
        [TestCase("Okay *nods*, sure.", "Okay, sure.")]
        [TestCase("*nods*, sure.", "sure.")]
        [TestCase("Hey. *smiles warmly* How are you?", "Hey. How are you?")]
        [TestCase("_shrugs_ dunno", "dunno")]
        [TestCase("Hi\n*waves*\nHow are you?", "Hi\nHow are you?")]
        [TestCase("*She looks up from her book,\nsmiling.* Oh, hi!", "Oh, hi!")]
        [TestCase("Para one.\n\n*leans back*\n\nPara two.", "Para one.\n\nPara two.")]
        public void RemovesActions(string input, string expected)
        {
            Assert.AreEqual(expected, ActionFilter.Strip(input));
        }

        [TestCase("This is **important**.")]
        [TestCase("2 * 3 * 4 = 24")]
        [TestCase("use my_var_name here")]
        [TestCase("Run `ls *foo*` now")]
        [TestCase("```\n  x = *p*;\n```")]
        [TestCase("No markup at all.")]
        public void LeavesOtherTextAlone(string input)
        {
            Assert.AreEqual(input, ActionFilter.Strip(input));
        }

        [Test]
        public void OnlyActionsBecomesEmpty()
        {
            Assert.AreEqual("", ActionFilter.Strip("*smiles* *waves*"));
        }

        [Test]
        public void PartialHidesUnclosedAction()
        {
            Assert.AreEqual("Hello!", ActionFilter.Strip("Hello! *leans clo", true));
            Assert.AreEqual("Hello! *leans clo", ActionFilter.Strip("Hello! *leans clo"));
            Assert.AreEqual("Hello! **bold", ActionFilter.Strip("Hello! **bold", true));
        }

        private static CharacterCard Card()
        {
            return new CharacterCard
            {
                Name = "Sera",
                MessageExamples = "<START>\n{{user}}: Hi\n{{char}}: *waves* Hello!",
                PostHistoryInstructions = "Stay gentle.",
            };
        }

        [Test]
        public void PromptAsksForNoActionsAndFiltersCharacterTurns()
        {
            var history = new List<ChatTurn>
            {
                new ChatTurn(false, "*hugs you* hey"),
                new ChatTurn(true, "*hugs back* Hey you!"),
                new ChatTurn(true, "*smiles*"),
                new ChatTurn(false, "how was your day?"),
            };
            List<LlmChatMessage> messages = PromptBuilder.Build(Card(), new UserProfile { Name = "Al" }, history, 0,
                suppressActions: true);

            string system = messages[0].Content;
            StringAssert.Contains("Do not narrate actions", system);
            StringAssert.DoesNotContain("*waves* Hello", system);
            StringAssert.Contains("Sera: Hello!", system);
            Assert.Less(system.IndexOf("Do not narrate actions"), system.IndexOf("Stay gentle."));

            Assert.AreEqual(4, messages.Count);              // the action-only turn is dropped
            Assert.AreEqual("*hugs you* hey", messages[1].Content);  // the user's text is kept
            Assert.AreEqual("Hey you!", messages[2].Content);
        }

        [Test]
        public void PromptUnchangedWhenOff()
        {
            var history = new List<ChatTurn> { new ChatTurn(true, "*smiles* Hi") };
            List<LlmChatMessage> messages = PromptBuilder.Build(Card(), new UserProfile { Name = "Al" }, history, 0);
            StringAssert.DoesNotContain("Do not narrate actions", messages[0].Content);
            StringAssert.Contains("*waves*", messages[0].Content);
            Assert.AreEqual("*smiles* Hi", messages[1].Content);
        }
    }
}
