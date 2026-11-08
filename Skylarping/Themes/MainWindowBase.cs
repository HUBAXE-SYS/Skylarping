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

using Skylarping.Formatting;
using Skylarping.Forms;
using Skylarping.Forms.Pages;
using Skylarping.Helpers;
using Skylarping.Infrastructure.Main;
using Skylarping.Native.Windows;
using Skylarping.ViewModels;
using Skylarping.Sounds;
using Skylarping.Preferences;
using Skylarping.Enumerations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Skylarping.Backend;
using Skylarping.Models;
using System.Linq;

namespace Skylarping
{
    /// <summary>
    ///  The main window's behaviour, shared by every theme. A theme is its XAML (x:Class derives
    ///  from this) plus a small subclass with the hooks below; the elements its XAML names are
    ///  reached through MainWindowBase.Parts.cs.
    /// </summary>
    public abstract partial class MainWindowBase : Window, IMainWindowHolder
    {
        #region Variables

        // ViewModel
        protected MainViewModel vmodel;

        // Other file-level variables
        private bool noCloseEvent;
        private bool _conversationOpening;
        private ScrollViewer _conversationScrollViewer;
        private Dictionary<SliceControl, ColumnDefinition> buttonToColumn;
        private User _oldUser;
        private WindowType current_window = WindowType.Chat;
        private string PlaceholderTextMTB = string.Empty;
        public event EventHandler Ready;

        protected enum WindowType
        {
            Home,
            Chat,
        }


        public static readonly DependencyProperty WindowTitleProperty = DependencyProperty.Register(
            "WindowTitle",
            typeof(string),
            typeof(MainWindowBase),
            new PropertyMetadata(
                null,
                (d, e) =>
                {
                    ((MainWindowBase)d).Title = (string)e.NewValue;
                }
            )
        );

        public string WindowTitle
        {
            get { return (string)GetValue(WindowTitleProperty); }
            set { SetValue(WindowTitleProperty, value); }
        }

        private BitmapImage sendBtnSmall = ImageHelper.FreezeLoad(
            "Universal/Main/msg-send-button.png"
        );
        private BitmapImage sendBtnFull = ImageHelper.FreezeLoad(
            "Universal/Main/msg-send-button-full.png"
        );

        private Metadata SelectedContact;

        #endregion

        #region Theme hooks


        /// <summary>Switches the content area to Home.</summary>
        protected abstract void ShowHomeView();

        /// <summary>Switches the content area to the open conversation.</summary>
        protected abstract void ShowChatView();

        /// <summary>Shows the signed-in user's name above the sidebar tabs.</summary>
        protected abstract void ShowOwnName(string name);

        /// <summary>Shows the credit line under the user's name.</summary>
        protected abstract void ShowCreditText(string text);

        /// <summary>A sidebar tab was picked and its list filled.</summary>
        protected virtual void OnTabSelected(SliceControl tab) { }

        /// <summary>The sidebar was dragged to a new width, or restored at startup.</summary>
        protected virtual void OnSidebarResized() { }

        /// <summary>Sets up a frame drawn by the theme. Themes on the system frame have nothing to do.</summary>
        protected virtual void InitializeFrame() { }

        /// <summary>Shows the user's name on a title bar drawn by the theme.</summary>
        protected virtual void ShowCaption(string name) { }

        /// <summary>The window gained or lost focus.</summary>
        protected virtual void OnActivationChanged(bool active) { }

        #endregion

        #region BitmapImage generators

        private BitmapImage GenerateAvatarImage(string avatar)
        {
            return ImageHelper.FreezeLoad("Themeable/Profile Pictures/" + avatar + ".png");
        }

        #endregion

        #region Home and Chat window switching

        protected void SetWindow(WindowType type, bool force = false)
        {
            CallButton.Visibility = Visibility.Visible;
            CallButton.Text = vmodel.SelectedConversation is Group
                ? Universal.Lang["sZAPBUTTON_CALLGROUP"]
                : Universal.Lang["sZAPBUTTON_CALL"];

            if (type == current_window && !force)
                return;

            current_window = type;
            switch (type)
            {
                case WindowType.Home:
                    ClearConversation();
                    ShowHomeView();

                    MessageWindowRow.Height = new GridLength(0);
                    ConversationList.SelectedItem = null;
                    SelectedContact = null;

                    MessageWindowRow.Height = new GridLength(0);

                    ChatTopBarSplitter.Visibility = Visibility.Collapsed;
                    ChatTopbarSplitterRow.MaxHeight = 0;
                    TWR_ORIGINAL_HEIGHT = TopbarWindowRow.Height.Value;
                    TopbarWindowRow.Height = new GridLength(1, GridUnitType.Star);
                    MessageWindowRow.Height = new GridLength(0);
                    TopbarWindowRow.MaxHeight = Double.PositiveInfinity;
                    break;

                case WindowType.Chat:
                    ShowChatView();


                    MessageWindowRow.Height = new GridLength(1, GridUnitType.Star);

                    ChatTopBarSplitter.Visibility = Visibility.Visible;
                    ChatTopbarSplitterRow.MaxHeight = CTR_ORIGINAL_MAXHEIGHT;
                    TopbarWindowRow.Height = new GridLength(TWR_ORIGINAL_HEIGHT);
                    TopbarWindowRow.MaxHeight = screen == null ? TWR_ORIGINAL_MAXHEIGHT : ChatArea.ActualHeight * 0.7;
                    if (location != null)
                        SetCallPageLocation(location);
                    break;
            }
        }

        #endregion

        #region Window activation

        private void HandleWindowActivated()
        {
            if (vmodel != null)
            {
                vmodel.IsWindowActive = true;
                vmodel.OnWindowActivated();
            }
            OnActivationChanged(true);
        }

        private void HandleWindowDeactivated()
        {
            if (vmodel != null)
                vmodel.IsWindowActive = false;
            OnActivationChanged(false);
        }

        #endregion

        #region Sidebar tab selection and population
        internal async Task InitSidebar() => await vmodel.InitSidebar();

        public Task BeginLoading() => vmodel.InitSidebar();

        private async void HandleConversationSelection(object selected_item)
        {
            if (selected_item == null)
                return;

            ChatArea.DataContext = selected_item;
            vmodel.SelectedConversation = (Conversation)selected_item;
            await SetConversation();
        }

