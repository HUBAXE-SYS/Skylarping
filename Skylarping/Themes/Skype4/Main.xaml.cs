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

using Skylarping.Preferences;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skylarping.Skype4
{
    public partial class Main : MainWindowBase
    {
        public Main()
        {
            InitializeComponent();
            InitializeMainWindow();
        }

        protected override string AeroTitleFont => "Arial";

        protected override void ShowCreditText(string text) => SkypeCreditBox.Text = text;

        protected override void ShowHomeView()
        {
            Home.Visibility = Visibility.Visible;
            ChatArea.Visibility = Visibility.Collapsed;
            TopbarWindowRow.Height = new GridLength(1, GridUnitType.Star);
        }

        protected override void ShowChatView()
        {
            StatusHeader.SetState(ButtonVisualState.Default);
            Home.Visibility = Visibility.Collapsed;
            ChatArea.Visibility = Visibility.Visible;
        }

        protected override void ShowOwnName(string name) => StatusHeader.Text = name;

        protected override void OnTabSelected(SliceControl tab)
        {
            ConversationsHint.Visibility = Visibility.Collapsed;
            if (tab == btnContacts)
            {
                SplashHeader.Text = Universal.Lang["sZAPBUTTON_CONTACTS"];
                SplashDescription.Text = "Choose a contact and start talking.";
            }
            else if (tab == btnRecents)
            {
                SplashHeader.Text = "Conversations";
                SplashDescription.Text = "Choose a conversation to pick up again.";
                if (Settings.InboxNoticeShown != true)
                    ConversationsHint.Visibility = Visibility.Visible;
            }
        }

        private void SBNewBtn_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = (ContextMenu)SBNewBtn.Resources["Menu"];
            menu.PlacementTarget = SBNewBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void NewMenuItemClick(object sender, RoutedEventArgs e)
        {
            string name = ((MenuItem)sender).Name.Substring(3);
            switch (name)
            {
                case "contact":
                    vmodel.ShowAddContactWindow();
                    break;
                case "group":
                    vmodel.ShowNewGroupWindow();
                    break;
                case "import":
                    Universal.NotImplemented("Importing contacts");
                    break;
            }
        }

        private void OnContactsLink(object sender, MouseButtonEventArgs e)
        {
            SelectTab(btnContacts);
        }

        private void CloseConversationHint(object sender, MouseButtonEventArgs e)
        {
            ConversationsHint.Visibility = Visibility.Collapsed;
            Settings.InboxNoticeShown = true;
            Settings.Save();
        }

        private void Directory_Click(object sender, MouseButtonEventArgs e) => Universal.NotImplemented("Directory");
    }
}
