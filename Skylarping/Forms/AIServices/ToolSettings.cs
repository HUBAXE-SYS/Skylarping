/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tools > AI Services > Tools. Two pages built from the installed tool manifests:
//  - Settings: the most rounds of tool use per reply (Settings.MaxToolRounds), then each
//    package's options (SimpleLLMChat manifest "options": string/int/bool), filled in and
//    ordered the way SimpleLLMChat's Options window does it.
//    {character_folder} in a value means the folder of whichever character uses the tool.
//  - Timeouts: seconds per tool, 0 = no timeout (SimpleLLMChat's tooltimeout.<tool>).
// Like the Language Model page these edit a draft; the window commits it on Save.
// Which tools a character may use is set per character, in Edit Character > Tools.
/*==========================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Skylarping.AI.Tools;
using Skylarping.Preferences;

namespace Skylarping.Forms.AIServicePages
{
    public sealed class ToolSettings : Page
    {
        private readonly bool _timeouts;
        // option -> (package, control), one per ini key; tool name -> box for timeouts
        private readonly List<KeyValuePair<ToolOption, KeyValuePair<ToolPackage, Control>>> _options =
            new List<KeyValuePair<ToolOption, KeyValuePair<ToolPackage, Control>>>();
        private readonly Dictionary<string, TextBox> _timeoutBoxes = new Dictionary<string, TextBox>();
        private TextBox _maxRoundsBox;

        public ToolSettings(bool timeouts)
        {
            _timeouts = timeouts;
            FontFamily = new System.Windows.Media.FontFamily("Tahoma");
            FontSize = 11;
            SetResourceReference(ForegroundProperty, "Text.VeryHighContrast");
            if (Application.Current.TryFindResource("Page") is Style style)
                Style = style;

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            Content = new ScrollViewer { Margin = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };

            if (!timeouts)
            {
                // Applies to every character, so it's shown even with no tools installed.
                stack.Children.Add(new TextBlock
                {
                    Text = "Most rounds of tool use in one reply (after that the character has to answer):",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3),
                });
                _maxRoundsBox = new TextBox
                {
                    Text = Settings.MaxToolRounds.ToString(),
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 12),
                    FontFamily = FontFamily,
                };
                DigitsOnly(_maxRoundsBox);
                stack.Children.Add(_maxRoundsBox);
            }

            IList<ToolPackage> packages = ToolRegistry.Packages;
            if (packages.Count == 0)
            {
                stack.Children.Add(Hint("No tools are installed. Tools go in the tools folder next to " + Universal.NAME + ".exe."));
                return;
            }

            stack.Children.Add(Hint(timeouts
                ? "How long each tool may run before it's stopped, in seconds. 0 = no limit."
                : "Settings for the installed tools. Which tools a character can use is set per character, in Edit Character > Tools. Clear a box to go back to the tool's default. " +
                  "In a folder setting, " + ToolRegistry.CharacterFolderPlaceholder + " means the folder of the character using the tool, so each character gets their own (e.g. their own memories)."));

            foreach (ToolPackage package in packages)
            {
                if (timeouts)
                    AddTimeouts(stack, package);
                else
                    AddOptions(stack, package);
            }
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) };
        }

        private void AddOptions(StackPanel stack, ToolPackage package)
        {
            // Options are global by ini key (as in LLMSettings.ini): a key already shown for an
            // earlier package is the same setting, so it isn't repeated.
            var options = package.Options.Where(o => !_options.Any(e => string.Equals(e.Key.Name, o.Name, System.StringComparison.OrdinalIgnoreCase))).ToList();
            if (options.Count == 0)
                return;
            stack.Children.Add(Header(package.Name));

            // As in SimpleLLMChat's Options: every box shows the value in use (the saved setting,
            // or the default when there is none), text options first, then checkboxes, each
            // sorted by label. Save writes them all.
            var byLabel = System.StringComparer.OrdinalIgnoreCase;
            var textOptions = options.Where(o => o.Type != "bool").OrderBy(o => o.Label, byLabel).ToList();
            var boolOptions = options.Where(o => o.Type == "bool").OrderBy(o => o.Label, byLabel).ToList();

            foreach (ToolOption option in textOptions)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = option.Label + ":",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3),
                });
                var box = new TextBox { Text = CurrentValue(option), Margin = new Thickness(0, 0, 0, 8), FontFamily = FontFamily };
                if (option.Type == "int")
                {
                    box.Width = 100;
                    box.HorizontalAlignment = HorizontalAlignment.Left;
                    DigitsOnly(box);
                }
                stack.Children.Add(box);
                Track(option, package, box);
            }

            if (boolOptions.Count > 0 && textOptions.Count > 0)
                stack.Children.Add(new Border { Height = 4 });
            foreach (ToolOption option in boolOptions)
            {
                string value = CurrentValue(option);
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap },
                    IsChecked = value == "1" || string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                stack.Children.Add(box);
                Track(option, package, box);
            }
        }

        /// <summary>The saved setting, or the default (ToolDefaults.ini, else the manifest) when there is none.</summary>
        private static string CurrentValue(ToolOption option)
        {
            string set = Settings.GetToolOption(option.Name);
            return string.IsNullOrEmpty(set) ? ToolRegistry.DefaultFor(option) : set;
        }

        private void Track(ToolOption option, ToolPackage package, Control control)
        {
            _options.Add(new KeyValuePair<ToolOption, KeyValuePair<ToolPackage, Control>>(
                option, new KeyValuePair<ToolPackage, Control>(package, control)));
        }

        private void AddTimeouts(StackPanel stack, ToolPackage package)
        {
            stack.Children.Add(Header(package.Name));
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            foreach (ToolFunction function in package.Functions)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = function.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
                var box = new TextBox
                {
                    Text = Settings.GetToolTimeout(function.Name).ToString(),
                    Margin = new Thickness(0, 2, 0, 2),
                    FontFamily = FontFamily,
                };
                DigitsOnly(box);
                Grid.SetRow(label, row);
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                grid.Children.Add(label);
                grid.Children.Add(box);
                _timeoutBoxes[function.Name] = box;
            }
            stack.Children.Add(grid);
        }

        private static void DigitsOnly(TextBox box)
        {
            box.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            DataObject.AddPastingHandler(box, (s, e) =>
            {
                string text = e.DataObject.GetDataPresent(DataFormats.Text) ? (string)e.DataObject.GetData(DataFormats.Text) : "";
                if (!text.All(char.IsDigit))
                    e.CancelCommand();
            });
        }

        /// <summary>Null if the page can be saved, otherwise what's wrong.</summary>
        public string Validate()
        {
            int rounds;
            if (_maxRoundsBox != null && (!int.TryParse(_maxRoundsBox.Text.Trim(), out rounds) || rounds < 1))
                return "The most rounds of tool use must be a whole number, at least 1.";
            foreach (var kv in _timeoutBoxes)
            {
                int seconds;
                string text = kv.Value.Text.Trim();
                if (text.Length > 0 && !int.TryParse(text, out seconds))
                    return "The timeout for " + kv.Key + " must be a whole number of seconds (0 = no limit).";
            }
            foreach (var entry in _options)
            {
                var box = entry.Value.Value as TextBox;
                int number;
                if (entry.Key.Type == "int" && box != null && box.Text.Trim().Length > 0 && !int.TryParse(box.Text.Trim(), out number))
                    return entry.Value.Key.Name + ": \"" + entry.Key.Label + "\" must be a whole number.";
            }
            return null;
        }

        public void Commit()
        {
            int maxRounds;
            if (_maxRoundsBox != null && int.TryParse(_maxRoundsBox.Text.Trim(), out maxRounds))
                Settings.MaxToolRounds = maxRounds;
            foreach (var kv in _timeoutBoxes)
            {
                int seconds;
                int.TryParse(kv.Value.Text.Trim(), out seconds);
                Settings.SetToolTimeout(kv.Key, seconds);
            }
            foreach (var entry in _options)
            {
                ToolOption option = entry.Key;
                Control control = entry.Value.Value;
                string value = control is CheckBox box
                    ? (box.IsChecked == true ? "1" : "0")
                    : ((TextBox)control).Text.Trim();
                Settings.SetToolOption(option.Name, value);
            }
        }
    }
}
