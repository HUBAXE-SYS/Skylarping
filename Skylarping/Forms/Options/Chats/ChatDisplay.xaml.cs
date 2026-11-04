/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System.Windows;
using System.Windows.Controls;
using Skylarping.Preferences;

namespace Skylarping.Forms.OptionPages.Chats
{
    public partial class ChatDisplay : Page
    {
        private readonly bool _loading;

        public ChatDisplay()
        {
            InitializeComponent();
            _loading = true;
            MarkdownBox.IsChecked = Settings.MarkdownParsing;
            StreamBox.IsChecked = Settings.StreamReplies;
            EditBox.IsChecked = Settings.ShowEditMessage;
            RegenerateBox.IsChecked = Settings.ShowRegenerate;
            MoodHoursBox.Text = Settings.CharacterMoodHours.ToString();
            _loading = false;
        }

        private void MoodHoursBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int hours;
            if (!_loading && int.TryParse(MoodHoursBox.Text.Trim(), out hours) && hours >= 0)
                Settings.CharacterMoodHours = hours;
        }

        private void MoodHoursBox_LostFocus(object sender, RoutedEventArgs e)
        {
            MoodHoursBox.Text = Settings.CharacterMoodHours.ToString(); // undo anything that isn't a number
        }

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;
            Settings.StreamReplies = StreamBox.IsChecked == true;
            Settings.ShowEditMessage = EditBox.IsChecked == true;
            Settings.ShowRegenerate = RegenerateBox.IsChecked == true;
        }

        private void MarkdownBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Settings.MarkdownParsing = MarkdownBox.IsChecked == true;
        }
    }
}
