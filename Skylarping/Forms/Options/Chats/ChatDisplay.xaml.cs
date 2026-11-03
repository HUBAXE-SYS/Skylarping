/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Windows;
using System.Windows.Controls;
using Skylarping.Enumerations;
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
            string mode = Settings.ThinkingDisplay.ToString();
            foreach (ComboBoxItem item in ThinkingBox.Items)
                if ((string)item.Tag == mode)
                    ThinkingBox.SelectedItem = item;
            MarkdownBox.IsChecked = Settings.MarkdownParsing;
            _loading = false;
        }

        private void ThinkingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(ThinkingBox.SelectedItem is ComboBoxItem item))
                return;
            ChatBlockDisplayMode mode;
            if (Enum.TryParse((string)item.Tag, out mode))
                Settings.ThinkingDisplay = mode;
        }

        private void MarkdownBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Settings.MarkdownParsing = MarkdownBox.IsChecked == true;
        }
    }
}