        private void ConfigureCompactRecentsList()
        {
            var grouped = CompactRecentsHelper.GroupByDate(vmodel.ConversationList);
            var selector = new CompactRecentsTemplateSelector
            {
                DateHeaderTemplate = (DataTemplate)FindResource("DateHeaderTemplate"),
                CompactDirectMessageTemplate = (DataTemplate)FindResource(
                    "CompactDirectMessageTemplate"
                ),
                CompactGroupTemplate = (DataTemplate)FindResource("CompactGroupTemplate"),
            };
            ConversationList.ItemTemplateSelector = selector;
            ConversationList.ItemsSource = grouped;

            // Rebuilding the list (a new message re-sorts it) drops the selection: put it back.
            string open = vmodel.SelectedConversation?.Identifier;
            if (open != null)
                foreach (object item in ConversationList.Items)
                    if (item is Conversation c && c.Identifier == open)
                    {
                        ConversationList.SelectedItem = item;
                        break;
                    }
        }

        private SliceControl StartupTabButton =>
            Settings.StartupTab == StartupTab.Conversations ? btnRecents : btnContacts;

        protected void SelectTab(SliceControl tab_to_select)
        {
            ConversationList.ItemsSource = null;

            // ButtonStateOnInit too: a tab's own Loaded (which fires after the window's, where
            // the startup tab is picked) re-applies it and would otherwise undo this.
            foreach (var tab in new[] { btnContacts, btnRecents })
            {
                var state = tab == tab_to_select ? ButtonVisualState.Pressed : ButtonVisualState.Default;
                tab.ButtonStateOnInit = state;
                tab.SetState(state);
            }

            switch (tab_to_select.Name)

            {
                case "btnContacts":
                    ConversationList.ItemTemplateSelector = null;
                    ConversationList.ItemsSource = vmodel.ContactList;
                    break;
                case "btnRecents":
                    ConfigureCompactRecentsList();
                    break;
            }
            OnTabSelected(tab_to_select);
            if (SelectedContact != null && SelectedContact is Metadata)
            {
                foreach (object item in ConversationList.Items)
                {
                    if (
                        item is Conversation
                        && ((Metadata)item).Identifier == ((Metadata)SelectedContact).Identifier
                    )
                    {
                        ConversationList.SelectedItem = item;
                    }
                }
            }
        }

        #endregion

        #region Resizing stuff

        private double CTR_ORIGINAL_MAXHEIGHT;
        private double CBR_ORIGINAL_HEIGHT;

        private double TWR_ORIGINAL_HEIGHT;
        private double TWR_ORIGINAL_MINHEIGHT;
        private double TWR_ORIGINAL_MAXHEIGHT;
        private double TWR_HEIGHT_CALLS_CHAT;
        private double TWR_ORIGINAL_MAXHEIGHT_CALLS;

        private double SIDEBAR_ORIGINAL_WIDTH; // dynamic
        private double SIDEBAR_ORIGINAL_MINWIDTH;
        private GridLength SIDEBAR_SPLITTER_ORIGINAL_WIDTH;

        private bool SkypeSplitterIsDragging = false;
        private bool ChatTopBarSplitterIsDragging = false;
        private Point dragStart;
        private UIElement capturedElement = null;

        protected void SkypeSplitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (SkypeSplitterIsDragging)
            {
                Point current = e.GetPosition(this);
                Vector delta = current - dragStart;
                ColumnDefinition sidebarCol = ContentArea.ColumnDefinitions[0];
                double max = sidebarCol.MaxWidth;
                double min = sidebarCol.MinWidth;

                double newWidth = sidebarCol.Width.Value + delta.X;

                if (newWidth < min)
                    newWidth = min;
                if (newWidth > max)
                    newWidth = max;

                sidebarCol.Width = new GridLength(newWidth);
                dragStart = current;

                OnSidebarResized();
            }
        }

        protected void SkypeSplitter_Press(object sender, MouseButtonEventArgs e)
        {
            MouseRelease(null, null); // Release previous element first. Potentially avoids weird behavior on touch screen, etc.
            SkypeSplitterIsDragging = true;
            dragStart = e.GetPosition(this);
            capturedElement = sender as UIElement;

            if (capturedElement != null)
            {
                capturedElement.CaptureMouse();
                e.Handled = true;
            }
        }

