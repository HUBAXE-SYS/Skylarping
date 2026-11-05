using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class MacroTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 15, 7, 9);

        private static MacroContext Context()
        {
            return new MacroContext
            {
                CharName = "Sera",
                UserName = "Al",
                Persona = "Likes {{char}}.",
                Now = Now,
                Card = new CharacterCard
                {
                    Description = "{{char}} guards the forest.",
                    Personality = "Kind",
                    FirstMessage = "Hi",
                    AlternateGreetings = new List<string> { "Hello", "Hey" },
                    MessageExamples = "<START>\n{{user}}: Hi",
                },
                History = new[]
                {
                    new ChatTurn(false, "first"),
                    new ChatTurn(true, "Hello {{user}}"),
                    new ChatTurn(false, "last"),
                },
                ContextWindow = 1000,
                Model = "test-model",
            };
        }

        private static string Run(string text, MacroContext context = null)
        {
            return Macros.Expand(text, context ?? Context());
        }

        [TestCase("{{user}} {{char}}", "Al Sera")]
        [TestCase("{{USER}} {{ Char }}", "Al Sera")]
        [TestCase("<USER> <BOT> <CHAR> <GROUP> <CHARIFNOTGROUP>", "Al Sera Sera Sera Sera")]
        [TestCase("{{group}} {{groupNotMuted}} {{charIfNotGroup}} {{notChar}}", "Sera Sera Sera Al")]
        [TestCase("{{description}}", "Sera guards the forest.")]
        [TestCase("{{persona}}", "Likes Sera.")]
        [TestCase("{{mesExamples}}", "Al: Hi")]
        [TestCase("{{charFirstMessage}}/{{charFirstMessage::1}}/{{charFirstMessage::2}}/{{charFirstMessage::9}}", "Hi/Hello/Hey/")]
        [TestCase("{{lastMessage}}/{{lastUserMessage}}/{{lastCharMessage}}/{{lastMessageId}}/{{allChatRange}}", "last/last/Hello Al/2/0-2")]
        [TestCase("{{maxContextTokens}} {{maxPrompt}} {{maxResponseTokens}} {{model}}", "1000 900 100 test-model")]
        [TestCase("a{{newline}}b{{newline::2}}c", "a\nb\n\nc")]
        [TestCase("a{{space::3}}b{{noop}}", "a   b")]
        [TestCase("{{reverse::abc}} {{reverse abc}} {{reverse:abc}}", "cba cba cba")]
        [TestCase("one\n\n{{trim}}\n\ntwo", "onetwo")]
        [TestCase("a{{// a note }}b", "ab")]
        [TestCase("a{{ // }}\nhidden {{char}}\n{{ /// }}b", "ab")]
        [TestCase(@"\{\{user\}\}", "{{user}}")]
        [TestCase("{{notAMacro}} {{not a macro::x}}", "{{notAMacro}} {{not a macro::x}}")]
        [TestCase("{{instructStop}}{{banned \"x\"}}{{input}}{{summary}}", "")]
        public void Expands(string text, string expected)
        {
            Assert.AreEqual(expected, Run(text));
        }

        [TestCase("{{time}}", "3:07 PM")]
        [TestCase("{{date}}", "November 5, 2026")]
        [TestCase("{{weekday}}", "Thursday")]
        [TestCase("{{isotime}}", "15:07")]
        [TestCase("{{isodate}}", "2026-11-05")]
        [TestCase("{{datetimeformat::YYYY-MM-DD HH:mm:ss}}", "2026-11-05 15:07:09")]
        [TestCase("{{datetimeformat::dddd, MMMM Do [at] h:mm a}}", "Thursday, November 5th at 3:07 pm")]
        public void Times(string text, string expected)
        {
            Assert.AreEqual(expected, Run(text));
        }

        [Test]
        public void TimeWithOffsetIsFromUtc()
        {
            var context = Context();
            context.Now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual("2:00 PM", Run("{{time_UTC+2}}", context));
            Assert.AreEqual("2:00 PM", Run("{{time::UTC+2}}", context));
            Assert.AreEqual("7:00 AM", Run("{{time::UTC-5}}", context));
        }

        [TestCase(30, "a few seconds")]
        [TestCase(60, "a minute")]
        [TestCase(5 * 60, "5 minutes")]
        [TestCase(3600, "an hour")]
        [TestCase(3 * 3600, "3 hours")]
        [TestCase(24 * 3600, "a day")]
        [TestCase(5 * 24 * 3600, "5 days")]
        [TestCase(40 * 24 * 3600, "a month")]
        [TestCase(100 * 24 * 3600, "3 months")]
        public void HumanizesLikeMoment(int seconds, string expected)
        {
            Assert.AreEqual(expected, Macros.Humanize(TimeSpan.FromSeconds(seconds)));
        }

        [Test]
        public void IdleDuration()
        {
            var context = Context();
            Assert.AreEqual("just now", Run("{{idle_duration}}", context));
            context.LastUserMessage = Now.AddHours(-3);
            Assert.AreEqual("3 hours", Run("{{idleDuration}}", context));
        }

        [TestCase("{{timeDiff::2026-11-05 15:00::2026-11-05 12:00}}", "in 3 hours")]
        [TestCase("{{timeDiff::2026-11-05 12:00::2026-11-05 15:00}}", "3 hours ago")]
        public void TimeDiff(string text, string expected)
        {
            Assert.AreEqual(expected, Run(text));
        }

        [Test]
        public void RandomChoosesFromAnyListForm()
        {
            CollectionAssert.Contains(new[] { "a", "b" }, Run("{{random::a::b}}"));
            CollectionAssert.Contains(new[] { "a", "b" }, Run("{{random:a,b}}"));
            CollectionAssert.Contains(new[] { "a", "b" }, Run("{{random a, b}}"));
            Assert.AreEqual("x, y", Run(@"{{random::x\, y}}"));

            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
                seen.Add(Run("{{random::a::b::c}}"));
            Assert.AreEqual(3, seen.Count, "rolled again each time");
        }

        [Test]
        public void PickStaysPutForAChatWhateverSurroundsIt()
        {
            var context = Context();
            string text = "{{pick::a::b::c::d::e::f::g::h}}";
            string first = Run(text, context);
            Assert.AreEqual("Extra text. " + first, Run("Extra text. " + text, context), "same pick when the prompt around it changes");

            var seen = new HashSet<string>();
            for (int i = 0; i < 100; i++)
                seen.Add(Run(text)); // a new chat each time
            Assert.Greater(seen.Count, 1, "other chats can pick differently");

            string[] two = Run(text + "|" + text, context).Split('|');
            Assert.AreEqual(2, two.Length);
            Assert.AreEqual(first, two[0]);

            context.Local.Clear();
            var afterClearing = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                afterClearing.Add(Run(text, context));
                context.Local.Clear();
            }
            Assert.Greater(afterClearing.Count, 1, "clearing the chat picks again");
        }

        [Test]
        public void Rolls()
        {
            for (int i = 0; i < 100; i++)
            {
                int plain = int.Parse(Run("{{roll::1d6}}"));
                Assert.That(plain, Is.InRange(1, 6));
                Assert.That(int.Parse(Run("{{roll:20}}")), Is.InRange(1, 20));
                Assert.That(int.Parse(Run("{{roll 2d4+3}}")), Is.InRange(5, 11));
                Assert.That(int.Parse(Run("{{roll::d6-1}}")), Is.InRange(0, 5));
            }
            Assert.AreEqual("", Run("{{roll::banana}}"));
            Assert.AreEqual("", Run("{{roll::99999d6}}"));
        }

        [Test]
        public void VariablesFunctions()
        {
            var context = Context();
            Assert.AreEqual("", Run("{{setvar::mood::happy}}", context));
            Assert.AreEqual("happy", Run("{{getvar::mood}}", context));
            Assert.AreEqual("true|false", Run("{{hasvar::mood}}|{{hasvar::nope}}", context));
            Run("{{addvar::mood:: today}}", context);
            Assert.AreEqual("happytoday", Run("{{getvar::mood}}", context), "whitespace around arguments is ignored");
            Run("{{setvar::n::2}}{{addvar::n::3}}", context);
            Assert.AreEqual("5", Run("{{getvar::n}}", context));
            Assert.AreEqual("6", Run("{{incvar::n}}", context));
            Assert.AreEqual("5", Run("{{decvar::n}}", context));
            Run("{{deletevar::n}}", context);
            Assert.AreEqual("", Run("{{getvar::n}}", context));

            Run("{{setglobalvar::g::1}}", context);
            Assert.AreEqual("2", Run("{{incglobalvar::g}}", context));
            Assert.AreEqual("", Run("{{getvar::g}}", context), "local and global are separate");
        }

        [TestCase("{{.x = 5}}{{.x}}", "5")]
        [TestCase("{{.x = 5}}{{.x ++}}{{.x --}}", "65")]
        [TestCase("{{.x = 5}}{{.x += 2}}{{.x -= 1}}{{.x}}", "6")]
        [TestCase("{{.x = a}}{{.x += b}}{{.x}}", "ab")]
        [TestCase("{{.x || none}}", "none")]
        [TestCase("{{.x = 0}}{{.x || none}}/{{.x ?? none}}", "none/0")]
        [TestCase("{{.x ??= 3}}/{{.x}}", "3/3")]
        [TestCase("{{.x = 0}}{{.x ||= 3}}", "3")]
        [TestCase("{{.x = 5}}{{.x == 5}}{{.x != 5}}{{.x > 4}}{{.x >= 6}}{{.x < 6}}{{.x <= 4}}", "truefalsetruefalsetruefalse")]
        [TestCase("{{.nothing == {{noop}}}}", "true")]
        [TestCase("{{$g = 1}}{{$g}}{{.g}}", "1")]
        [TestCase("{{.my-var_1 = x}}{{.my-var_1}}", "x")]
        public void VariableShorthands(string text, string expected)
        {
            Assert.AreEqual(expected, Run(text));
        }

        [Test]
        public void InnerMacrosRunFirst()
        {
            var context = Context();
            Run("{{setvar::Sera_mood::calm}}", context);
            Assert.AreEqual("calm", Run("{{getvar::{{char}}_mood}}", context));
            Assert.AreEqual("arES", Run("{{reverse::{{reverse::{{reverse::Sera}}}}}}".Replace("Sera", "SEra"), context));
        }

        [TestCase("{{if 1}}yes{{/if}}", "yes")]
        [TestCase("{{if 0}}yes{{/if}}", "")]
        [TestCase("{{if off}}yes{{else}}no{{/if}}", "no")]
        [TestCase("{{if !no}}yes{{else}}no{{/if}}", "yes")]
        [TestCase("{{if description}}has one{{/if}}", "has one")]
        [TestCase("{{if scenario}}has one{{else}}none{{/if}}", "none")]
        [TestCase("{{if {{lastMessage}}}}{{char}}{{/if}}", "Sera")]
        [TestCase("{{if 1}}{{if 0}}a{{else}}b{{/if}}{{else}}c{{/if}}", "b")]
        [TestCase("{{if 0}}{{if 1}}a{{/if}}{{else}}c{{/if}}", "c")]
        [TestCase("{{if 1}}\n    one\n      two\n{{/if}}", "one\n  two")]
        [TestCase("{{#if 1}} kept {{/if}}", " kept ")]
        public void Conditionals(string text, string expected)
        {
            Assert.AreEqual(expected, Run(text));
        }

        [Test]
        public void ScenarioFollowsTheCharactersCheckbox()
        {
            var context = Context();
            context.Card.Scenario = "{{user}} wakes in a glade.";
            Assert.AreEqual("", Run("{{scenario}}", context), "off by default");
            Assert.AreEqual("none", Run("{{if scenario}}has one{{else}}none{{/if}}", context));

            context.Card.UseScenario = true;
            Assert.AreEqual("Al wakes in a glade.", Run("{{scenario}}", context));
        }

        [Test]
        public void OnlyTheChosenBranchRuns()
        {
            var context = Context();
            Run("{{if 0}}{{setvar::x::1}}{{else}}{{setvar::y::1}}{{/if}}", context);
            Assert.AreEqual("false|true", Run("{{hasvar::x}}|{{hasvar::y}}", context));
        }

        [Test]
        public void AFieldThatUsesItsOwnMacroEnds()
        {
            var context = Context();
            context.Card.Description = "{{description}}";
            Assert.DoesNotThrow(() => Run("{{description}}", context));
        }

        [Test]
        public void VariablesSurviveARestartUntilCleared()
        {
            string path = Path.Combine(Path.GetTempPath(), "skylarping-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var first = new MacroVariables(path);
                first.Set("name", "value");
                string seed = first.Seed;

                var second = new MacroVariables(path);
                Assert.AreEqual("value", second.Get("name"));
                Assert.AreEqual(seed, second.Seed);

                second.Clear();
                Assert.IsFalse(File.Exists(path));
                Assert.IsNull(new MacroVariables(path).Get("name"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ADamagedFileStartsEmpty()
        {
            string path = Path.Combine(Path.GetTempPath(), "skylarping-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "not json");
                Assert.IsNull(new MacroVariables(path).Get("name"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void SystemPromptRunsMacrosButHistoryOnlyGetsNames()
        {
            var card = new CharacterCard { Name = "Sera", Description = "Today is {{weekday}}.{{setvar::seen::1}}" };
            var macros = new MacroContext { Now = Now };
            var history = new[] { new ChatTurn(false, "{{user}} {{weekday}} {{setvar::chat::1}}") };

            var messages = PromptBuilder.Build(card, new UserProfile { Name = "Al" }, history, 0, macros: macros);

            StringAssert.Contains("Today is Thursday.", messages[0].Content);
            Assert.AreEqual("Al {{weekday}} {{setvar::chat::1}}", messages[1].Content);
            Assert.AreEqual("1", macros.Local.Get("seen"));
            Assert.IsNull(macros.Local.Get("chat"));
        }
    }
}
