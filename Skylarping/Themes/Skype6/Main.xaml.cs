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
using Skylarping.Models;
using Skylarping.Native.Windows;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Skylarping.Skype6
{
    public partial class Main : MainWindowBase
    {
        private readonly BitmapImage contactsBtnImage = ImageHelper.FreezeLoad("Themeable/Main/contacts.png");
        private readonly BitmapImage recentsBtnImage = ImageHelper.FreezeLoad("Themeable/Main/recents.png");
        private readonly BitmapImage sidebarBtnEmpty = ImageHelper.FreezeLoad("Themeable/Main/empty.png");

        public Main()
        {
            InitializeComponent();
            InitializeMainWindow();
            btnContacts.OverlayText.TextTrimming = TextTrimming.None;
            btnRecents.OverlayText.TextTrimming = TextTrimming.None;
            SourceInitialized += (s, e) => BuildMenu();
        }

        protected override void ShowHomeView()
        {
            HomeButton.SetState(ButtonVisualState.Pressed);
            Topbar.Visibility = Visibility.Collapsed;
            HomeTopbar.Visibility = Visibility.Visible;
            ChatProfileArea.Visibility = Visibility.Collapsed;
            MessageWindow.Visibility = Visibility.Collapsed;
            // A call stays up behind Home; coming back to the chat puts it back (SetCallPageLocation).
            FillMessagePanelHost.Visibility = Visibility.Collapsed;
            Home.Visibility = Visibility.Visible;
        }

        protected override void ShowChatView()
        {
            HomeButton.SetState(ButtonVisualState.Default);
            HomeTopbar.Visibility = Visibility.Collapsed;
            Topbar.Visibility = Visibility.Visible;
            ChatProfileArea.Visibility = Visibility.Visible;
            MessageWindow.Visibility = Visibility.Visible;
            Home.Visibility = Visibility.Collapsed;
        }

        protected override void ShowOwnName(string name) => StatusBox.Text = name;

        // Skype 6 has no credit line under the name.
        protected override void ShowCreditText(string text) { }

        // The sidebar tabs drop their icons as it narrows.
        protected override void OnSidebarResized()
        {
            if (SidebarColumn.Width.Value <= 185)
            {
                btnContacts.Source = sidebarBtnEmpty;
                btnRecents.Source = sidebarBtnEmpty;
                btnContacts.TextLeftMargin = 5;
                btnRecents.TextLeftMargin = 5;
                SidebarTabs.ColumnDefinitions[0].MinWidth = 0;
                SidebarTabs.ColumnDefinitions[0].Width = new GridLength(69);
                btnContacts.MaxWidth = 69;
                btnContacts.HorizontalAlignment = HorizontalAlignment.Left;
                btnContacts.TextHorizontalAlignment = HorizontalAlignment.Center;
            }
            else
            {
                btnContacts.Source = contactsBtnImage;
                btnRecents.Source = recentsBtnImage;
                btnContacts.TextLeftMargin = 31;
                btnRecents.TextLeftMargin = 31;
                SidebarTabs.ColumnDefinitions[0].MinWidth = 93;
                SidebarTabs.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                btnContacts.MaxWidth = double.MaxValue;
                btnContacts.HorizontalAlignment = HorizontalAlignment.Stretch;
                btnContacts.TextHorizontalAlignment = HorizontalAlignment.Left;
            }
        }

        private void HomeButton_Click(object sender, MouseButtonEventArgs e)
        {
            SetWindow(WindowType.Home);
        }

        #region Native menu bar

        private static (string, object) Item(string label, EventHandler handler = null) => (label, handler);
        private static (string, object) Sub(string label, NativeSubMenu menu) => (label, menu);
        private static (string, object) Separator() => ("$", null);
        private static string L(string key) => Universal.Lang[key];

        private static EventHandler Status(PresenceStatus status) =>
            async (s, e) => await Universal.Backend.SetConnectionStatus(status);

        // The same commands as the other themes' XAML menu; items without a handler are greyed out.
        private void BuildMenu()
        {
            var bar = new NativeMenuBar(this);

            var status = new NativeSubMenu(bar).Create(
                L("sMAINMENU_SKYPE_ONLINESTATUS"),
                (L("sTRAYHINT_USER_ONLINE"), Status(PresenceStatus.Online)),
                (L("sTRAYHINT_USER_AWAY"), Status(PresenceStatus.Away)),
                (L("sTRAYHINT_USER_DND"), Status(PresenceStatus.DoNotDisturb)),
                (L("sTRAYHINT_USER_INVISIBLE"), Status(PresenceStatus.Invisible)),
                (L("sTRAYHINT_USER_OFFLINE"), null),
                ("$", null),
                (L("sSTATUSMENU_CAPTION_CF_OPTIONS2"), null)
            );
            var profile = new NativeSubMenu(bar).Create(
                L("sMAINMENU_SKYPE_PROFILE"),
                (L("sCHANGE_MY_PICTURE"), (s, e) => OnChangePicture(null, null)),
                // TODO: lang
                ("Remove Your Picture", (s, e) => OnRemovePicture(null, null))
            );
            bar.Create(
                "&" + L("sMAINMENU_SKYPE"),
                Sub(L("sMAINMENU_SKYPE_ONLINESTATUS"), status),
                Sub(L("sMAINMENU_SKYPE_PROFILE"), profile),
                Separator(),
                Item(L("sMAINMENU_SKYPE_PRIVACY")),
                Item(L("sMAINMENU_SKYPE_ACCOUNT")),
                Item(L("sMAINMENU_SKYPE_BUYCREDIT")),
                Separator(),
                Item(L("sMAINMENU_SKYPE_CHANGEPASSWORD")),
                Item(L("sMAINMENU_SKYPE_SIGN_OUT"), (s, e) => OnSignOut(null, null)),
                Item(L("sMAINMENU_SKYPE_SWITCH_USER"), (s, e) => OnSwitchUser(null, null)),
                Item(L("sMAINMENU_SKYPE_CLOSE"), (s, e) => OnClose(null, null))
            );
            bar.Create(
                "&" + L("sMAINMENU_CONTACTS"),
                Item(L("sMAINMENU_CONTACTS_ADD_CONTACT"), (s, e) => OnAddContact(null, null)),
                Item(L("sMAINMENU_CONTACTS_NEW_CONTACT")),
                Item(L("sMAINMENU_CONTACTS_SEARCH")),
                Item(L("sMAINMENU_CONTACTS_IMPORT")),
                Item(L("sMAINMENU_CONTACTS_NEW_GROUP"), (s, e) => MakeGroup_Click(null, null)),
                Separator(),
                Item(L("sMAINMENU_CONTACTS_GROUPS")),
                Item(L("sMAINMENU_CONTACTS_SHOW_OUTLOOK")),
                Separator(),
                Item(L("sBUDDYMENU_REMOVE"))
            );
            bar.Create(
                "&" + L("sMAINMENU_CONVERSATION"),
                Item(L("sMAINMENU_CONVERSATION_PROFILE_PANEL")),
                Item(L("sMAINMENU_CONVERSATION_ADD_TO_CONTACTS")),
                Item(L("sMAINMENU_CONVERSATION_ADD_PEOPLE")),
                Item(L("sMAINMENU_CONVERSATION_RENAME")),
                Item(L("sMAINMENU_CONVERSATION_LEAVE")),
                Item(L("sMAINMENU_CONVERSATION_BLOCK")),
                Item(L("sMAINMENU_CONVERSATION_UNBLOCK")),
                Item(L("sCONVERSATION_MENU_NOTIFICATIONS")),
                Separator(),
                Item(L("sMAINMENU_CONVERSATION_SEARCH")),
                Item(L("sMAINMENU_CONVERSATION_OLD_MESSAGES")),
                Separator(),
                Item(L("sCONVERSATION_MARK_UNREAD")),
                Item(L("sCONVERSATION_MARK_READ")),
                Item(L("sMAINMENU_CONVERSATION_HIDE"))
            );
            bar.Create(
                "&" + L("sMAINMENU_CALL"),
                Item(L("sMAINMENU_CALL"), (s, e) => OnCall(null, null)),
                Item(L("sMAINMENU_CALL_START_VIDEO")),
                Item(L("sMAINMENU_CALL_ANSWER")),
                Separator(),
                Item(L("sMAINMENU_CALL_IGNORE")),
                Item(L("sMAINMENU_CALL_MUTE")),
                Item(L("sMAINMENU_CALL_UNMUTE")),
                Item(L("sMAINMENU_CALL_HOLD")),
                Item(L("sMAINMENU_CALL_RESUME")),
                Item(L("sMAINMENU_CALL_TRANSFER")),
                Item(L("sMAINMENU_CALL_HANG_UP")),
                Separator(),
                Item(L("sMAINMENU_CALL_CALL_A_PHONE_NUMBER")),
                Separator(),
                Item(L("sMAINMENU_CALL_AUDIO")),
                Item(L("sMAINMENU_CALL_VIDEO_SETTINGS")),
                Item(L("sMAINMENU_CALL_VIDEO_SNAPSHOT")),
                Separator(),
                Item(L("sMAINMENU_CALL_QUALITY")),
                Item(L("sCALL_TOOLBAR_TECHNICAL_INFO"))
            );
            bar.Create(
                "&" + L("sMAINMENU_VIEW"),
                Item(L("sMAINMENU_VIEW_CONTACTS"), (s, e) => OnContacts(null, null)),
                Item(L("sMAINMENU_VIEW_CONVERSATIONS"), (s, e) => OnRecent(null, null)),
                Item(L("sMAINMENU_VIEW_VOICEMAILS")),
                Item(L("sMAINMENU_VIEW_FILESSENT")),
                Item(L("sMAINMENU_VIEW_SMSMESSAGES")),
                Item(L("sMAINMENU_VIEW_INSTANT_MESSAGES")),
                Separator(),
                Item(L("sMAINMENU_VIEW_HOME"), (s, e) => OnHome(null, null)),
                Item(L("sMAINMENU_VIEW_PROFILE")),
                Item(L("sMAINMENU_VIEW_CALL_PHONES")),
                Item(L("sMAINMENU_VIEW_SNAPSHOTS_GALLERY")),
                Separator(),
                Item(L("sMAINMENU_VIEW_SINGLE_WINDOW_MODE")),
                Item(L("sMAINMENU_VIEW_MULTI_WINDOW_MODE")),
                Item(L("sMAINMENU_VIEW_FULLSCREEN")),
                Separator(),
                Item(L("sMAINMENU_SHOW_HIDDEN_CONV"))
            );
            bar.Create(
                "&" + L("sMAINMENU_TOOLS"),
                Item(L("sMAINMENU_TOOLS_LANGUAGE")),
                Separator(),
                Item(L("sMAINMENU_TOOLS_ACCESSIBILITY")),
                Item(L("sMAINMENU_TOOLS_SHARE")),
                Separator(),
                Item("AI Services...", (s, e) => OnAIServices(null, null)),
                Item("Connection Wizard...", (s, e) => OnConnectionWizard(null, null)),
                Item(L("sMAINMENU_TOOLS_OPTIONS"), (s, e) => OnOptions(null, null))
            );
            bar.Create(
                "&" + L("sMAINMENU_HELP"),
                Item(L("sMAINMENU_HELP_HELP")),
                Item(L("sMAINMENU_HELP_HEARTBEAT")),
                Separator(),
                Item(L("sMAINMENU_HELP_QUALITY")),
                Item(L("sMAINMENU_HELP_UPDATES")),
                Item(L("sZAPBUTTON_FEEDBACK")),
                Separator(),
                Item(L("sMAINMENU_HELP_ABOUT"), (s, e) => OnAbout(null, null)),
                Item(L("sMAINMENU_HELP_PRIVACY"), (s, e) => OnPrivacyPolicy(null, null))
            );
        }

        #endregion
    }
}