        protected void ChatTopBarSplitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (ChatTopBarSplitterIsDragging)
            {
                Point current = e.GetPosition(this);
                Vector delta = current - dragStart;
                RowDefinition row = ChatArea.RowDefinitions[1];
                double max = row.MaxHeight;
                double min = row.MinHeight;

                double newHeight = row.Height.Value + delta.Y;

                if (newHeight < min)
                    newHeight = min;
                if (newHeight > max)
                    newHeight = max;

                row.Height = new GridLength(newHeight);
                dragStart = current;

                TWR_ORIGINAL_HEIGHT = newHeight;
            }
        }

        protected void ChatTopBarSplitter_Press(object sender, MouseButtonEventArgs e)
        {
            MouseRelease(null, null);
            ChatTopBarSplitterIsDragging = true;
            dragStart = e.GetPosition(this);
            capturedElement = sender as UIElement;

            if (capturedElement != null)
            {
                capturedElement.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MouseRelease(object sender, MouseButtonEventArgs e)
        {
            if (SkypeSplitterIsDragging || ChatTopBarSplitterIsDragging)
            {
                SkypeSplitterIsDragging = false;
                ChatTopBarSplitterIsDragging = false;

                if (capturedElement != null && capturedElement.IsMouseCaptured)
                {
                    capturedElement.ReleaseMouseCapture();
                }
                capturedElement = null;
                if (e != null)
                    e.Handled = true;
            }
        }

        #endregion

        #region Event handlers

        private void Main_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SidebarColumn.MaxWidth = this.ActualWidth / 2;
            if (screen != null && location.ChatToggle)
                TopbarWindowRow.MaxHeight = ChatArea.ActualHeight * 0.7;
            RefreshChatSendButton();
        }

        private void RefreshChatSendButton()
        {
            // TODO: Don't rely on this. Rely on chat width. Also use Width, not ActualWidth. Maybe this is why the thing is laggy?
            MessageWindow.UpdateLayout();
            if (MessageWindow.ActualWidth <= 720 && MessageWindow.ActualWidth != 0)
            {
                SendMsgButton.Text = "";
                SendMsgButton.Source = sendBtnSmall;
            }
            else
            {
                SendMsgButton.Text = Universal.Lang["sZAPBUTTON_SENDMESSAGE"];
                SendMsgButton.Source = sendBtnFull;
            }
        }

        // Date headers in the Conversations tab are list items too; they get no menu.
        // Edit Character is only offered for characters (not e.g. Echo).
        protected void ConversationItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var element = sender as FrameworkElement;
            var conversation = element?.DataContext as Conversation;
            if (conversation == null)
            {
                e.Handled = true;
                return;
            }
            // Edit Character, Export Character and Remove Contact are only for characters; Edit and
            // Remove Group only for groups. The separator comes before whichever Remove is shown.
            Visibility characterOnly = vmodel.IsCharacter(conversation) ? Visibility.Visible : Visibility.Collapsed;
            Visibility groupOnly = vmodel.IsGroup(conversation) ? Visibility.Visible : Visibility.Collapsed;
            if (element.ContextMenu != null)
                foreach (object item in element.ContextMenu.Items)
                {
                    string header = (item as MenuItem)?.Header as string;
                    if (item is Separator)
                        ((Control)item).Visibility = characterOnly == Visibility.Visible ? characterOnly : groupOnly;
                    else if (header == "Edit Character..." || header == "Export Character..." || header == "Remove Contact...")
                        ((Control)item).Visibility = characterOnly;
                    else if (header == "Edit Group..." || header == "Remove Group...")
                        ((Control)item).Visibility = groupOnly;
                }
        }

        protected void EditGroup_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ShowEditGroupWindow(conversation);
        }

        protected void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ConfirmRemoveGroup(conversation);
        }

        protected void RemoveContact_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ConfirmRemoveContact(conversation);
        }

        protected void ExportCharacter_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ExportCharacter(conversation);
        }

        protected void EditCharacter_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ShowEditCharacterWindow(conversation);
        }

        private static Message MenuMessage(object sender)
        {
            var menu = sender as ContextMenu ?? (sender as FrameworkElement)?.Parent as ContextMenu;
            return (menu?.PlacementTarget as FrameworkElement)?.DataContext as Message;
        }

        protected void MessageContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            Message message = MenuMessage(menu);
            bool edit = vmodel.CanEditMessage(message);
            bool regenerate = vmodel.CanRegenerate(message);
            foreach (object item in menu.Items)
            {
                var element = item as FrameworkElement;
                if (element == null)
                    continue;
                switch (element.Tag as string)
                {
                    case "MsgEditItem":
                    case "MsgRemoveItem":
                        element.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "MsgRegenerateItem":
                        element.Visibility = regenerate ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "MsgEditSeparator":
                        element.Visibility = edit || regenerate ? Visibility.Visible : Visibility.Collapsed;
                        break;
                }
            }
        }

        protected void MessageCopy_Click(object sender, RoutedEventArgs e) => vmodel.CopyMessage(MenuMessage(sender));
        protected void MessageEdit_Click(object sender, RoutedEventArgs e) => vmodel.EditMessage(MenuMessage(sender));
        protected void MessageRemove_Click(object sender, RoutedEventArgs e) => vmodel.RemoveMessage(MenuMessage(sender));
        protected void MessageRegenerate_Click(object sender, RoutedEventArgs e) => vmodel.RegenerateReply(MenuMessage(sender));

        protected void ClearChatHistory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Conversation conversation)
                vmodel.ConfirmClearHistory(conversation);
        }

        protected void ConversationList_ItemClicked(object sender, MouseButtonEventArgs e)
        {
            var selected = ((ListBox)sender).SelectedItem;
            if (selected != null && selected is Metadata)
                SelectedContact = (Metadata)selected;
            if (selected is DateHeaderItem)
            {
                ((ListBox)sender).SelectedItem = null;
                return;
            }
            HandleConversationSelection(selected);
        }

        // Options > General "When I double-click on a contact start a call". The first click
        // already opened the chat (ConversationList_ItemClicked).
        protected void ConversationItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!Settings.DoubleClickStartsCall || e.ChangedButton != MouseButton.Left)
                return;
            if (!((sender as FrameworkElement)?.DataContext is Conversation conversation))
                return;
            e.Handled = true;
            if (screen != null)
                return; // already on a call
            if (vmodel.SelectedConversation != conversation)
                HandleConversationSelection(conversation);
            InitiateCall(conversation);
        }

        protected void StatusArea_Click(object sender, MouseButtonEventArgs e)
        {
            OpenStatusMenu();
        }


        protected void MakeGroup_Click(object sender, MouseButtonEventArgs e)
        {
            vmodel.ShowNewGroupWindow();
        }

        protected void CallDropdown_Click(object sender, MouseButtonEventArgs e)
        {
            Universal.NotImplemented("Call options");
        }

        protected void SidebarTab_BtnDown(object sender, MouseButtonEventArgs e)
        {
            SelectTab(sender as SliceControl);
        }



        protected void Window_Activated(object sender, EventArgs e)
        {
            HandleWindowActivated();
        }

        protected void Window_Deactivated(object sender, EventArgs e)
        {
            HandleWindowDeactivated();
        }

        protected void StatusMenuItemClick(object sender, RoutedEventArgs e)
        {
            HandleStatusItemClick(sender as MenuItem);
        }

        protected void Main_Closing(object sender, CancelEventArgs ev)
        {
            if (!noCloseEvent)
                Universal.Hide(ev);
        }

        protected override void OnClosed(EventArgs e)
        {
            SharedServices.SavePositioning(this, SidebarColumn);
        }

        protected void OnClose(object sender, RoutedEventArgs e)
        {
            Universal.Close();
        }

        protected void OnContacts(object sender, RoutedEventArgs e)
        {
            SelectTab(btnContacts);
        }

        protected void OnRecent(object sender, RoutedEventArgs e)
        {
            SelectTab(btnRecents);
        }

        protected void OnHome(object sender, RoutedEventArgs e) => SetWindow(WindowType.Home);

        protected void OnOptions(object sender, RoutedEventArgs e)
        {
            new Options().Show();
        }

        protected void OnAIServices(object sender, RoutedEventArgs e)
        {
            AIServices.ShowWindow();
        }

        protected void OnConnectionWizard(object sender, RoutedEventArgs e)
        {
            ConnectionWizard.ShowWizard(this);
        }

        protected void OnAbout(object sender, RoutedEventArgs e)
        {
            new About().Show();
        }

        protected void OnPrivacyPolicy(object sender, RoutedEventArgs e)
        {
            Universal.OpenUrl(Universal.SKYLARPING_WEBSITE_PRIVACY);
        }

        protected void OnCall(object sender, RoutedEventArgs e) => CallButtonClick(null, null);

        protected void OnAddContact(object sender, RoutedEventArgs e) => AddContact_Click(null, null);

        protected void OnSignOut(object sender, RoutedEventArgs e) => InitiateSignOut();


        protected async void OnChangePicture(object sender, RoutedEventArgs e) => await ProfilePicture.Change(this);

        protected async void OnRemovePicture(object sender, RoutedEventArgs e) => await ProfilePicture.Remove();

        protected async void SidebarAvatar_Click(object sender, MouseButtonEventArgs e) => await ProfilePicture.Change(this);


        protected void OnSwitchUser(object sender, RoutedEventArgs e) => InitiateSignOut(true);


        protected void AddContact_Click(object sender, MouseButtonEventArgs e)
        {
            vmodel.ShowAddContactWindow();
        }

        protected async void OnMsgSendClickButton(object sender, MouseButtonEventArgs e)
        {
            await SendMessage();
        }

        protected void ConversationItemsList_Loaded(object sender, RoutedEventArgs e)
        {
            HandleConversationItems();
        }

        protected void SearchBox_Focused(object sender, KeyboardFocusChangedEventArgs e)
        {
            PseudoSearchBox.SetState(ButtonVisualState.Pressed);
            SharedServices.RemovePlaceholder(SearchBox);
        }

        protected void SearchBox_Unfocused(object sender, KeyboardFocusChangedEventArgs e)
        {
            PseudoSearchBox.SetState(ButtonVisualState.Default);
            SharedServices.SetPlaceholder(SearchBox, Universal.Lang["sCONTACT_QF_HINT"]);
        }

        protected void MessageTextBox_Focused(object sender, KeyboardFocusChangedEventArgs e)
        {
            SharedServices.RemovePlaceholder(MessageTextBox);
            if (SendMsgButton != null) SendMsgButton.IsEnabled = SharedServices.CheckIfMessageSendable(MessageTextBox);
        }

        protected void MessageTextBox_Unfocused(object sender, KeyboardFocusChangedEventArgs e)
        {
            MentionPopup.IsOpen = false;
            CheckIfMTBUnfocused(true);
        }

        protected async void MessageTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (MentionPopup.IsOpen && HandleMentionKey(e.Key))
            {
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter)
                return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return;

            e.Handled = true;
            await SendMessage();
        }

        protected void WindowArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Keyboard.ClearFocus();
        }

        private void SelfInfoChanged(object sender, PropertyChangedEventArgs e)
        {
            var cu = sender as User; // TODO delay ish fix
            if (e == null)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(cu.ConnectionStatus);
                    Tray.SetStatus(cu.ConnectionStatus);
                    ShowCaption(cu.DisplayName);
                    ShowOwnName(cu.DisplayName);
                    ShowStatusText();
                    this.Title = Settings.BrandingName + "\u2122 - " + Universal.CurrentUser?.Username;
                    SidebarAvatar.InvalidateVisual();
                });
            }
            else
                switch (e.PropertyName)
                {
                    case nameof(User.ConnectionStatus):
                        Dispatcher.Invoke(() =>
                        {
                            StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(cu.ConnectionStatus);
                            Tray.SetStatus(cu.ConnectionStatus);
                        });
                        break;
                    case nameof(User.DisplayName):
                        Dispatcher.Invoke(() =>
                        {
                            ShowCaption(cu.DisplayName);
                            ShowOwnName(cu.DisplayName);
                            this.Title = Settings.BrandingName + "\u2122 - " + Universal.CurrentUser?.Username;
                        });
                        break;
                    case nameof(User.Status):
                        Dispatcher.Invoke(ShowStatusText);
                        break;
                    case nameof(User.Avatar):
                        Dispatcher.Invoke(() =>
                            SidebarAvatar.InvalidateVisual()
                        );
                        break;
                }
        }

        #region Status message box

        // TODO: lang
        private const string StatusPlaceholder = "Add video or write a message for your friends to see.";
        private bool _moodBoxShowsPlaceholder = true;
        private bool _moodBoxCancelEdit;

        /// <summary>Shows the user's status message, or the placeholder hint when there is none.</summary>
        private void ShowStatusText()
        {
            if (MoodBox.IsKeyboardFocusWithin)
                return; // don't clobber what the user is typing
            string status = Universal.CurrentUser?.Status;
            _moodBoxShowsPlaceholder = string.IsNullOrEmpty(status);
            MoodBox.Text = _moodBoxShowsPlaceholder ? StatusPlaceholder : status;
            MoodBox.SetResourceReference(
                ForegroundProperty,
                _moodBoxShowsPlaceholder ? "Text.LowContrast" : "Text.HighContrast");
        }

        protected void MoodBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _moodBoxCancelEdit = false;
            if (_moodBoxShowsPlaceholder)
                MoodBox.Text = string.Empty;
            MoodBox.SetResourceReference(ForegroundProperty, "Text.HighContrast");
            MoodBox.SelectAll();
        }

        protected async void MoodBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var user = Universal.CurrentUser;
            string oldStatus = user?.Status ?? string.Empty;
            string newStatus = MoodBox.Text.Trim();

            if (user != null && !_moodBoxCancelEdit && newStatus != oldStatus)
            {
                bool saved = false;
                try { saved = await Universal.Backend.SetMood(newStatus); }
                catch (Exception ex) { Debug.WriteLine("SetMood failed: " + ex); }
                if (!saved)
                    Universal.ShowMessage("Your status message couldn't be saved.", "Status message", WindowBase.IconType.Error);
            }
            ShowStatusText();
        }

        protected void MoodBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Escape)
                return;
            _moodBoxCancelEdit = e.Key == Key.Escape;
            e.Handled = true;
            Keyboard.ClearFocus(); // commits (or discards) via LostKeyboardFocus
        }

        #endregion

        protected void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_conversationOpening) return;
            if (SendMsgButton != null) SendMsgButton.IsEnabled = SharedServices.CheckIfMessageSendable(MessageTextBox);
            if (SharedServices.HasAnyContent(MessageTextBox))
                vmodel.lastTypingActivity = DateTime.UtcNow;
            UpdateMentionPopup();
        }

        #region @ tagging

        // What's typed after an @ at the caret, or null if the caret isn't in a tag.
        private string MentionQuery()
        {
            string before = MessageTextBox.CaretPosition.GetTextInRun(LogicalDirection.Backward);
            int at = before.LastIndexOf('@');
            if (at < 0 || (at > 0 && !char.IsWhiteSpace(before[at - 1])))
                return null;
            return before.Substring(at + 1);
        }

        private void UpdateMentionPopup()
        {
            if (vmodel == null)
                return; // the box fires TextChanged while the window is still being built
            var group = vmodel.SelectedConversation as Group;
            string query = group == null ? null : MentionQuery();
            List<User> matches = query == null ? new List<User>()
                : group.Members.Where(m => Characters.GroupTurns.MatchesTag(m.DisplayName, query)).ToList();
            if (matches.Count == 0)
            {
                MentionPopup.IsOpen = false;
                return;
            }
            MentionList.ItemsSource = matches;
            MentionList.SelectedIndex = 0;
            MentionPopup.IsOpen = true;
        }

        private bool HandleMentionKey(Key key)
        {
            int count = MentionList.Items.Count;
            switch (key)
            {
                case Key.Down:
                    MentionList.SelectedIndex = (MentionList.SelectedIndex + 1) % count;
                    MentionList.ScrollIntoView(MentionList.SelectedItem);
                    return true;
                case Key.Up:
                    MentionList.SelectedIndex = (MentionList.SelectedIndex + count - 1) % count;
                    MentionList.ScrollIntoView(MentionList.SelectedItem);
                    return true;
                case Key.Enter:
                case Key.Tab:
                    AcceptMention(MentionList.SelectedItem as User);
                    return true;
                case Key.Escape:
                    MentionPopup.IsOpen = false;
                    return true;
            }
            return false;
        }

        protected void MentionList_Click(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(MentionList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null)
                return;
            e.Handled = true;
            AcceptMention(item.DataContext as User);
        }

        // Replaces the @ and what was typed after it with the member's name, in bold.
        private void AcceptMention(User user)
        {
            MentionPopup.IsOpen = false;
            string query = MentionQuery();
            if (user == null || query == null)
                return;
            TextPointer caret = MessageTextBox.CaretPosition;
            new TextRange(caret.GetPositionAtOffset(-(query.Length + 1)), caret).Text = string.Empty;

            var tag = new Run("@" + user.DisplayName, MessageTextBox.CaretPosition) { FontWeight = FontWeights.Bold };
            var space = new Run(" ") { FontWeight = FontWeights.Normal };
            tag.SiblingInlines.InsertAfter(tag, space);
            MessageTextBox.CaretPosition = space.ElementEnd;
            MessageTextBox.Focus();
        }

        #endregion

        private void OnCallSpeakerChanged(string memberId)
        {
            Dispatcher.BeginInvoke(new Action(() => screen?.ShowSpeaking(memberId)));
        }

        protected void CallPhones_Click(object sender, MouseButtonEventArgs e)
        {
            vmodel.ShowCallPhones();
        }


        protected void CallButtonClick(object sender, MouseButtonEventArgs e)
        {
            InitiateCall(vmodel.SelectedConversation);
        }

        protected void EmojiButton_Click(object sender, MouseButtonEventArgs e)
        {
            EmojiFlyout.IsOpen = true;
        }

        #endregion

        #region Message sending

        private async Task SendMessage(string message = null)
        {
            if (!SendMsgButton.IsEnabled && message == null)
                return;

            string message_body = message ?? SharedServices.ExtractText(MessageTextBox);

            MessageTextBox.Document.Blocks.Clear();
            MessageTextBox.Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            CheckIfMTBUnfocused();

            await vmodel.SendMessage(message_body);
        }

        private void CheckIfMTBUnfocused(bool force = false)
        {
            if (!MessageTextBox.IsKeyboardFocused || force)
            {
                if (!SharedServices.HasAnyContent(MessageTextBox))
                {
                    SharedServices.SetPlaceholder(MessageTextBox, PlaceholderTextMTB);
                }
                if (SendMsgButton != null) SendMsgButton.IsEnabled = SharedServices.CheckIfMessageSendable(MessageTextBox);
            }
        }

        #endregion

        #region Calls

        private Frame frame;
        private CallScreen screen;
        private CallScreen.LocationChangeEventArgs location;
        private CallScreen.LocationChangeEventArgs initial_location =
            new CallScreen.LocationChangeEventArgs(Settings.HideLeftHandSide != true, false);

        private async void InitiateCall(Conversation conversation, bool is_answering_call = false)
        {
            if (!vmodel.CheckCallEligibility(conversation))
                return;

            if (TWR_ORIGINAL_HEIGHT == default)
                TWR_ORIGINAL_HEIGHT = TopbarWindowRow.Height.Value;
            if (SIDEBAR_SPLITTER_ORIGINAL_WIDTH == default)
                SIDEBAR_SPLITTER_ORIGINAL_WIDTH = SkypeSplitterColumn.Width;
            if (SIDEBAR_ORIGINAL_MINWIDTH == default)
                SIDEBAR_ORIGINAL_MINWIDTH = SidebarColumn.MinWidth;
            if (TWR_ORIGINAL_MINHEIGHT == default)
                TWR_ORIGINAL_MINHEIGHT = TopbarWindowRow.MinHeight;
            if (TWR_ORIGINAL_MAXHEIGHT_CALLS == default)
                TWR_ORIGINAL_MAXHEIGHT_CALLS = TopbarWindowRow.MaxHeight;
            if (CBR_ORIGINAL_HEIGHT == default)
                CBR_ORIGINAL_HEIGHT = ChatButtonRow.Height.Value;
            SIDEBAR_ORIGINAL_WIDTH = SidebarColumn.Width.Value;
            TopbarWindowRow.MinHeight = 250;
            TopbarWindowRow.MaxHeight = ChatArea.ActualHeight * 0.7;
            TopbarWindowRow.Height = new GridLength(ChatArea.ActualHeight * 0.7); // TODO: Retain this across reboots and sessions
            ChatButtonRow.Height = new GridLength(0);

            screen = new CallScreen(conversation, initial_location, is_answering_call);
            vmodel.CallConversationId = conversation.Identifier;
            screen.HangUpRequested += OnHangUp;
            screen.LocationChangeRequested += OnLocationChanged;
            if (Universal.Backend != null)
                Universal.Backend.CallSpeakerChanged += OnCallSpeakerChanged;
            frame = new Frame();
            frame.Navigate(screen);
            SetCallPageLocation(initial_location);

            await screen.StartCall();
        }

        private void OnLocationChanged(object sender, CallScreen.LocationChangeEventArgs e)
        {
            SetCallPageLocation(e);
        }

        private void OnHangUp(object sender, EventArgs e)
        {
            SetCallPageLocation(null);
        }

        private void SetCallPageLocation(CallScreen.LocationChangeEventArgs location, bool storeChatStatus = true)
        {
            if (frame != null)
            {
                frame.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                frame.VerticalContentAlignment = VerticalAlignment.Stretch;
                frame.HorizontalAlignment = HorizontalAlignment.Stretch;
                frame.VerticalAlignment = VerticalAlignment.Stretch;
            }

            if (location == null)
            {
                Settings.HideLeftHandSide = this.location.SidebarToggle ? false : true;
                Settings.Save();
                SetCallPageLocation(new CallScreen.LocationChangeEventArgs(true, true), false); // quickly reset stuff
                if (FillWindowHost.Content == frame)
                    FillWindowHost.Content = null;
                if (FillMessagePanelHost.Content == frame)
                    FillMessagePanelHost.Content = null;
                ChatProfileArea.Visibility = Visibility.Visible;
                FillMessagePanelHost.Visibility = Visibility.Collapsed;
                TopbarWindowRow.MinHeight = TWR_ORIGINAL_MINHEIGHT;
                TopbarWindowRow.MaxHeight = TWR_ORIGINAL_MAXHEIGHT_CALLS;
                TopbarWindowRow.Height = new GridLength(TWR_ORIGINAL_HEIGHT);
                ChatButtonRow.Height = new GridLength(CBR_ORIGINAL_HEIGHT);
                if (screen != null)
                {
                    screen.HangUpRequested -= OnHangUp;
                    screen.LocationChangeRequested -= OnLocationChanged;
                    if (Universal.Backend != null)
                        Universal.Backend.CallSpeakerChanged -= OnCallSpeakerChanged;
                    screen.Visibility = Visibility.Collapsed;
                    frame.Visibility = Visibility.Collapsed;
                    screen = null;
                    vmodel.CallConversationId = null;
                    frame.Content = null;
                    frame = null;
                }
                this.location = null;
                SetWindow(WindowType.Chat, true);
            }
            else
            {
                this.location = location;
                // Disable it here (bugfix)
                ChatProfileArea.Visibility = Visibility.Collapsed;
                // Show ContentArea here, as it is visible in all cases except with both sidebar and chat hidden
                ContentArea.Visibility = Visibility.Visible;
                // Same for this, but with chat hidden only now
                ChatTopBarSplitter.Visibility = Visibility.Collapsed;
                ChatTopbarSplitterRow.MaxHeight = 0;

                if (storeChatStatus)
                    initial_location.ChatToggle = location.ChatToggle;

                if (FillWindowHost.Content == frame)
                    FillWindowHost.Content = null;
                if (FillMessagePanelHost.Content == frame)
                    FillMessagePanelHost.Content = null;

                // Show sidebar
                if (location.SidebarToggle)
                {
                    SidebarColumn.Width = new GridLength(SIDEBAR_ORIGINAL_WIDTH);
                    SidebarColumn.MinWidth = SIDEBAR_ORIGINAL_MINWIDTH;
                    SkypeSplitterColumn.Width = SIDEBAR_SPLITTER_ORIGINAL_WIDTH;
                }
                // Show chat
                if (location.ChatToggle)
                {
                    if (TWR_HEIGHT_CALLS_CHAT == default)
                        TWR_HEIGHT_CALLS_CHAT = ChatArea.ActualHeight * 0.7;
                    TopbarWindowRow.Height = new GridLength(TWR_HEIGHT_CALLS_CHAT);
                    MessageWindowRow.Height = new GridLength(1, GridUnitType.Star);
                    TopbarWindowRow.MaxHeight = ChatArea.ActualHeight * 0.7;
                    ChatTopBarSplitter.Visibility = Visibility.Visible;
                    ChatTopbarSplitterRow.MaxHeight = int.MaxValue;
                }
                // Show everything
                if (location.SidebarToggle && location.ChatToggle)
                {
                    TopbarWindowRow.Height = new GridLength(TWR_ORIGINAL_HEIGHT);
                    MessageWindowRow.Height = new GridLength(1, GridUnitType.Star);
                    FillMessagePanelHost.Visibility = Visibility.Visible;
                    FillMessagePanelHost.Content = frame;
                    return;
                }
                // Show nothing
                if (!location.SidebarToggle && !location.ChatToggle)
                {
                    ContentArea.Visibility = Visibility.Collapsed;
                    FillWindowHost.Content = frame;
                    return;
                }
                // Hide sidebar
                if (!location.SidebarToggle)
                {
                    SIDEBAR_ORIGINAL_WIDTH = SidebarColumn.Width.Value;
                    SidebarColumn.Width = new GridLength(0);
                    SidebarColumn.MinWidth = 0;
                    SkypeSplitterColumn.Width = new GridLength(0);
                }
                // Hide chat
                else if (!location.ChatToggle)
                {
                    TWR_HEIGHT_CALLS_CHAT = TopbarWindowRow.ActualHeight;
                    TopbarWindowRow.Height = new GridLength(1, GridUnitType.Star);
                    MessageWindowRow.Height = new GridLength(0);
                    TopbarWindowRow.MaxHeight = Double.PositiveInfinity;
                }
                // We fill the message panel in sidebar or chat shown cases
                FillMessagePanelHost.Visibility = Visibility.Visible;
                FillMessagePanelHost.Content = frame;
            }
        }

        #endregion

        #region Conversation

        private void ClearConversation()
        {
            Universal.Backend?.TypingUsersList.Clear();
            ConversationItemsList.ItemsSource = null;
            vmodel.ClearActiveConversation();
        }

        private async Task SetConversation()
        {
            _conversationOpening = true;
            ClearConversation();
            SetWindow(WindowType.Chat);
            PlaceholderTextMTB = Universal.Lang.Format(
                "sCHAT_TYPE_HERE_DIALOG",
                vmodel.SelectedConversation?.DisplayName
            );
            SharedServices.SetPlaceholder(MessageTextBox, PlaceholderTextMTB, true);
            if (SendMsgButton != null) SendMsgButton.IsEnabled = SharedServices.CheckIfMessageSendable(MessageTextBox);
            Spinner.Visibility = Visibility.Visible;

            await vmodel.SetConversation();

            if (vmodel.SelectedConversation == null)
                return;

            ConversationItemsList.ItemsSource = vmodel.ActiveConversation;
            Spinner.Visibility = Visibility.Collapsed;
            ScrollConversationToEnd();
            RefreshChatSendButton();
            _conversationOpening = false;
        }

        /// <summary>
        /// Scrolls the message list to the newest message once layout has settled. With pixel scrolling
        /// (CanContentScroll=False, used so long messages can be read part-way) ScrollToEnd clamps to the
        /// height measured so far, so calling it before the new rows are laid out would leave the view at
        /// the top.
        /// </summary>
        private void ScrollConversationToEnd()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _conversationScrollViewer?.ScrollToEnd()));
        }

        private void HandleConversationItems()
        {
            ConversationItemsList.ApplyTemplate();
            _conversationScrollViewer =
                ConversationItemsList.Template.FindName("ScrollViewer", ConversationItemsList)
                as ScrollViewer;
            // Scrolling is left to the user; the view only jumps to the bottom when a conversation
            // opens or a new message arrives (see ScrollConversationToEnd).
        }

        #endregion

        #region Emoji picker

        private void InitializeEmojiPicker()
        {
            foreach (var (emojiKey, emojiFilename) in vmodel.GetUniqueEmojiList())
            {
                var border = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(1),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = SharedServices.ConvertHexKeyToUnicode(emojiKey),
                };
                try
                {
                    var sliceControl = Formatter.MakeEmoji(emojiFilename);
                    sliceControl.Tag = emojiFilename;
                    border.Child = sliceControl;
                    border.MouseLeftButtonUp += EmojiBox_Click;
                    border.MouseEnter += (s, ev) =>
                        ((Border)s).Background = new SolidColorBrush(
                            Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                        );
                    border.MouseLeave += (s, ev) => ((Border)s).Background = Brushes.Transparent;
                    EmojiWrapPanel.Children.Add(border);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EMOJI] Failed to load emoji: {emojiFilename} - {ex.Message}");
                }
            }
        }

        private void SetEmojiPickerAnimation(bool animate)
        {
            foreach (var child in EmojiWrapPanel.Children)
            {
                if (child is Border border && border.Child is SliceControl sc)
                {
                    sc.IsAnimation = animate;
                }
            }
        }

        private void EmojiBox_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var sliceControlInside = border?.Child as SliceControl;
            if (sliceControlInside == null)
                return;

            EmojiFlyout.IsOpen = false;
            SharedServices.RemovePlaceholder(MessageTextBox);

            string emojiFilename = sliceControlInside.Tag as string;
            var sliceControl = Formatter.MakeEmoji(emojiFilename);

            if (!MessageTextBox.Selection.IsEmpty)
                MessageTextBox.Selection.Text = string.Empty;

            TextPointer caret = MessageTextBox.CaretPosition;
            if (!caret.IsAtInsertionPosition)
                caret = caret.GetInsertionPosition(LogicalDirection.Forward);

            var container = new InlineUIContainer(sliceControl, caret)
            {
                BaselineAlignment = BaselineAlignment.Center,
                Tag = emojiFilename,
            };
            var spaceRun = new Run(" ");
            container.SiblingInlines.InsertAfter(container, spaceRun);
            MessageTextBox.CaretPosition = spaceRun.ElementEnd;
            MessageTextBox.Focus();
            if (SendMsgButton != null) SendMsgButton.IsEnabled = SharedServices.CheckIfMessageSendable(MessageTextBox);
        }

        #endregion

        #region Initialization and closing

        /// <summary>The window's setup. A theme's constructor calls this right after InitializeComponent.</summary>
        protected void InitializeMainWindow()
        {
            noCloseEvent = false;

            Application.Current.MainWindow = this;

            vmodel = new MainViewModel();
            this.DataContext = vmodel;

            vmodel.Ready += (s, e) =>
            {
                ShowOwnName(Universal.CurrentUser.DisplayName);
                StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(
                    Universal.CurrentUser.ConnectionStatus
                );
                ProfileGrid.DataContext = Universal.CurrentUser;
                ShowStatusText();
                SelectTab(StartupTabButton); // the lists are filled now
                WindowTitle = Settings.BrandingName + "™ - " + Universal.CurrentUser.Username;
                this.Title = WindowTitle;
                Universal.CurrentUser.PropertyChanged += (ss, ee) =>
                {
                    if (ee.PropertyName == nameof(User.ConnectionStatus))
                        Dispatcher.Invoke(() => StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(Universal.CurrentUser.ConnectionStatus));
                };
                Universal.CurrentUser.PropertyChanged += SelfInfoChanged;
                _oldUser = Universal.CurrentUser;
                Main_SizeChanged(null, null);
                AutoAway.Start(() => screen != null);
                Ready?.Invoke(this, EventArgs.Empty);
                // First run: set up the language model once the main window is up.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() => ConnectionWizard.ShowIfNeeded(this)));
            };

            vmodel.SignOutRequested += (s, e) =>
            {
                AutoAway.Stop();
                Universal.LoginDispenser(switchUser: e.switchuser).Show();
                noCloseEvent = true;
                Close();
            };

            // Raised when a message is added to the open conversation (not while it's loading):
            // the only time the view scrolls to the bottom besides opening a conversation.
            vmodel.ConversationItemChanged += (s, e) => ScrollConversationToEnd();
            // An edited or still-streaming message grows in place: follow it only if already at the bottom.
            vmodel.ConversationItemUpdated += (s, e) =>
            {
                var viewer = _conversationScrollViewer;
                if (viewer != null && viewer.VerticalOffset >= viewer.ScrollableHeight - 40)
                    ScrollConversationToEnd();
            };
            
            vmodel.ConversationOpened += (s, e) =>
            {
                _oldUser.PropertyChanged -= SelfInfoChanged;
                Universal.CurrentUser.PropertyChanged += SelfInfoChanged;
                _oldUser = Universal.CurrentUser;
                SelfInfoChanged(Universal.CurrentUser, null);
            };

            vmodel.CompactRecentsRefreshRequested += (s, e) =>
            {
                if (ConversationList.Visibility == Visibility.Visible
                && ConversationList.ItemTemplateSelector
                    is CompactRecentsTemplateSelector
            )
                {
                    ConfigureCompactRecentsList();
                }
            };

            vmodel.ConversationChanged += (s, e) =>
            {
                if (!(s is Conversation sc)) return;
                DirectMessage sdm = null;
                if (sc is DirectMessage)
                    sdm = sc as DirectMessage;
                bool found = false;
                foreach (var item in ConversationList.Items)
                    if (item is Conversation c && c.Identifier == sc.Identifier)
                    { ConversationList.SelectedItem = item as Conversation; found = true; break; }
                if (!found)
                {
                    if (ConversationList.ItemsSource == vmodel.ContactList)
                        SelectTab(btnRecents);
                    else
                        SelectTab(btnContacts);
                    foreach (var item in ConversationList.Items)
                        if (item is Conversation c && c.Identifier == sc.Identifier)
                        { ConversationList.SelectedItem = item as Conversation; break; }
                }
                _ = SetConversation();
            };

            vmodel.PropertyChanged += (s, e) =>
            {
                // Set directly: these already arrive on the UI thread, and Dispatcher.Invoke here runs other
                // queued work partway through, so a "show" could land inside a "hide" and lose the name.
                if (e.PropertyName == nameof(MainViewModel.TypingText))
                    TypingIndicatorText.Text = vmodel.TypingText;
                else if (e.PropertyName == nameof(MainViewModel.IsTypingVisible))
                    TypingIndicator.Visibility = vmodel.IsTypingVisible
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            };

            vmodel.IncomingCallAccepted += (e) =>
            {
                InitiateCall(e.Caller, true);
            };

            InitializeFrame();

            Universal.GroupAvatar = GenerateAvatarImage("group");
            Universal.AnonymousAvatar = GenerateAvatarImage("anonymous");
            Universal.UnknownAvatar = GenerateAvatarImage("unknown");

            EmojiFlyout.Opened += (s, e) => SetEmojiPickerAnimation(true);
            EmojiFlyout.Closed += (s, e) => SetEmojiPickerAnimation(false);

            this.MouseLeftButtonUp += MouseRelease;
            this.SizeChanged += Main_SizeChanged;
            buttonToColumn = new Dictionary<SliceControl, ColumnDefinition>
            {
                { btnContacts, ContactsColumn },
                { btnRecents, RecentsColumn },
            };
            SelectTab(StartupTabButton);
            SharedServices.SetPlaceholder(SearchBox, Universal.Lang["sCONTACT_QF_HINT"]);
            InitializeEmojiPicker();

            vmodel.SubscribeTypingIndicator();

            CTR_ORIGINAL_MAXHEIGHT = ChatTopBarRow.MaxHeight;
            TWR_ORIGINAL_MAXHEIGHT = TopbarWindowRow.MaxHeight;
            SetWindow(WindowType.Home);
            RefreshChatSendButton();

            Settings.Default.PropertyChanged += RefreshCreds;
            Universal.Lang.PropertyChanged += RefreshCreds;
            RefreshCreds();


            SourceInitialized += (s, e) =>
            {
                if (Settings.SaveWindowPosition && Settings.Width >= 0.0)
                {
                    this.Top = Settings.Y;
                    this.Left = Settings.X;
                    this.Width = Settings.Width;
                    this.Height = Settings.Height;
                    if (Settings.Maximized && this.WindowState != WindowState.Minimized)
                        this.WindowState = WindowState.Maximized;
                    if (Settings.ConvListWidth > 0)
                        SidebarColumn.Width = new GridLength(Settings.ConvListWidth);
                }
                OnSidebarResized();
            };

            this.AllowsTransparency = false;
        }

        private void InitiateSignOut(bool switchuser = false) => vmodel.InitiateSignOut(switchuser);

        #endregion

        #region Status change menu

        private void OpenStatusMenu()
        {
            var menu = (ContextMenu)StatusArea.Resources["StatusMenu"];
            menu.PlacementTarget = StatusArea;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private async void HandleStatusItemClick(MenuItem item)
        {
            string name = item.Name.Substring(3);
            var currentStatus = MainViewModel.GetStatusFromInt(StatusIcon.DefaultIndex);

            if (name == "dnd")
                vmodel.InformDND();

            PresenceStatus status = vmodel.GetConnectionStatusFromName(name);
            if (status == PresenceStatus.Unknown) return;

            StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(status);
            Tray.SetStatus(status);

            if (!await Universal.Backend.SetConnectionStatus(status))
            {
                StatusIcon.DefaultIndex = MainViewModel.GetIntFromStatus(currentStatus);
                Tray.SetStatus(currentStatus);
            }
        }

        #endregion

        private void RefreshCreds(object sender = null, PropertyChangedEventArgs e = null)
        {
            ShowCreditText(SharedServices.GetCreditText());
        }
    }

}
