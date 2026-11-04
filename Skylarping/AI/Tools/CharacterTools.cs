/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// The tools one character may use (its card's extensions.skylarping.tools). Options are
// global (ToolRegistry.Config); {character_folder} in any value becomes this character's
// folder, which is how memories are kept per character (ToolDefaults.ini ships
// memoriesDirectory={character_folder}\memories) and removed with the character. Nothing
// here knows any tool by name.
// Context injectors (e.g. get_memory_context, the saved memories) run with the same
// config before every reply and go into the system prompt.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Skylarping.Characters;

namespace Skylarping.AI.Tools
{
    public sealed class CharacterTools
    {
        private readonly string _characterFolder;
        private readonly Dictionary<ToolPackage, Dictionary<string, string>> _configs =
            new Dictionary<ToolPackage, Dictionary<string, string>>();

        private CharacterTools(string characterFolder, List<ToolFunction> functions)
        {
            _characterFolder = characterFolder;
            Functions = functions;
            Schema = ToolRegistry.BuildToolsArray(functions, ConfigFor);
        }

        /// <summary>The character's tools that are installed, or null if it has none.</summary>
        public static CharacterTools For(StoredCharacter character)
        {
            if (character == null || character.Card.Tools == null || character.Card.Tools.Count == 0)
                return null;
            var functions = character.Card.Tools.Select(ToolRegistry.Find).Where(f => f != null)
                                     .Distinct().ToList();
            return functions.Count == 0 ? null : new CharacterTools(character.Folder, functions);
        }

        public IList<ToolFunction> Functions { get; private set; }

        /// <summary>The OpenAI "tools" array sent with each request.</summary>
        public JArray Schema { get; private set; }

        public IDictionary<string, string> ConfigFor(ToolPackage package)
        {
            Dictionary<string, string> config;
            if (!_configs.TryGetValue(package, out config))
            {
                config = ToolRegistry.Config(package);
                foreach (string key in config.Keys.ToList())
                    config[key] = ToolRegistry.ExpandHostPlaceholders(config[key], _characterFolder);
                _configs[package] = config;
            }
            return config;
        }

        /// <summary>Text the packages want in the system prompt (e.g. the saved memories).</summary>
        public List<string> ContextInjections(CancellationToken cancel)
        {
            var texts = new List<string>();
            foreach (ToolPackage package in Functions.Select(f => f.Package).Distinct())
            {
                string text = ToolRegistry.RunContextInjector(package, ConfigFor(package), cancel);
                if (!string.IsNullOrEmpty(text))
                    texts.Add(text);
            }
            return texts;
        }

        /// <summary>Runs a call the model made; the result is the text to send back as the tool message.</summary>
        public ToolOutput Run(LlmToolCall call, CancellationToken cancel)
        {
            ToolFunction function = Functions.FirstOrDefault(f => string.Equals(f.Name, call.Name, StringComparison.OrdinalIgnoreCase));
            if (function == null)
                return new ToolOutput(ToolRegistry.Format(call.Name, "error: unknown tool '" + call.Name + "'.", 1));
            return ToolRegistry.Run(function, call.Arguments, ConfigFor(function.Package),
                Preferences.Settings.GetToolTimeout(function.Name), cancel);
        }
    }
}
