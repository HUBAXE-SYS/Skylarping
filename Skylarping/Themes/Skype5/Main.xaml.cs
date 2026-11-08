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

using Skylarping.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Skylarping.Skype5
{
    public partial class Main : MainWindowBase
    {
        private readonly BitmapImage contactsBtnImage = ImageHelper.FreezeLoad(
            "Themeable/Main/contacts.png"
        );
        private readonly BitmapImage recentsBtnImage = ImageHelper.FreezeLoad(
            "Themeable/Main/recents.png"
        );
        private readonly BitmapImage contactsBtnImageEmpty = ImageHelper.FreezeLoad(
            "Themeable/Main/contacts-empty.png"
        );
        private readonly BitmapImage recentsBtnImageEmpty = ImageHelper.FreezeLoad(
            "Themeable/Main/recents-empty.png"
        );

        public Main()
        {
            InitializeComponent();
            InitializeMainWindow();
            // Their captions are swapped for shorter ones as the sidebar narrows; never trim them.
            AddContactButton.OverlayText.TextTrimming = TextTrimming.None;
            MakeGroupButton.OverlayText.TextTrimming = TextTrimming.None;
        }

        protected override string AeroTitleFont => "Segoe UI";

        protected override void ShowCreditText(string text) => SkypeCreditBox.Text = text;

        protected override void ShowHomeView()
        {
            ToggleStatusBoxSelection(true);

            HomeTopbar.Visibility = Visibility.Visible;
            ChatTopbar.Visibility = Visibility.Collapsed;
            ChatProfileArea.Visibility = Visibility.Collapsed;
            MessageWindow.Visibility = Visibility.Collapsed;
            // A call stays up behind Home; coming back to the chat puts it back (SetCallPageLocation).
            FillMessagePanelHost.Visibility = Visibility.Collapsed;
            Home.Visibility = Visibility.Visible;

            MainPageButton.SetState(ButtonVisualState.Pressed);
        }

        protected override void ShowChatView()
        {
            ToggleStatusBoxSelection(false);
            HomeTopbar.Visibility = Visibility.Collapsed;
            ChatTopbar.Visibility = Visibility.Visible;
            ChatProfileArea.Visibility = Visibility.Visible;
            MessageWindow.Visibility = Visibility.Visible;
            Home.Visibility = Visibility.Collapsed;
        }

        protected override void ShowOwnName(string name) => StatusBox.Text = name;

        private void ToggleStatusBoxSelection(bool selected)
        {
            StatusBox.SetState(selected ? ButtonVisualState.Pressed : ButtonVisualState.Default);
            StatusBox.TextColor = selected
                ? Brushes.White
                : (SolidColorBrush)Application.Current.Resources["Text.HighContrast"];
            SBHomeButton.SetState(selected ? ButtonVisualState.Pressed : ButtonVisualState.Default);
        }

        // The sidebar tabs and buttons drop their icons and long captions as it narrows.
        protected override void OnSidebarResized()
        {
            if (SidebarColumn.Width.Value <= 185)
            {
                btnContacts.Source = contactsBtnImageEmpty;
                btnRecents.Source = recentsBtnImageEmpty;
                btnContacts.TextLeftMargin = 5;
                btnRecents.TextLeftMargin = 5;
                SidebarTabs.ColumnDefinitions[0].Width = GridLength.Auto;
                btnContacts.HorizontalAlignment = HorizontalAlignment.Left;
                btnContacts.TextHorizontalAlignment = HorizontalAlignment.Center;
            }
            else
            {
                btnContacts.Source = contactsBtnImage;
                btnRecents.Source = recentsBtnImage;
                btnContacts.TextLeftMargin = 30;
                btnRecents.TextLeftMargin = 30;
                SidebarTabs.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                btnContacts.HorizontalAlignment = HorizontalAlignment.Stretch;
                btnContacts.TextHorizontalAlignment = HorizontalAlignment.Left;
            }
            if (SidebarColumn.Width.Value < 195)
            {
                MakeGroupButton.OverlayText.Visibility = Visibility.Collapsed;
                MakeGroupButton.TextLeftMargin = 0;
            }
            else
            {
                MakeGroupButton.OverlayText.Visibility = Visibility.Visible;
                MakeGroupButton.TextLeftMargin = 41;
            }
            if (SidebarColumn.Width.Value < 245)
                MakeGroupButton.Text = Universal.Lang["sCREATE_GROUP_SHORT"];
            else
                MakeGroupButton.Text = Universal.Lang["sCREATE_GROUP_LONG"];
        }

        // The chat tab's Close button, and clicking your own name above the tabs.
        private void Chat_Close(object sender, MouseButtonEventArgs e)
        {
            SetWindow(WindowType.Home);
        }

        private void MakeGroup_Click(object sender, MouseButtonEventArgs e)
        {
            vmodel.ShowNewGroupWindow();
        }

        private void CallDropdown_Click(object sender, MouseButtonEventArgs e)
        {
            Universal.NotImplemented("Call options");
        }
    }
}
