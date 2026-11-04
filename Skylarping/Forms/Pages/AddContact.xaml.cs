/*==========================================================*/
// Copyright © The Skymu Team and other contributors.
// For any inquiries or concerns, email contact@skymu.app.
/*==========================================================*/
// Modification or redistribution of this code is governed
// by the terms set out in the project license agreement.
// If you do not comply with those terms, you may not
// modify or distribute any original code from the project.
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Add a contact: create a character, or import a SillyTavern character card to
// prefill the form. The picture comes from the card or from the user's own file.
/*==========================================================*/

using Microsoft.Win32;
using Skylarping.AI.Tools;
using Skylarping.Characters;
using Skylarping.Helpers;
using Skylarping.Models;
using Skylarping.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skylarping.Forms.Pages
{
    public partial class AddContact : Page
    {
        private WindowBase window;

        // The imported card (null when creating from scratch). Fields the form doesn't show,
        // like the lorebook or alternate greetings, stay on it and are saved with the character.
        private CharacterCard _card;
        private byte[] _cardPicture; // the imported card's own picture, normalized
        private byte[] _picture;     // the picture that will be saved, normalized (null = default)
        private bool _adding;
        private readonly string _editId; // set when editing an existing character

        public AddContact()
        {
            InitializeComponent();
            UpdatePicture();
            LoadLore(null);
            BuildToolList();
            ProactiveMinBox.Text = CharacterCard.DefaultProactiveMinMinutes.ToString();
            ProactiveMaxBox.Text = CharacterCard.DefaultProactiveMaxMinutes.ToString();
            MoodHoursBox.Text = CharacterCard.DefaultMoodHours.ToString();
        }

        /// <summary>Edit Character: the same form, filled in from a saved character.</summary>
        public AddContact(StoredCharacter character) : this()
        {
            _editId = character.Id;
            _card = CharacterCard.FromJson(character.Card.ToJson()); // a copy, so Cancel changes nothing
            _picture = character.Avatar;
            FillForm(_card);
            FillTools(_card.Tools); // not in FillForm: importing a card keeps the tools already ticked
            UpdatePicture();
        }

        private bool Editing => _editId != null;

        public void Close() => window.Close();

        public void ShowWindow()
        {
            window = new WindowBase(this)
            {
                MinWidth = 600,
                MinHeight = 500,
                Width = 680,
                Height = 640,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Title = Settings.BrandingName + "™ - " + (Editing ? "Edit Character" : Universal.Lang["sADDADDAFRIEND_SCR1_CAPTION"]),
                HeaderIcon = Editing ? WindowBase.IconType.ContactFlat : WindowBase.IconType.ContactAdd,
                HeaderText = Editing ? "Edit " + _card.Name : Universal.Lang["sADDADDAFRIEND_SCR1_HEADER"],
                ButtonLeftText = Editing ? Universal.Lang["sF_OPTIONS_BUTTON_SAVE"] : Universal.Lang["sZAPBUTTON_ADDCONTACT"],
                ButtonRightText = Editing ? Universal.Lang["sZAPBUTTON_CANCEL"] : Universal.Lang["sZAPBUTTON_CLOSE"],
                ButtonLeftAction = AddCharacter,
                ButtonRightAction = () => window.Close()
            };
            window.ButtonLeft.MinWidth = 100;
            window.ButtonRight.MinWidth = 98;
            // Enter belongs to the multi-line boxes, not the Add button.
            window.ButtonLeft.IsDefault = false;
            UpdateAddButton();
            window.Show();
            NameBox.Focus();
        }

        // A multi-line box eats the mouse wheel even when it has nothing left to scroll;
        // hand the wheel to the form then, so scrolling over a box doesn't get stuck.
        private void LongText_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var box = (TextBox)sender;
            bool atTop = box.VerticalOffset <= 0;
            bool atBottom = box.VerticalOffset + box.ViewportHeight >= box.ExtentHeight - 1;
            if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
            {
                e.Handled = true;
                FormScroller.ScrollToVerticalOffset(FormScroller.VerticalOffset - e.Delta / 3.0);
            }
        }

        #region Import

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import a character card",
                Filter = CharacterCardFile.OpenFilter,
                CheckFileExists = true
            };
            if (dlg.ShowDialog(window) != true)
                return;

            LoadedCard loaded;
            try
            {
                loaded = CharacterCardFile.Load(dlg.FileName);
            }
            catch (CharacterCardException ex)
            {
                Universal.ShowMessage(ex.Message, "Import a character card", WindowBase.IconType.Error);
                return;
            }

            byte[] cardPicture = null;
            if (loaded.Image != null)
            {
                try { cardPicture = CharacterAvatar.Normalize(loaded.Image); }
                catch (CharacterCardException) { } // keep the card, just without its picture
            }

            _card = loaded.Card;
            _cardPicture = cardPicture;
            _picture = cardPicture; // an imported card brings its picture; the user can still change it
            FillForm(loaded.Card);
            UpdatePicture();
            ShowImportInfo(Path.GetFileName(dlg.FileName), loaded.Card, loaded.Image != null && cardPicture == null);
            FormScroller.ScrollToTop();
        }

        private void FillForm(CharacterCard card)
        {
            NameBox.Text = card.Name;
            DescriptionBox.Text = card.Description;
            PersonalityBox.Text = card.Personality;
            ExamplesBox.Text = card.MessageExamples;
            FirstMessageBox.Text = card.FirstMessage;
            ScenarioBox.Text = card.Scenario;
            SystemPromptBox.Text = card.SystemPrompt;
            PostHistoryBox.Text = card.PostHistoryInstructions;
            CreatorNotesBox.Text = card.CreatorNotes;
            TagsBox.Text = string.Join(", ", card.Tags.ToArray());
            CreatorBox.Text = card.Creator;
            VersionBox.Text = card.CharacterVersion;
            UseGreetingBox.IsChecked = card.UseGreeting; // both off for imported cards unless saved on by us
            UseScenarioBox.IsChecked = card.UseScenario;
            UseLorebookBox.IsChecked = card.UseLorebook;
            ProactiveBox.IsChecked = card.ProactiveMessages;
            SuppressActionsBox.IsChecked = card.SuppressActions;
            ProactiveMinBox.Text = card.ProactiveMinMinutes.ToString();
            ProactiveMaxBox.Text = card.ProactiveMaxMinutes.ToString();
            MoodHoursBox.Text = card.MoodHours.ToString();
            LoadLore(card.Lorebook);
            int alternates = card.AlternateGreetings.Count;
            GreetingHint.Text = "What they say to open a new conversation." + (alternates == 0 ? "" :
                " The card's " + alternates + " alternate greeting" + (alternates == 1 ? " is" : "s are") +
                " kept too, and can be picked from at random.");
            foreach (TextBox box in new[] { DescriptionBox, PersonalityBox, ExamplesBox, FirstMessageBox,
                                            ScenarioBox, SystemPromptBox, PostHistoryBox, CreatorNotesBox })
                box.ScrollToHome();
        }

        private void ShowImportInfo(string fileName, CharacterCard card, bool pictureFailed)
        {
            string info = "Imported \"" + fileName + "\" (" + CharacterCard.DescribeSpec(card.SourceSpec) + ").";
            if (card.HasLorebook)
                info += " Its lorebook has " + card.Lorebook.Entries.Count + " entr" + (card.Lorebook.Entries.Count == 1 ? "y" : "ies") + " (see Lorebook below).";
            if (pictureFailed)
                info += " Its picture couldn't be read, so choose one with Change Picture.";
            ImportInfo.Text = info;
            ImportInfo.Visibility = Visibility.Visible;
        }

        private void UseGreetingBox_Changed(object sender, RoutedEventArgs e)
        {
            GreetingPanel.Visibility = UseGreetingBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProactiveBox_Changed(object sender, RoutedEventArgs e)
        {
            ProactivePanel.Visibility = ProactiveBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UseScenarioBox_Changed(object sender, RoutedEventArgs e)
        {
            ScenarioPanel.Visibility = UseScenarioBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Tools

        private readonly Dictionary<string, CheckBox> _toolBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        private void BuildToolList()
        {
            ToolsPanel.Children.Clear();
            _toolBoxes.Clear();
            IList<ToolPackage> packages = ToolRegistry.Packages;
            if (packages.Count == 0)
            {
                ToolsPanel.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("FieldHint"),
                    Text = "No tools are installed. Tools go in the tools folder next to " + Universal.NAME + ".exe.",
                });
                return;
            }

            foreach (ToolPackage package in packages)
            {
                // The package box ticks or clears all of its tools; it shows a mixed state otherwise.
                var all = new CheckBox { Content = package.Name, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) };
                var boxes = new List<CheckBox>();
                var group = new StackPanel { Margin = new Thickness(18, 0, 0, 4) };
                foreach (ToolFunction function in package.Functions)
                {
                    var box = new CheckBox
                    {
                        // A TextBlock, not a string: a string's '_' would be taken as an access key.
                        Content = new TextBlock { Text = function.DisplayName },
                        Margin = new Thickness(0, 0, 0, 3),
                        ToolTip = function.Name + ": " + ToolRegistry.FillPlaceholders(function.Description, ToolRegistry.Config(package)),
                    };
                    box.Click += (s, e) => UpdatePackageBox(all, boxes);
                    boxes.Add(box);
                    group.Children.Add(box);
                    _toolBoxes[function.Name] = box;
                }
                all.Click += (s, e) =>
                {
                    bool on = all.IsChecked == true;
                    foreach (CheckBox box in boxes)
                        box.IsChecked = on;
                    UpdatePackageBox(all, boxes);
                };
                all.Tag = boxes;
                ToolsPanel.Children.Add(all);
                ToolsPanel.Children.Add(group);
                UpdatePackageBox(all, boxes);
            }
        }

        private static void UpdatePackageBox(CheckBox all, List<CheckBox> boxes)
        {
            int on = boxes.Count(b => b.IsChecked == true);
            all.IsThreeState = false;
            all.IsChecked = on == 0 ? false : on == boxes.Count ? (bool?)true : null;
        }

        private void FillTools(IEnumerable<string> tools)
        {
            var enabled = new HashSet<string>(tools ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _toolBoxes)
                kv.Value.IsChecked = enabled.Contains(kv.Key);
            foreach (CheckBox all in ToolsPanel.Children.OfType<CheckBox>())
                if (all.Tag is List<CheckBox> boxes)
                    UpdatePackageBox(all, boxes);
        }

        /// <summary>The ticked tools, plus any the card names that aren't installed here (kept, not lost).</summary>
        private List<string> SelectedTools(IEnumerable<string> previous)
        {
            var result = (previous ?? new string[0]).Where(t => !_toolBoxes.ContainsKey(t)).ToList();
            result.AddRange(_toolBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
            return result;
        }

        #endregion

        #region Lorebook

        private Lorebook _lore = new Lorebook();
        private bool _loreFromCard;
        private LoreEntry _loreEntry; // the entry shown in the editor
        private bool _loreLoading;

        private void LoadLore(Lorebook book)
        {
            _loreFromCard = book != null;
            _lore = book ?? new Lorebook();
            _loreEntry = null;
            LoreDepthBox.Text = _lore.ScanDepth > 0 ? _lore.ScanDepth.ToString() : "";
            LoreBudgetBox.Text = _lore.TokenBudget > 0 ? _lore.TokenBudget.ToString() : "";
            LoreRecursiveBox.IsChecked = _lore.RecursiveScanning;
            RefreshLoreList(null);
        }

        private void RefreshLoreList(LoreEntry select)
        {
            _loreLoading = true;
            LoreList.ItemsSource = null;
            LoreList.ItemsSource = _lore.Entries;
            LoreList.SelectedItem = select;
            _loreLoading = false;
            int n = _lore.Entries.Count;
            LoreExpander.Header = n == 0 ? "Lorebook" : "Lorebook (" + n + " entr" + (n == 1 ? "y" : "ies") + ")";
            ShowLoreEntry(select);
        }

        private void ShowLoreEntry(LoreEntry entry)
        {
            _loreEntry = entry;
            _loreLoading = true;
            LoreEditor.IsEnabled = entry != null;
            LoreRemoveButton.IsEnabled = entry != null;
            LoreNameBox.Text = entry?.Name ?? "";
            LoreKeysBox.Text = entry == null ? "" : string.Join(", ", entry.Keys.ToArray());
            LoreContentBox.Text = entry?.Content ?? "";
            LoreEnabledBox.IsChecked = entry?.Enabled ?? true;
            LoreConstantBox.IsChecked = entry?.Constant ?? false;
            LoreCaseBox.IsChecked = entry?.CaseSensitive ?? false;
            LoreRegexBox.IsChecked = entry?.UseRegex ?? false;
            LoreSelectiveBox.IsChecked = entry?.Selective ?? false;
            LoreSecondaryBox.Text = entry == null ? "" : string.Join(", ", entry.SecondaryKeys.ToArray());
            LoreOrderBox.Text = entry == null ? "" : entry.InsertionOrder.ToString();
            LorePriorityBox.Text = entry == null ? "" : entry.Priority.ToString();
            LorePositionBox.SelectedIndex = entry != null && entry.AfterDescription ? 1 : 0;
            _loreLoading = false;
        }

        /// <summary>Copies the editor's fields into the entry being edited.</summary>
        private void CommitLoreEntry()
        {
            LoreEntry e = _loreEntry;
            if (e == null || _loreLoading)
                return;
            e.Name = LoreNameBox.Text.Trim();
            // Keywords can contain commas only as regex; split plain lists on commas.
            e.Keys = SplitKeys(LoreKeysBox.Text);
            e.SecondaryKeys = SplitKeys(LoreSecondaryBox.Text);
            e.Content = CharacterCard.NormalizeNewlines(LoreContentBox.Text);
            e.Enabled = LoreEnabledBox.IsChecked == true;
            e.Constant = LoreConstantBox.IsChecked == true;
            e.CaseSensitive = LoreCaseBox.IsChecked == true;
            e.UseRegex = LoreRegexBox.IsChecked == true;
            e.Selective = LoreSelectiveBox.IsChecked == true;
            e.InsertionOrder = ParseNumber(LoreOrderBox.Text, e.InsertionOrder);
            e.Priority = ParseNumber(LorePriorityBox.Text, e.Priority);
            e.AfterDescription = LorePositionBox.SelectedIndex == 1;
        }

        private static List<string> SplitKeys(string text)
        {
            return (text ?? "").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        }

        private static int ParseNumber(string text, int fallback)
        {
            int value;
            return int.TryParse((text ?? "").Trim(), out value) ? value : fallback;
        }

        private static int ParseCount(string text, int fallback)
        {
            return Math.Max(0, ParseNumber(text, fallback));
        }

        private void LoreList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loreLoading)
                return;
            CommitLoreEntry();
            ShowLoreEntry(LoreList.SelectedItem as LoreEntry);
        }

        // Keep the list's titles current as fields are edited.
        private void LoreField_LostFocus(object sender, RoutedEventArgs e) => CommitAndRefreshTitle();
        private void LoreField_Changed(object sender, RoutedEventArgs e) => CommitAndRefreshTitle();

        private void LorePosition_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loreLoading)
                CommitLoreEntry();
        }

        private void CommitAndRefreshTitle()
        {
            if (_loreLoading || _loreEntry == null)
                return;
            CommitLoreEntry();
            _loreLoading = true;
            LoreList.Items.Refresh();
            LoreList.SelectedItem = _loreEntry;
            _loreLoading = false;
        }

        private void LoreAdd_Click(object sender, RoutedEventArgs e)
        {
            CommitLoreEntry();
            var entry = new LoreEntry
            {
                InsertionOrder = _lore.Entries.Count == 0 ? 100 : _lore.Entries.Max(x => x.InsertionOrder) + 1
            };
            _lore.Entries.Add(entry);
            RefreshLoreList(entry);
            LoreKeysBox.Focus();
        }

        private void LoreRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_loreEntry == null)
                return;
            _lore.Entries.Remove(_loreEntry);
            _loreEntry = null;
            RefreshLoreList(null);
        }

        #endregion

        #region Picture

        private void ChangePicture_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a picture",
                Filter = CharacterAvatar.OpenFilter,
                CheckFileExists = true
            };
            if (dlg.ShowDialog(window) != true)
                return;
            try
            {
                _picture = CharacterAvatar.Normalize(File.ReadAllBytes(dlg.FileName));
            }
            catch (Exception ex)
            {
                string message = ex is CharacterCardException ? ex.Message : "The picture couldn't be opened: " + ex.Message;
                Universal.ShowMessage(message, "Choose a picture", WindowBase.IconType.Error);
                return;
            }
            UpdatePicture();
        }

        private void CardPicture_Click(object sender, RoutedEventArgs e)
        {
            _picture = _cardPicture;
            UpdatePicture();
        }

        private void RemovePicture_Click(object sender, RoutedEventArgs e)
        {
            _picture = null;
            UpdatePicture();
        }

        private void UpdatePicture()
        {
            AvatarPreview.Source = _picture != null ? ImageHelper.GenerateFromArray(_picture) : Universal.AnonymousAvatar;
            CardPictureButton.IsEnabled = _cardPicture != null && _picture != _cardPicture;
            RemovePictureButton.IsEnabled = _picture != null;
        }

        #endregion

        #region Adding

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddButton();

        private void UpdateAddButton()
        {
            if (window != null)
                window.ButtonLeft.IsEnabled = !_adding && NameBox.Text.Trim().Length > 0;
        }

        private CharacterCard BuildCard()
        {
            CharacterCard card = _card ?? new CharacterCard();
            card.Name = NameBox.Text.Trim();
            card.Description = CharacterCard.NormalizeNewlines(DescriptionBox.Text);
            card.Personality = CharacterCard.NormalizeNewlines(PersonalityBox.Text);
            card.MessageExamples = CharacterCard.NormalizeNewlines(ExamplesBox.Text);
            // The greeting text is kept even when switched off, so turning it on later loses nothing.
            card.FirstMessage = CharacterCard.NormalizeNewlines(FirstMessageBox.Text);
            card.UseGreeting = UseGreetingBox.IsChecked == true;
            card.Scenario = CharacterCard.NormalizeNewlines(ScenarioBox.Text); // kept even when switched off
            card.UseScenario = UseScenarioBox.IsChecked == true;
            card.UseLorebook = UseLorebookBox.IsChecked == true;
            card.ProactiveMessages = ProactiveBox.IsChecked == true;
            card.SuppressActions = SuppressActionsBox.IsChecked == true;
            int quietMin = ParseNumber(ProactiveMinBox.Text, CharacterCard.DefaultProactiveMinMinutes);
            int quietMax = ParseNumber(ProactiveMaxBox.Text, CharacterCard.DefaultProactiveMaxMinutes);
            if (quietMin < 1) quietMin = CharacterCard.DefaultProactiveMinMinutes;
            if (quietMax < 1) quietMax = CharacterCard.DefaultProactiveMaxMinutes;
            card.ProactiveMinMinutes = Math.Min(quietMin, quietMax);
            card.ProactiveMaxMinutes = Math.Max(quietMin, quietMax);
            int moodHours = ParseNumber(MoodHoursBox.Text, CharacterCard.DefaultMoodHours);
            card.MoodHours = moodHours < 0 ? CharacterCard.DefaultMoodHours : moodHours;
            card.Tools = SelectedTools(card.Tools);
            CommitLoreEntry();
            _lore.ScanDepth = ParseCount(LoreDepthBox.Text, 0);
            _lore.TokenBudget = ParseCount(LoreBudgetBox.Text, 0);
            _lore.RecursiveScanning = LoreRecursiveBox.IsChecked == true;
            // Keep an imported (even emptied) lorebook; don't add an empty one to a card that had none.
            card.Lorebook = _lore.Entries.Count > 0 || _loreFromCard ? _lore : null;
            card.SystemPrompt = CharacterCard.NormalizeNewlines(SystemPromptBox.Text);
            card.PostHistoryInstructions = CharacterCard.NormalizeNewlines(PostHistoryBox.Text);
            card.CreatorNotes = CharacterCard.NormalizeNewlines(CreatorNotesBox.Text);
            card.Tags = CharacterCard.SplitTags(TagsBox.Text);
            card.Creator = CreatorBox.Text.Trim();
            card.CharacterVersion = VersionBox.Text.Trim();
            return card;
        }

        private void AddCharacter()
        {
            if (_adding || NameBox.Text.Trim().Length == 0)
                return;
            _ = IAddCharacter();
        }

        private async Task IAddCharacter()
        {
            if (Universal.Backend == null)
                return;
            _adding = true;
            UpdateAddButton();
            CharacterCard card = BuildCard();
            DirectMessage contact;
            try
            {
                if (Editing)
                {
                    contact = await Universal.Backend.UpdateCharacter(_editId, card, _picture);
                    if (contact == null)
                        throw new InvalidOperationException("That character doesn't exist any more.");
                    Universal.ActiveViewModel?.RefreshContact(contact);
                    window.Close();
                    return;
                }
                contact = await Universal.Backend.AddCharacter(card, _picture);
            }
            catch (Exception ex)
            {
                _adding = false;
                UpdateAddButton();
                Universal.ShowMessage("The character couldn't be saved: " + ex.Message, "Add a contact", WindowBase.IconType.Error);
                return;
            }

            DoneCaption.Text = card.Name + " has been added.";
            DoneAvatar.Source = _picture != null ? ImageHelper.GenerateFromArray(_picture) : Universal.AnonymousAvatar;
            MainTabControl.SelectedIndex = 1;
            window.ButtonLeft.Visibility = Visibility.Collapsed;
            window.ButtonRight.IsDefault = true;
        }

        #endregion
    }
}
